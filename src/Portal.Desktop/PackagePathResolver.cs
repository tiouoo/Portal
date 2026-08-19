using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Desktop;

internal static class PackagePathResolver
{
    public static bool TryGetBedrockPackagePath(string[] args, out string? packagePath)
    {
        packagePath = null;
        if (args.Length != 1)
            return false;

        var path = Uri.TryCreate(args[0], UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : args[0];
        if (!BedrockPackageImportService.TryGetArchiveType(path, out _))
            return false;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return false;

        packagePath = fullPath;
        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_packageResolver_bedrockPath.CurrentValue(), packagePath));
        return true;
    }

    public static bool TryGetJavaPackagePath(string[] args, out string? packagePath)
    {
        packagePath = null;
        if (args.Length != 1)
            return false;

        var path = Uri.TryCreate(args[0], UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : args[0];

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".mrpack", StringComparison.OrdinalIgnoreCase))
            return false;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return false;

        packagePath = fullPath;
        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_packageResolver_javaPath.CurrentValue(), packagePath));
        return true;
    }
}