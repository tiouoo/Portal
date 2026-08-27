using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iridium.Minecraft;
using Iridium.Minecraft.Formats;
using Iridium.Extension.Minecraft.Formats;
using Iridium.Models.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Instance.Java;

internal static class ExternalMinecraftScanner
{
    public static async Task<IReadOnlyList<MinecraftInstance>> ScanAsync(MinecraftFolderEntry folder,
        CancellationToken cancellationToken = default)
    {
        var layout = folder.DetectedLayout;
        return layout.Kind switch
        {
            MinecraftFolderKind.Modrinth or MinecraftFolderKind.ModrinthInstance
                => await ScanModrinthAsync(folder, layout, cancellationToken),
            MinecraftFolderKind.MultiMc or MinecraftFolderKind.MultiMcInstance
                => await ScanMultiMcAsync(folder, layout, cancellationToken),
            MinecraftFolderKind.CurseForge or MinecraftFolderKind.CurseForgeInstance => await ScanCurseForgeAsync(folder, layout, cancellationToken),
            MinecraftFolderKind.PortalMc => await ScanPortalMcAsync(folder, layout, cancellationToken),
            _ => []
        };
    }

    private static async Task<IReadOnlyList<MinecraftInstance>> ScanPortalMcAsync(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout, CancellationToken cancellationToken)
    {
        var result = new List<MinecraftInstance>();
        result.AddRange(await ScanPortalMcJavaAsync(folder, folderLayout, cancellationToken));
        result.AddRange(ScanPortalMcBedrock(folder, folderLayout));
        return result;
    }

    private static async Task<IReadOnlyList<MinecraftInstance>> ScanPortalMcJavaAsync(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout, CancellationToken cancellationToken)
    {
        var root = folderLayout.RootPath;
        var metadataRoot = Path.Combine(root, "meta");
        var result = new List<MinecraftInstance>();

        foreach (var context in await new MinecraftProvider(new DirectoryInfo(root),
                     [new PortalMcProvider()]).GetMinecraftsAsync(cancellationToken))
        {
            try
            {
                var entry = context.Entry;
                var iconPath = ResolveIcon(entry.InstancePath, "icon.png") ?? ResolveIcon(entry.InstancePath, "Icon.png")
                    ?? ResolveIcon(entry.InstancePath, "Portal.Icon.png");
                result.Add(CreateInstance(context, folder, MinecraftFolderKind.PortalMc, root, metadataRoot,
                    iconPath, entry.Name));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or
                                                  ArgumentException or InvalidOperationException)
            {
            }
        }

        return result;
    }

    private static IReadOnlyList<MinecraftInstance> ScanPortalMcBedrock(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout)
    {
        var instancesRoot = Path.Combine(folderLayout.RootPath, "bedrock_instances");
        if (!Directory.Exists(instancesRoot))
            return [];

        var result = new List<MinecraftInstance>();
        foreach (var instanceFolder in Directory.GetDirectories(instancesRoot))
            try
            {
                var config = BedrockHelper.GetInstanceConfig(instanceFolder);
                result.Add(new MinecraftInstance(config, folder.FolderName, folderLayout.RootPath));
            }
            catch (Exception exception)
            {
                Logger.Error(string.Format(LogLanguageManager.Instance.instanceManager_bedrockScanFailed.CurrentValue(), instanceFolder), exception);
            }

        return result;
    }

