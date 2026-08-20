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
               args[0].ToLowerInvariant() is "--version" or "-v" or "-l" or "--list" or "list" or "--search" or "search"
                   or "--launch" or "help" or "--help" or "-h" or "/?" or "-?";
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
            case "--launch":
                PortalCommandRegistration.RegisterAsync().GetAwaiter().GetResult();
                exitCode = RunLaunch(args, out var continueToGui);
                return !continueToGui;
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
        var folderFilter = ParseOptionValue(args, "--folder", "-f");
        if (folderFilter is not null)
            folderFilter = ResolvePossiblePath(folderFilter);
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
                var idWidth = instances.Max(instance => GetDisplayWidth(instance.Id));
                var versionWidth = instances.Max(instance => GetDisplayWidth(instance.Version));
                foreach (var instance in instances)
                {
                    var loader = string.IsNullOrWhiteSpace(instance.Loader)
                        ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue()
                        : instance.Loader;
                    output.Add(string.Format(CommonLanguageManager.Instance.desktop_cli_instanceLine.CurrentValue(),
                        PadDisplay(instance.Id, idWidth),
                        PadDisplay(instance.Version, versionWidth),
                        loader));
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

    private static string? ParseOptionValue(string[] args, params string[] names)
    {
        for (var index = 1; index < args.Length; index++)
            if (names.Contains(args[index], StringComparer.OrdinalIgnoreCase) && index + 1 < args.Length)
                return args[index + 1];
        return null;
    }

    private static string ResolvePossiblePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        if (value.Contains('/') || value.Contains('\\') || value.StartsWith('.'))
            try
            {
                return Path.GetFullPath(value);
            }
            catch (Exception)
            {
            }

        return value;
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

        var query = ResolvePossiblePath(args[1]);
        var projectType = "mod";
        var limit = 10;
        var offset = 0;

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
                case "--offset":
                    if (index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedOffset))
                    {
                        offset = Math.Max(0, parsedOffset);
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
            var page = provider.SearchPageAsync(query, projectType: projectType, offset: offset, limit: limit)
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

    private static int RunLaunch(string[] args, out bool continueToGui)
    {
        continueToGui = false;
        switch (PortalCommandParser.Parse(args, out var command, out var error))
        {
            case PortalCliParseStatus.Error:
                Write(string.Format(
                    CommonLanguageManager.Instance.desktop_commandService_argumentError.CurrentValue(), error,
                    Environment.NewLine, Environment.NewLine, PortalCommandParser.GetHeadlessUsageText()));
                return 1;
            case PortalCliParseStatus.Command when command is not null:
                if (!string.IsNullOrWhiteSpace(command.Folder))
                    command.Folder = ResolvePossiblePath(command.Folder.Trim());
                return CliHeadlessLauncher.Run(command);
            default:
                return 1;
        }
    }

    private static void Write(params string[] lines)
    {
        PortalCommandService.WriteConsoleLines(lines);
    }

    private static string PadDisplay(string text, int width)
    {
        var padding = width - GetDisplayWidth(text);
        return padding > 0 ? text + new string(' ', padding) : text;
    }

    private static int GetDisplayWidth(string text)
    {
        var width = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                width += char.ConvertToUtf32(character, text[index + 1]) >= 0x20000 ? 2 : 1;
                index++;
                continue;
            }

            width += IsWideCharacter(character) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWideCharacter(char character)
    {
        return character is >= '\u1100' and <= '\u115f' or
            '\u2329' or '\u232a' or
            >= '\u2e80' and <= '\ua4cf' or
            >= '\uac00' and <= '\ud7a3' or
            >= '\uf900' and <= '\ufaff' or
            >= '\ufe10' and <= '\ufe19' or
            >= '\ufe30' and <= '\ufe6f' or
            >= '\uff00' and <= '\uff60' or
            >= '\uffe0' and <= '\uffe6';
    }

    private static bool IsSupportedProjectType(string projectType)
    {
        return projectType is "mod" or "modpack" or "resourcepack" or "shader" or "datapack";
    }
}
