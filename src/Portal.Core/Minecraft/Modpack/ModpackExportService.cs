using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Utilities;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Modpack;

public sealed record ModpackExportOptions
{
    public required string PackName { get; init; }
    public required string PackVersion { get; init; }
    public required string PackSummary { get; init; }
    public required IReadOnlyList<string> Rules { get; init; }
    public bool CheckHostedAssets { get; init; } = true;
    public bool ModrinthOnly { get; init; }
    public bool IncludePortalSettings { get; init; } = true;
    public bool IncludePortalIcon { get; init; } = true;
}

public sealed record ModpackExportResult
{
    public required int CopiedFileCount { get; init; }
    public required int HostedFileCount { get; init; }
    public required string OutputPath { get; init; }
}

public sealed record ModpackExportProgress(string Stage, double Progress, string Description);

public sealed class ModpackExportService
{
    private const string ModrinthVersionFilesEndpoint = "https://api.modrinth.com/v2/version_files";
    private const string CurseforgeFingerprintEndpoint = "https://api.curseforge.com/v1/fingerprints/432/";

    private static readonly string[] RootSkipFolders = ["assets", "versions", "libraries"];

    private static readonly string[] GlobalSkipFolders =
    [
        "structureCacheV1", ".fabric", ".git", "avatar-cache", "cosmetic-cache"
    ];

    private static readonly string[] HostedExtensions = [".zip", ".rar", ".jar", ".disabled", ".old"];
    private static readonly string[] HostedPathHints = ["mods", "packs", "openloader", "resource"];

    public static Task<ModpackExportResult> ExportAsync(MinecraftInstance instance, ModpackExportOptions options,
        string outputPath, Action<ModpackExportProgress>? report = null, CancellationToken cancellationToken = default)
    {
        return ExportAsync(instance, options, outputPath, report, null, cancellationToken);
    }

