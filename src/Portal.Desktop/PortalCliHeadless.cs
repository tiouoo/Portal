using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Provider;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Ipc;
using Portal.Core.Services;
using Portal.Localization;

namespace Portal.Desktop;

/// <summary>
/// 不打开窗口的命令行命令：--version / -l|--list / --search。
/// 在单实例保护和 Avalonia 启动之前调用，只读本地数据，用完直接退出。
/// </summary>
internal static class PortalCliHeadless
{
    public static bool IsHeadlessCommand(string[] args)
    {
        return args.Length > 0 &&
               args[0].ToLowerInvariant() is "--version" or "-v" or "-l" or "--list" or "list" or "--search" or "search" or "help" or
                   "--help" or "-h" or "/?" or "-?";
    }

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
            return false;

        switch (args[0].ToLowerInvariant())
        {
            case "--version" or "-v":
                PortalCommandRegistration.RegisterAsync().GetAwaiter().GetResult();
                PrintVersion();
                return true;
            case "-l" or "--list" or "list":
                PortalCommandRegistration.RegisterAsync().GetAwaiter().GetResult();
                exitCode = RunList(args);
                return true;
            case "--search" or "search":
                PortalCommandRegistration.RegisterAsync().GetAwaiter().GetResult();
                exitCode = RunSearch(args);
                return true;
            case "help" or "--help" or "-h" or "/?" or "-?":
                PortalCommandRegistration.RegisterAsync().GetAwaiter().GetResult();
                Write(PortalCommandParser.GetHeadlessUsageText());
                return true;
            default:
                return false;
        }
    }

    private static void PrintVersion()
    {
        var version = AppVersionService.Instance.Version;
        var title = string.IsNullOrWhiteSpace(version.VersionTitle) ? "Portal" : version.VersionTitle;
        var type = string.IsNullOrWhiteSpace(version.Type) ? "local" : version.Type;
        Write(string.Format(CommonLanguageManager.Instance.desktop_cli_versionFormat.CurrentValue(), title, type));
    }

    private static int RunList(string[] args)
    {
        var output = new List<string>();
        var folderFilter = ParseFolderFilter(args);
        var folders = CliInstanceScanner.LoadFolders();

        CliFolderSnapshot? directFolder = null;
        if (folderFilter is not null &&
            !folders.Any(folder => IsFolderMatch(folder, folderFilter)) &&
            Directory.Exists(folderFilter))
        {
            directFolder = CreateDirectFolderSnapshot(folderFilter);
            folders = [directFolder];
        }

        if (folders.Count == 0)
        {
            var message = folderFilter is not null
                ? string.Format(CommonLanguageManager.Instance.minecraft_minecraftFolderNotFoundShort.CurrentValue(),
                    folderFilter)
                : CommonLanguageManager.Instance.desktop_cli_noFolders.CurrentValue();
            Write(string.Empty, message, string.Empty);
            return 0;
        }

        var totalInstances = 0;
        var shownFolders = 0;
        output.Add(string.Empty);
        foreach (var folder in folders)
        {
            if (directFolder is null && folderFilter is not null && !IsFolderMatch(folder, folderFilter))
                continue;

            var instances = CliInstanceScanner.Scan(folder)
                .OrderBy(instance => instance.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            totalInstances += instances.Count;
            shownFolders++;

            var layout = MinecraftFolderLayout.Detect(folder.FolderPath);
            output.Add(string.Format(CommonLanguageManager.Instance.desktop_cli_folderHeader.CurrentValue(),
                layout.DisplayName, folder.FolderName, folder.FolderPath));

            if (instances.Count == 0)
            {
                output.Add(CommonLanguageManager.Instance.desktop_cli_noInstances.CurrentValue());
            }
            else
            {
                foreach (var instance in instances)
                {
                    var loader = string.IsNullOrWhiteSpace(instance.Loader)
                        ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue()
                        : instance.Loader;
                    output.Add(string.Format(CommonLanguageManager.Instance.desktop_cli_instanceLine.CurrentValue(),
                        instance.Id, instance.Version, loader).TrimEnd());
                }
            }

            output.Add(string.Empty);
        }

        if (shownFolders == 0 && folderFilter is not null && directFolder is null)
        {
            Write(string.Empty,
                string.Format(CommonLanguageManager.Instance.minecraft_minecraftFolderNotFoundShort.CurrentValue(),
                    folderFilter),
                string.Empty);
            return 0;
        }

        if (shownFolders > 0)
        {
            output.Add(string.Format(CommonLanguageManager.Instance.desktop_cli_summary.CurrentValue(),
                totalInstances, shownFolders));
            output.Add(string.Empty);
        }

        Write(output.ToArray());
        return 0;
    }

    private static string? ParseFolderFilter(string[] args)
    {
        for (var index = 1; index < args.Length; index++)
            if (args[index].ToLowerInvariant() is "--folder" or "-f" && index + 1 < args.Length)
                return args[index + 1];
        return null;
    }

    private static bool IsFolderMatch(CliFolderSnapshot folder, string filter)
    {
        return folder.FolderName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               folder.FolderPath.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static CliFolderSnapshot CreateDirectFolderSnapshot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            name = fullPath;
        return new CliFolderSnapshot(name, fullPath, MinecraftFolderKind.Auto);
    }

    private static int RunSearch(string[] args)
    {
        if (args.Length < 2)
        {
            Write(CommonLanguageManager.Instance.desktop_cli_searchNeedsQuery.CurrentValue());
            return 1;
        }

        var query = args[1];
        var projectType = "mod";
        var limit = 10;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--type":
                    if (index + 1 >= args.Length)
                    {
                        Write(CommonLanguageManager.Instance.desktop_cli_searchNeedsQuery.CurrentValue());
                        return 1;
                    }
                    projectType = args[++index].ToLowerInvariant();
                    break;
                case "--limit":
                    if (index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedLimit))
                    {
                        limit = Math.Clamp(parsedLimit, 1, 50);
                        index++;
                    }
                    break;
                default:
                    Write(string.Format(CommonLanguageManager.Instance.ipc_unknownArgument.CurrentValue(), args[index]));
                    return 1;
            }
        }

        if (!IsSupportedProjectType(projectType))
        {
            Write(string.Format(CommonLanguageManager.Instance.desktop_cli_searchTypeUnknown.CurrentValue(), projectType));
            return 1;
        }

        try
        {
            var provider = new ModrinthProvider();
            var page = provider.SearchPageAsync(query, projectType: projectType, limit: limit)
                .GetAwaiter().GetResult();

            var output = new List<string>
            {
                string.Empty,
                string.Format(CommonLanguageManager.Instance.desktop_cli_searchTitle.CurrentValue(), query, projectType,
                    page.Items.Count),
                string.Empty
            };

            var index = 1;
            foreach (var item in page.Items)
            {
                if (index > 1)
                    output.Add(string.Empty);

                output.Add(string.Format(CommonLanguageManager.Instance.desktop_cli_searchItem.CurrentValue(), index,
                    item.Name, item.Slug, item.DownloadCount));
                output.Add(string.Format(CommonLanguageManager.Instance.desktop_cli_searchLink.CurrentValue(),
                    item.WebLink));
                index++;
            }

            if (page.Items.Count == 0)
                output.Add(CommonLanguageManager.Instance.desktop_cli_searchEmpty.CurrentValue());

            output.Add(string.Empty);
            Write(output.ToArray());
            return 0;
        }
        catch (Exception exception)
        {
            Write(string.Format(CommonLanguageManager.Instance.desktop_cli_searchFailed.CurrentValue(),
                exception.Message));
            return 1;
        }
    }

    private static void Write(params string[] lines)
    {
        PortalCommandService.WriteConsoleLines(lines);
    }

    private static bool IsSupportedProjectType(string projectType)
    {
        return projectType is "mod" or "modpack" or "resourcepack" or "shader" or "datapack";
    }
}
