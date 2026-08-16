using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Utilities;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Instance.Java;

public static class JavaRuntimeManager
{
    private const int BfsMaxDepth = 8;
    private const int BfsMaxDirsPerRoot = 50_000;
    private const int FullExploreDepth = 3;

    private static readonly string[] DirNameKeywords =
    [
        "java", "jdk", "jre", "dragonwell", "azul", "zulu", "oracle", "open",
        "amazon", "corretto", "eclipse", "temurin", "hotspot", "semeru", "kona",
        "bellsoft", "liberica", "graal", "sdkman", "environment", "env", "runtime",
        "x86_64", "amd64", "arm64", "minecraft", "launcher", "hmcl", "portal",
        "pcl", "bakaxl", "fluent", "mc", "bin", "tool", "dev", "software", "app",
        "program", "game", "server", "agent", "platform", "engine"
    ];

    private static readonly string[] ExcludedDirNames =
    [
        "javapath", "java8path", "common files", "netease", "node_modules",
        "assets", "libraries", "resourcepacks", "shaderpacks", "screenshots",
        "saves", "logs", "crash-reports", "cache", "mods", "versions", ".git",
        "$recycle.bin", "system volume information", "programdata", "windows",
        "winnt", "temp", "tmp", "debug", "obj", "bin\\debug", "bin\\release",
        "nodejs", "python", "ruby", "go\\pkg", "cargo", "rustup", ".nuget",
        ".gradle", ".m2", ".idea", ".vscode", ".vs", "packages"
    ];

    public static async Task<JavaRuntimeEntry?> FromPathAsync(string javaPath,
        CancellationToken cancellationToken = default)
    {
        var java = await JavaUtil.GetJavaInfoAsync(javaPath, cancellationToken);
        return java == null ? null : Convert(java);
    }

    public static async Task<IReadOnlyList<JavaRuntimeEntry>> ScanAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var result = new List<JavaRuntimeEntry>();
            await foreach (var java in JavaUtil.EnumerableJavaAsync(true, cancellationToken))
                if (java != null)
                    result.Add(Convert(java));

