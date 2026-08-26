using System.Text.Json;
using Iridium.Download;
using Iridium.Models.Download;
using Iridium.Models.Minecraft;
using Iridium.Minecraft;

namespace Portal.Core.Minecraft;

public static class MinecraftResourceCompleter
{
    public static IReadOnlyList<(string Source, string Target)> BuildCopies(
        MinecraftContext context,
        IReadOnlyList<string> sourceRoots)
    {
        var entry = context.Entry;
        var layout = context.Layout;
        var librariesRoot = layout.GetLibrariesRoot(entry);
        var assetsRoot = layout.GetAssetsRoot(entry);
        var versionJarPath = layout.GetVersionJarPath(entry);
        var assetIndexId = entry.AssetIndex?.Id ?? entry.Id;
        var assetIndexPath = Path.Combine(assetsRoot, "indexes", $"{assetIndexId}.json");

        var copies = new List<(string Source, string Target)>();

        AddLibraryCopies(entry, librariesRoot, sourceRoots, copies);

        if (!File.Exists(versionJarPath))
            AddFileCopy(copies, sourceRoots, Path.Combine("versions", entry.Id, $"{entry.Id}.jar"), versionJarPath);

        if (!File.Exists(assetIndexPath))
            AddFileCopy(copies, sourceRoots, Path.Combine("assets", "indexes", $"{assetIndexId}.json"), assetIndexPath);

        if (File.Exists(assetIndexPath))
            AddAssetCopies(assetIndexPath, assetsRoot, sourceRoots, copies);

        return copies;
    }

    public static async Task CopyAsync(
        IReadOnlyList<(string Source, string Target)> copies,
        Action<CopyProgress>? onCopy,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < copies.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (source, target) = copies[i];
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);

            onCopy?.Invoke(new CopyProgress(i + 1, copies.Count, Path.GetFileName(target)));
        }
    }

    public static async Task<DownloadResponse> DownloadAsync(
        MinecraftContext context,
        Action<ResourceDownloadProgressChangedEventArgs>? onDownload,
        CancellationToken cancellationToken)
    {
        using var downloader = new ResourceDownloader(DownloadSource.Official,
            context.Layout,
            maxConcurrency: 8);
        if (onDownload is not null)
            downloader.ProgressChanged += (_, progress) => onDownload(progress);

        return await downloader.DownloadAsync(context.Entry, cancellationToken);
    }

    private static void AddLibraryCopies(MinecraftEntry entry, string librariesRoot, IReadOnlyList<string> sourceRoots,
        List<(string, string)> copies)
    {
        foreach (var library in EnumerateLibraries(entry))
        {
            if (string.IsNullOrWhiteSpace(library.Name))
                continue;

            var relative = GetLibraryRelativePath(library.Name);
            if (relative is null)
                continue;

            var target = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target))
                continue;

            AddFileCopy(copies, sourceRoots, Path.Combine("libraries", relative.Replace('/', Path.DirectorySeparatorChar)), target);
        }
    }

    private static void AddAssetCopies(string assetIndexPath, string assetsRoot,
        IReadOnlyList<string> sourceRoots, List<(string Source, string Target)> copies)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetIndexPath));
            if (!document.RootElement.TryGetProperty("objects", out var objects))
                return;

            foreach (var asset in objects.EnumerateObject())
            {
                if (!asset.Value.TryGetProperty("hash", out var hashElement))
                    continue;
                var hash = hashElement.GetString();
                if (string.IsNullOrWhiteSpace(hash))
                    continue;

                var target = Path.Combine(assetsRoot, "objects", hash[..2], hash);
                if (File.Exists(target))
                    continue;

                AddFileCopy(copies, sourceRoots, Path.Combine("assets", "objects", hash[..2], hash), target);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void AddFileCopy(List<(string Source, string Target)> copies,
        IReadOnlyList<string> sourceRoots, string relative, string target)
    {
        foreach (var root in sourceRoots)
        {
            var source = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
                continue;

            copies.Add((source, target));
            return;
        }
    }

    private static IEnumerable<MinecraftLibrary> EnumerateLibraries(MinecraftEntry entry)
    {
        foreach (var library in entry.Libraries)
            yield return library;

        foreach (var mavenFile in entry.MavenFiles)
            yield return mavenFile;
    }

    private static string? GetLibraryRelativePath(string mavenName)
    {
        if (string.IsNullOrWhiteSpace(mavenName))
            return null;

        var extensionParts = mavenName.Split('@');
        var main = extensionParts[0];
        var extension = extensionParts.Length > 1 ? extensionParts[1] : "jar";
        var parts = main.Split(':');
        if (parts.Length < 3)
            return null;

        var groupPath = parts[0].Replace('.', '/');
        var fileName = $"{parts[1]}-{parts[2]}{(parts.Length > 3 ? $"-{parts[3]}" : string.Empty)}.{extension}";
        return $"{groupPath}/{parts[1]}/{parts[2]}/{fileName}";
    }
}

public readonly record struct CopyProgress(int CompletedCount, int TotalCount, string CurrentFile);