    public static async Task<ModpackExportResult> ExportAsync(MinecraftInstance instance, ModpackExportOptions options,
        string outputPath, Action<ModpackExportProgress>? report, IProgress<double>? progressReporter,
        CancellationToken cancellationToken = default)
    {
        var gameRoot = instance.GetJavaGameDirectory().TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            throw new InvalidOperationException("游戏目录不存在，无法导出整合包。");

        var cacheFolder = Path.Combine(Path.GetTempPath(), "Portal", "ModpackExport",
            $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}");
        var modpackRoot = Path.Combine(cacheFolder, "modpack");
        var overridesFolder = Path.Combine(modpackRoot, "overrides");
        Directory.CreateDirectory(overridesFolder);

        try
        {
            report?.Invoke(new ModpackExportProgress("copy", 0.1, "正在复制文件"));
            var copyResult = CopyFilesAsync(gameRoot, options.Rules, overridesFolder, report, cancellationToken);

            if (options.IncludePortalSettings)
                CopyPortalSettings(instance, overridesFolder);

            if (options.IncludePortalIcon)
                CopyPortalIcon(instance, overridesFolder);

            var filesIndex = new JsonArray();
            if (options.CheckHostedAssets)
            {
                report?.Invoke(new ModpackExportProgress("fetch", 0.5, "正在从平台获取下载地址"));
                await FetchHostedDownloadsAsync(copyResult.HostedFiles, filesIndex, overridesFolder,
                    options.ModrinthOnly,
                    report, cancellationToken);
            }

            report?.Invoke(new ModpackExportProgress("archive", 0.85, "正在生成整合包"));
            var mrpackPath = await Task.Run(() => BuildArchive(instance, options, filesIndex, modpackRoot, outputPath),
                cancellationToken);
            report?.Invoke(new ModpackExportProgress("done", 1.0, "导出完成"));
            progressReporter?.Report(1.0);

            return new ModpackExportResult
            {
                CopiedFileCount = copyResult.CopiedCount,
                HostedFileCount = copyResult.HostedFiles.Count,
                OutputPath = mrpackPath
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(cacheFolder))
                    Directory.Delete(cacheFolder, true);
            }
            catch (Exception exception)
            {
                Logger.Warning($"[Export] 清理临时目录失败：{exception}");
            }
        }
    }

    private static void CopyPortalSettings(MinecraftInstance instance, string overridesFolder)
    {
        var configPath = instance.GetConfigPath();
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return;

        try
        {
            var targetDirectory = Path.Combine(overridesFolder, "Portal");
            Directory.CreateDirectory(targetDirectory);
            File.Copy(configPath, Path.Combine(targetDirectory, MinecraftInstance.PortablePortalConfigFileName), true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Export] 复制 Portal 实例设置失败：{exception}");
        }
    }

    private static void CopyPortalIcon(MinecraftInstance instance, string overridesFolder)
    {
        var iconPath = instance.GetExportIconPath();
        if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
            return;

        try
        {
            var targetDirectory = Path.Combine(overridesFolder, "Portal");
            Directory.CreateDirectory(targetDirectory);
            File.Copy(iconPath, Path.Combine(targetDirectory, MinecraftInstance.PortablePortalIconFileName), true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Export] 复制 Portal 实例图标失败：{exception}");
        }
    }

    private static CopyResult CopyFilesAsync(string gameRoot, IReadOnlyList<string> rules,
        string overridesFolder, Action<ModpackExportProgress>? report, CancellationToken cancellationToken)
    {
        var hostedFiles = new List<ModFileInfo>();
        var copiedCount = 0;
        var progress = 0;

        void SearchFolder(DirectoryInfo folder)
        {
            foreach (var subFolder in folder.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(folder.FullName, gameRoot, StringComparison.OrdinalIgnoreCase) &&
                    RootSkipFolders.Contains(subFolder.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (GlobalSkipFolders.Contains(subFolder.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                SearchFolder(subFolder);
            }

            foreach (var entry in folder.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(gameRoot, entry.FullName);

                var shouldKeep = false;
                foreach (var rule in rules)
                {
                    var revert = rule.StartsWith("!");
                    if (ModpackGlobMatcher.Like(relativePath, rule.TrimStart('!')))
                        shouldKeep = !revert;
                }

                if (!shouldKeep)
                    continue;

                var targetPath = Path.Combine(overridesFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(entry.FullName, targetPath, true);
                copiedCount++;

                if (HostedExtensions.Contains(entry.Extension.ToLowerInvariant()) &&
                    HostedPathHints.Any(hint => relativePath.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                    hostedFiles.Add(new ModFileInfo { Path = targetPath });

                if (++progress % 25 == 0)
                    report?.Invoke(new ModpackExportProgress("copy", 0, $"已复制 {progress} 个文件"));
            }
        }

        SearchFolder(new DirectoryInfo(gameRoot));
        return new CopyResult(hostedFiles, copiedCount);
    }

    private static async Task FetchHostedDownloadsAsync(IReadOnlyList<ModFileInfo> hostedFiles,
        JsonArray filesIndex, string overridesFolder, bool modrinthOnly, Action<ModpackExportProgress>? report,
        CancellationToken cancellationToken)
    {
        if (hostedFiles.Count == 0)
            return;

        using var hashSemaphore = new SemaphoreSlim(4, 4);
        await Task.WhenAll(hostedFiles.Select(async modFile =>
        {
            await hashSemaphore.WaitAsync(cancellationToken);
            try
            {
                var hashes = await Task.Run(() => ModService.ComputeHashes(modFile.Path, cancellationToken),
                    cancellationToken);
                modFile.Sha1 = hashes.Sha1;
                modFile.Fingerprint = hashes.Fingerprint;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                hashSemaphore.Release();
            }
        }));

        var downloads = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var sha1List = hostedFiles.Where(f => f.Sha1 is not null).Select(f => f.Sha1!).ToArray();
        if (sha1List.Length > 0)
            try
            {
                var modrinthRaw = await HttpUtil.Request(ModrinthVersionFilesEndpoint)
                    .PostJsonAsync(new { hashes = sha1List, algorithm = "sha1" }, cancellationToken: cancellationToken)
                    .ReceiveJson<JsonObject>();
                foreach (var modFile in hostedFiles)
                {
                    if (modFile.Sha1 is not { } sha1) continue;
                    if (!modrinthRaw.TryGetPropertyValue(sha1, out var versionNode) ||
                        versionNode is not JsonObject version)
                        continue;
                    var files = version["files"] as JsonArray;
                    var url = files?[0]?["url"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    AddDownloadUrl(downloads, modFile.Path, url);
                }
            }
            catch (Exception exception) when (exception is FlurlHttpException or JsonException)
            {
                Logger.Warning($"[Export] 从 Modrinth 获取下载地址失败：{exception.Message}");
            }

        if (!modrinthOnly && CredentialsService.CurseForgeApiKey is { } apiKey)
        {
            var fingerprints = hostedFiles.Where(f => f.Fingerprint is not null).Select(f => f.Fingerprint!.Value)
                .ToArray();
            if (fingerprints.Length > 0)
                try
                {
                    var curseRaw = await HttpUtil.Request(CurseforgeFingerprintEndpoint)
                        .WithHeader("x-api-key", apiKey)
                        .PostJsonAsync(new { fingerprints }, cancellationToken: cancellationToken)
                        .ReceiveJson<JsonObject>();
                    var exactMatches = curseRaw["data"]?["exactMatches"] as JsonArray;
                    if (exactMatches != null)
                        foreach (var match in exactMatches)
                        {
                            var file = match?["file"];
                            var downloadUrl = file?["downloadUrl"]?.GetValue<string>();
                            if (string.IsNullOrWhiteSpace(downloadUrl)) continue;
                            var fingerprint = file?["fileFingerprint"]?.GetValue<uint>();
                            var modFile = hostedFiles.FirstOrDefault(f => f.Fingerprint == fingerprint);
                            if (modFile is null) continue;
                            AddDownloadUrl(downloads, modFile.Path, HandleCurseForgeDownloadUrls(downloadUrl));
                        }
                }
                catch (Exception exception) when (exception is FlurlHttpException or IOException or JsonException)
                {
                    Logger.Warning($"[Export] 从 CurseForge 获取下载地址失败：{exception.Message}");
                }
        }

        foreach (var modFile in hostedFiles)
        {
            if (!downloads.TryGetValue(modFile.Path, out var urls))
                continue;

            var relativePath = Path.GetRelativePath(overridesFolder, modFile.Path).Replace("\\", "/");
            filesIndex.Add(new JsonObject
            {
                ["path"] = relativePath,
                ["hashes"] = new JsonObject
                {
                    ["sha1"] = modFile.Sha1,
                    ["sha512"] = ComputeSha512(modFile.Path)
                },
                ["downloads"] = new JsonArray(urls
                    .OrderByDescending(u => u.Contains("modrinth.com", StringComparison.OrdinalIgnoreCase))
                    .Select(u => (JsonNode)u).ToArray()),
                ["fileSize"] = new FileInfo(modFile.Path).Length
            });
            File.Delete(modFile.Path);
        }
    }

    private static void AddDownloadUrl(Dictionary<string, List<string>> downloads, string filePath, string url)
    {
        if (!downloads.TryGetValue(filePath, out var list))
            downloads[filePath] = list = [];
        list.Add(url);
    }

    internal static string HandleCurseForgeDownloadUrls(string url)
    {
        return url
            .Replace("-service.overwolf.wtf", ".forgecdn.net")
            .Replace("://media.", "://edge.")
            .Replace("://mediafilez.", "://edge.");
    }

    private static string ComputeSha512(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA512.HashData(stream)).ToLowerInvariant();
    }

    private static string BuildArchive(MinecraftInstance instance, ModpackExportOptions options,
        JsonArray filesIndex, string modpackRoot, string outputPath)
    {
        var minecraftVersion = instance.MinecraftEntry is ModifiedMinecraftEntry { InheritedMinecraft: { } inherited }
            ? inherited.Version.VersionId
            : instance.VersionId;
        var dependencies = new JsonObject { ["minecraft"] = minecraftVersion };
        if (instance.MinecraftEntry is ModifiedMinecraftEntry modified)
            foreach (var loader in modified.ModLoaders)
                switch (loader.Type)
                {
                    case ModLoaderType.Forge:
                        dependencies["forge"] = loader.Version;
                        break;
                    case ModLoaderType.NeoForge:
                        dependencies["neoforge"] = loader.Version;
                        break;
                    case ModLoaderType.Fabric:
                        dependencies["fabric-loader"] = loader.Version;
                        break;
                    case ModLoaderType.Quilt:
                        dependencies["quilt-loader"] = loader.Version;
                        break;
                }

        var index = new JsonObject
        {
            ["game"] = "minecraft",
            ["formatVersion"] = 1,
            ["versionId"] = options.PackVersion,
            ["name"] = options.PackName,
            ["summary"] = options.PackSummary,
            ["files"] = filesIndex,
            ["dependencies"] = dependencies
        };

        File.WriteAllText(Path.Combine(modpackRoot, "modrinth.index.json"),
            index.ToJsonString(new JsonSerializerOptions()));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
            File.Delete(outputPath);
        ZipFile.CreateFromDirectory(modpackRoot, outputPath);
        return outputPath;
    }

    private sealed record CopyResult(List<ModFileInfo> HostedFiles, int CopiedCount);

    private sealed class ModFileInfo
    {
        public required string Path { get; init; }
        public string? Sha1 { get; set; }
        public uint? Fingerprint { get; set; }
    }
}