            return (IReadOnlyList<JavaRuntimeEntry>)result;
        }, cancellationToken);
    }

    public static async Task<IReadOnlyList<JavaRuntimeEntry>> DeepScanAsync(
        IProgress<DeepScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new HashSet<JavaRuntimeEntry>();
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalDirsScanned = 0;
        long totalDirsToScan = 0;


        progress?.Report(new DeepScanProgress(0, 0, 0, "阶段 1/2：执行自动扫描（registry + where + 常见路径）…"));
        try
        {
            await foreach (var java in JavaUtil.EnumerableJavaAsync(false, cancellationToken))
                if (java != null)
                {
                    result.Add(Convert(java));
                    foundPaths.Add(java.JavaPath);
                    progress?.Report(new DeepScanProgress(0, 0, result.Count,
                        $"自动扫描找到: {java.JavaVersion}"));
                }
        }
        catch (OperationCanceledException)
        {
        }


        var roots = GetBfsSearchRoots().ToList();
        totalDirsToScan = roots.Count;
        progress?.Report(new DeepScanProgress(
            totalDirsScanned, totalDirsToScan, result.Count,
            $"阶段 2/2：深度扫描磁盘（已找到 {result.Count} 个）…"));

        var tasks = new List<Task>();
        const int collectConcurrency = 4;
        using var semaphore = new SemaphoreSlim(collectConcurrency, collectConcurrency);

        foreach (var root in roots)
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var found = BfsKeywordScan(root, foundPaths,
                        ref totalDirsScanned, ref totalDirsToScan,
                        progress, cancellationToken);
                    foreach (var javaPath in found)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var entry = await FromPathAsync(javaPath, cancellationToken);
                            if (entry != null)
                            {
                                lock (result)
                                {
                                    result.Add(entry);
                                }

                                progress?.Report(new DeepScanProgress(
                                    totalDirsScanned, totalDirsToScan, result.Count,
                                    $"深度扫描找到 Java: {Path.GetFileName(Path.GetDirectoryName(javaPath))}"));
                            }
                        }
                        catch (Exception exception)
                        {
                            Logger.Error($"读取深度扫描发现的 Java 运行时失败：{javaPath}", exception);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }

        progress?.Report(new DeepScanProgress(
            totalDirsScanned, totalDirsToScan, result.Count,
            $"扫描完成，共找到 {result.Count} 个 Java"));

        return result.ToList();
    }

    private static List<string> GetBfsSearchRoots()
    {
        var roots = new List<string>();

        if (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { } home &&
            Directory.Exists(home))
            roots.Add(home);

        if (OperatingSystem.IsWindows())
        {
            if (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) is { } appData &&
                Directory.Exists(appData))
                roots.Add(appData);
            if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { } localAppData &&
                Directory.Exists(localAppData))
                roots.Add(localAppData);

            try
            {
                foreach (var drive in DriveInfo.GetDrives()
                             .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Network))
                {
                    var root = drive.RootDirectory.FullName;
                    if (!Directory.Exists(root)) continue;
                    roots.Add(root);
                }
            }
            catch
            {
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddDirIfExists(roots, "/opt");
            AddDirIfExists(roots, "/usr/local");
            AddDirIfExists(roots, "/opt/homebrew/opt");
            AddDirIfExists(roots, "/usr/local/opt");
            AddDirIfExists(roots, "/Applications");
            AddDirIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        else if (OperatingSystem.IsLinux())
        {
            AddDirIfExists(roots, "/opt");
            AddDirIfExists(roots, "/usr/local");
            try
            {
                foreach (var drive in DriveInfo.GetDrives()
                             .Where(d => d.IsReady && d.DriveType is DriveType.Fixed))
                {
                    var mount = drive.RootDirectory.FullName;
                    if (mount != "/" && Directory.Exists(mount)) roots.Add(mount);
                }
            }
            catch
            {
            }

            foreach (var common in new[] { "/mnt", "/media", "/run/media" })
            {
                if (!Directory.Exists(common)) continue;
                try
                {
                    foreach (var entry in Directory.EnumerateDirectories(common))
                        if (Directory.Exists(entry))
                            roots.Add(entry);
                }
                catch
                {
                }
            }
        }

        return roots
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r)
            .ToList();
    }

    private static void AddDirIfExists(List<string> roots, string path)
    {
        if (Directory.Exists(path)) roots.Add(path);
    }

    private static HashSet<string> BfsKeywordScan(
        string root,
        HashSet<string> foundPaths,
        ref long totalDirsScanned,
        ref long totalDirsToScan,
        IProgress<DeepScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scannedDirs = 0;
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));

        var javaBinName = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
        var javaAltBinName = OperatingSystem.IsWindows() ? "java.exe" : null;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (dir, depth) = queue.Dequeue();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var entryPath in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? name = null;
                try
                {
                    name = Path.GetFileName(entryPath).ToLowerInvariant();
                }
                catch
                {
                    continue;
                }

                if (DirNameExcluded(name)) continue;

                scannedDirs++;
                Interlocked.Increment(ref totalDirsScanned);
                if (scannedDirs > BfsMaxDirsPerRoot) return found;

                if (scannedDirs % 200 == 0)
                    progress?.Report(new DeepScanProgress(
                        Volatile.Read(ref totalDirsScanned),
                        Volatile.Read(ref totalDirsToScan),
                        foundPaths.Count + found.Count,
                        $"正在扫描: {entryPath}"));


                try
                {
                    var binDir = Path.Combine(entryPath, "bin");
                    var javaBin = Path.Combine(binDir, javaBinName);
                    if (File.Exists(javaBin) && foundPaths.Add(javaBin))
                    {
                        found.Add(javaBin);
                    }
                    else if (javaAltBinName != null)
                    {
                        var javaAltBin = Path.Combine(binDir, javaAltBinName);
                        if (File.Exists(javaAltBin) && foundPaths.Add(javaAltBin))
                        {
                            var javaw = Path.Combine(binDir, "javaw.exe");
                            found.Add(File.Exists(javaw) ? javaw : javaAltBin);
                        }
                    }
                }
                catch
                {
                }


                if (depth + 1 < BfsMaxDepth &&
                    (depth + 1 < FullExploreDepth || DirNameMatchesKeywords(name)))
                {
                    queue.Enqueue((entryPath, depth + 1));
                    Interlocked.Increment(ref totalDirsToScan);
                }
            }
        }

        return found;
    }

    private static bool DirNameMatchesKeywords(string name)
    {
        return DirNameKeywords.Any(keyword => name.Contains(keyword, StringComparison.Ordinal));
    }

    private static bool DirNameExcluded(string name)
    {
        return ExcludedDirNames.Any(excluded => name.Contains(excluded, StringComparison.Ordinal));
    }

    private static JavaRuntimeEntry Convert(JavaEntry java)
    {
        return new JavaRuntimeEntry
        {
            JavaPath = java.JavaPath,
            JavaType = java.JavaType,
            JavaVersion = java.JavaVersion,
            MajorVersion = java.MajorVersion,
            Is64Bit = java.Is64bit
        };
    }
}

public record DeepScanProgress(
    long DirectoriesScanned,
    long DirectoriesQueued,
    int JavasFound,
    string CurrentStatus);