    private static async Task<IReadOnlyList<MinecraftInstance>> ScanMultiMcAsync(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout, CancellationToken cancellationToken)
    {
        var root = folderLayout.RootPath;
        var metadataRoot = Path.Combine(root, "meta");
        var result = new List<MinecraftInstance>();

        foreach (var context in await new MinecraftProvider(new DirectoryInfo(root),
                     [new PrismMinecraftProvider()]).GetMinecraftsAsync(cancellationToken))
        {
            try
            {
                var entry = context.Entry;
                var isBakaXl = File.Exists(Path.Combine(entry.InstancePath, "package.info"));
                string? iconPath;
                if (isBakaXl)
                    iconPath = ResolveIcon(entry.InstancePath, "icon.png") ?? ResolveIcon(entry.InstancePath, "Icon.png");
                else
                {
                    var cfg = ReadCfg(Path.Combine(entry.InstancePath, "instance.cfg"));
                    iconPath = ResolveMultiMcIcon(root, entry.InstancePath, cfg.GetValueOrDefault("iconKey"));
                }

                result.Add(CreateInstance(context, folder, MinecraftFolderKind.MultiMc, root, metadataRoot,
                    iconPath, entry.Name));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<MinecraftInstance>> ScanCurseForgeAsync(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout, CancellationToken cancellationToken)
    {
        var root = folderLayout.RootPath;
        var metadataRoot = Path.Combine(root, "Install");
        var result = new List<MinecraftInstance>();

        foreach (var context in await new MinecraftProvider(new DirectoryInfo(root),
                     [new CurseForgeProvider()]).GetMinecraftsAsync(cancellationToken))
        {
            try
            {
                var entry = context.Entry;
                var icon = ResolveIcon(entry.InstancePath, "icon.png") ?? ResolveIcon(entry.InstancePath, "Icon.png");
                result.Add(CreateInstance(context, folder, MinecraftFolderKind.CurseForge, root, metadataRoot,
                    icon, entry.Name));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<MinecraftInstance>> ScanModrinthAsync(MinecraftFolderEntry folder,
        MinecraftFolderLayout folderLayout, CancellationToken cancellationToken)
    {
        var root = folderLayout.RootPath;
        var metadataRoot = Path.Combine(root, "meta");
        var result = new List<MinecraftInstance>();

        var instances = await new MinecraftProvider(new DirectoryInfo(root),
            [new ModrinthProvider()]).GetMinecraftsAsync(cancellationToken);
        if (folderLayout.Kind is MinecraftFolderKind.ModrinthInstance)
        {
            var selected = Path.GetFullPath(folderLayout.SelectedPath);
            instances = instances.Where(context =>
                Path.GetFullPath(context.Entry.InstancePath).Equals(selected, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        foreach (var context in instances)
        {
            try
            {
                var entry = context.Entry;
                var icon = ResolveIcon(entry.InstancePath, "icon.png") ?? ResolveIcon(entry.InstancePath, "Icon.png");
                result.Add(CreateInstance(context, folder, MinecraftFolderKind.Modrinth, root, metadataRoot,
                    icon, entry.Name));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or
                                                  ArgumentException or InvalidOperationException)
            {
            }
        }

        return result;
    }

    private static MinecraftInstance CreateInstance(MinecraftContext context, MinecraftFolderEntry folder,
        MinecraftFolderKind kind, string root, string metadataRoot, string? iconPath, string displayName)
    {
        var entry = context.Entry;
        var iridiumLayout = context.Layout;
        var layout = new MinecraftInstanceLayout(
            kind, root, entry.InstancePath, iridiumLayout.GetGameDirectory(entry), metadataRoot,
            Path.Combine(metadataRoot, "assets"), Path.Combine(metadataRoot, "libraries"),
            iridiumLayout.GetNativesDirectory(entry), iconPath);

        return new MinecraftInstance(context, layout)
        {
            FolderName = folder.FolderName,
            FolderPath = folder.FolderPath,
            ExternalDisplayName = displayName
        };
    }

    private static Dictionary<string, string> ReadCfg(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;

        foreach (var parts in File.ReadLines(path).Select(line => line.Split('=', 2))
                     .Where(parts => parts.Length == 2))
            values[parts[0]] = parts[1];
        return values;
    }

    private static string? ResolveMultiMcIcon(string root, string instanceRoot, string? iconKey)
    {
        var candidates = new List<string>
        {
            Path.Combine(instanceRoot, ".minecraft", "icon.png"),
            Path.Combine(instanceRoot, ".minecraft", "Icon.png"),
            Path.Combine(instanceRoot, "icon.png")
        };
        if (!string.IsNullOrWhiteSpace(iconKey) && iconKey != "default")
        {
            candidates.Add(Path.Combine(root, "icons", $"{iconKey}.png"));
            candidates.Add(Path.Combine(root, "icons", iconKey));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ResolveIcon(string root, string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        var path = Path.IsPathRooted(iconPath) ? iconPath : Path.Combine(root, iconPath);
        return File.Exists(path) ? path : null;
    }
}
