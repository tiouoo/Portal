using System.Security.Cryptography;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using Portal.Core.Minecraft.Graphics;
using Portal.Localization;

namespace Portal.Core.Minecraft.Services;

public static class MesaLoaderService
{
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);

    private static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cc.tiouo.Portal", "Cache",
        "mesa-loader");

    public static string? GetCachedPath()
    {
        var os = OperatingSystem.IsWindows() ? OperatingSystemKind.Windows : OperatingSystemKind.Unknown;
        var artifact = MesaLoaderArtifact.ForCurrentPlatform(os);
        return artifact == null ? null : Path.Combine(CacheRoot, artifact.Sha1, "mesa-loader.jar");
    }

    public static async Task<string> EnsureMesaLoaderAsync(CancellationToken cancellationToken = default)
    {
        var os = OperatingSystem.IsWindows() ? OperatingSystemKind.Windows : OperatingSystemKind.Unknown;
        var artifact = MesaLoaderArtifact.ForCurrentPlatform(os)
                       ?? throw new InvalidOperationException(CommonLanguageManager.Instance.mesaLoader_platformUnsupported.CurrentValue());

        var jarPath = Path.Combine(CacheRoot, artifact.Sha1, "mesa-loader.jar");
        if (File.Exists(jarPath) &&
            await VerifySha1Async(jarPath, artifact.Sha1, cancellationToken).ConfigureAwait(false))
            return jarPath;

        await DownloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(jarPath) &&
                await VerifySha1Async(jarPath, artifact.Sha1, cancellationToken).ConfigureAwait(false))
                return jarPath;

            Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
            if (File.Exists(jarPath))
                File.Delete(jarPath);

            var request = new DownloadRequest(artifact.Url, jarPath, artifact.Size);
            var result = await new DefaultDownloader()
                .DownloadAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (result.Type == DownloadResultType.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            if (result.Type != DownloadResultType.Successful)
                throw result.Exception ?? new IOException(CommonLanguageManager.Instance.mesaLoader_downloadFailed.CurrentValue());

            if (!await VerifySha1Async(jarPath, artifact.Sha1, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(CommonLanguageManager.Instance.mesaLoader_verificationFailed.CurrentValue());

            return jarPath;
        }
        finally
        {
            DownloadLock.Release();
        }
    }

    private static async Task<bool> VerifySha1Async(string path, string expectedSha1,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            return actual.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}