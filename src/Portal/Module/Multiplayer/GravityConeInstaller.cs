using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Portal.Const;
using Portal.Module.Update;

namespace Portal.Module.Multiplayer;

public static class GravityConeInstaller
{
    public const string ManifestUrl = "https://cdn.tiouo.xyz/portal/gravitycone.json";
    public const string GravityConeVersion = "0.1.3-alpha";
    public const string EasyTierVersion = "2.6.4";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static string Root => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer");
    private static string InstallationStatePath => Path.Combine(Root, "installed.json");

    public static GravityConeInstallation? FindInstalled()
    {
        var rid = GetRid();
        var state = ReadInstallationState();
        var gravityConeVersion = state is { Rid: var stateRid } && stateRid == rid
            ? state.GravityConeVersion
            : GravityConeVersion;
        var easyTierVersion = state is { Rid: var easyTierRid } && easyTierRid == rid
            ? state.EasyTierVersion
            : EasyTierVersion;
        var cliName = state is { Rid: var executableRid } && executableRid == rid
            ? state.CliExecutable
            : GetCliName(rid);
        var cli = Path.Combine(Root, "GravityCone", gravityConeVersion, cliName);
        var easyTier = Path.Combine(Root, "EasyTier", easyTierVersion, rid);
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return File.Exists(cli) &&
               File.Exists(Path.Combine(easyTier, "easytier-core" + suffix)) &&
               File.Exists(Path.Combine(easyTier, "easytier-cli" + suffix))
            ? new GravityConeInstallation(cli, easyTier)
            : null;
    }

    public static async Task<GravityConeInstallation> EnsureInstalledAsync(
        IProgress<(double? Progress, string Message)>? progress, CancellationToken cancellationToken)
    {
        if (FindInstalled() is { } installed) return installed;

        progress?.Report((null, "正在获取联机组件清单"));
        await using var manifestStream = await Client.GetStreamAsync(ManifestUrl, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<GravityConeManifest>(manifestStream,
            cancellationToken: cancellationToken) ?? throw new InvalidDataException("联机组件清单为空。");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("不支持的联机组件清单版本。");

        var rid = GetRid();
        if (!manifest.GravityCone.Packages.TryGetValue(rid, out var gcPackage) ||
            !manifest.EasyTier.Packages.TryGetValue(rid, out var etPackage))
            throw new PlatformNotSupportedException($"联机组件暂不支持 {rid}。");

        var gcDirectory = Path.Combine(Root, "GravityCone", manifest.GravityCone.Version);
        var etDirectory = Path.Combine(Root, "EasyTier", manifest.EasyTier.Version, rid);
        Directory.CreateDirectory(gcDirectory);
        Directory.CreateDirectory(etDirectory);

        await InstallPackageAsync(gcPackage, gcDirectory, false, progress, cancellationToken);
        await InstallPackageAsync(etPackage, etDirectory, true, progress, cancellationToken);

        var cliName = gcPackage.Executable ?? GetCliName(rid);
        var cliPath = Path.Combine(gcDirectory, cliName);
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        if (!File.Exists(cliPath) || !File.Exists(Path.Combine(etDirectory, "easytier-core" + suffix)) ||
            !File.Exists(Path.Combine(etDirectory, "easytier-cli" + suffix)))
            throw new InvalidDataException("联机组件压缩包缺少必要文件。");

        MakeExecutable(cliPath);
        MakeExecutable(Path.Combine(etDirectory, "easytier-core" + suffix));
        MakeExecutable(Path.Combine(etDirectory, "easytier-cli" + suffix));
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(InstallationStatePath, JsonSerializer.Serialize(new InstallationState(
            manifest.GravityCone.Version, manifest.EasyTier.Version, rid, cliName)), cancellationToken);
        progress?.Report((1, "联机组件安装完成"));
        return new GravityConeInstallation(cliPath, etDirectory);
    }

    private static InstallationState? ReadInstallationState()
    {
        if (!File.Exists(InstallationStatePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<InstallationState>(File.ReadAllText(InstallationStatePath));
        }
        catch
        {
            return null;
        }
    }

    private static async Task InstallPackageAsync(OnlinePackageManifest package, string destination,
        bool flatten, IProgress<(double? Progress, string Message)>? progress, CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(ConfigPath.TempFolderPath, "Multiplayer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var archive = Path.Combine(tempRoot, package.FileName);
        var extracted = Path.Combine(tempRoot, "extracted");
        Directory.CreateDirectory(extracted);
        try
        {
            progress?.Report((0, $"正在下载 {package.FileName}"));
            var downloadUrl = GithubMirror.Apply(package.Url);
            using var response = await Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archive))
            {
                var total = response.Content.Headers.ContentLength ?? package.Size;
                var buffer = new byte[128 * 1024];
                long downloaded = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    progress?.Report((total > 0 ? (double)downloaded / total : null,
                        $"正在下载 {package.FileName}"));
                }
            }

            if (package.Size > 0 && new FileInfo(archive).Length != package.Size)
                throw new InvalidDataException($"{package.FileName} 文件大小校验失败。");
            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{package.FileName} SHA-256 校验失败。");
            }

            progress?.Report((null, $"正在解压 {package.FileName}"));
            if (package.ArchiveType.Equals("zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(archive, extracted);
            else if (package.ArchiveType.Equals("tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var input = File.OpenRead(archive);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, extracted, false);
            }
            else throw new InvalidDataException($"不支持的压缩格式：{package.ArchiveType}");

            foreach (var file in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
            {
                var relative = flatten ? Path.GetFileName(file) : Path.GetRelativePath(extracted, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static string GetRid()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("联机组件仅支持 x64 和 arm64。")
        };
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" :
            OperatingSystem.IsMacOS() ? "osx" : throw new PlatformNotSupportedException("当前系统不支持联机组件。");
        return $"{os}-{architecture}";
    }

    private static string GetCliName(string rid) => rid switch
    {
        "win-x64" => "gravitycone-cli-windows-amd64.exe",
        "win-arm64" => "gravitycone-cli-windows-arm64.exe",
        "linux-x64" => "gravitycone-cli-linux-amd64",
        "linux-arm64" => "gravitycone-cli-linux-arm64",
        "osx-x64" => "gravitycone-cli-darwin-amd64",
        "osx-arm64" => "gravitycone-cli-darwin-arm64",
        _ => throw new PlatformNotSupportedException($"联机组件暂不支持 {rid}。")
    };

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
                                  UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private sealed record InstallationState(string GravityConeVersion, string EasyTierVersion, string Rid,
        string CliExecutable);
}
