using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Provider;
using MinecraftLaunch.Utilities;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Module.Ipc;
using Portal.Core.Services;
using Portal.Views.Pages;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module;

public static class PortalCommandExecutor
{
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static async Task ExecuteAsync(PortalCommand command)
    {
        var window = App.MainWindow;
        if (window == null) return;

        FocusMainWindow(window);
        try
        {
            Logger.Info($"执行外部命令：{command.Kind}");
            switch (command.Kind)
            {
                case PortalCommandKind.ShowMainWindow:
                    break;
                case PortalCommandKind.DownloadVanilla:
                case PortalCommandKind.DownloadLoader:
                    await StartMinecraftInstallAsync(window, command);
                    break;
                case PortalCommandKind.DownloadModpack:
                    StartModpackInstall(window, command);
                    break;
                case PortalCommandKind.Launch:
                    LaunchInstance(window, command);
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.Error($"外部命令执行失败：{exception}");
            window.Notice($"命令执行失败：{exception.Message}", NotificationType.Error);
        }
    }

    private static void FocusMainWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    private static async Task StartMinecraftInstallAsync(TopLevel window, PortalCommand command)
    {
        var folder = ResolveInstallFolder(command.Folder);
        var version = command.Version!;
        window.Notice($"正在获取 Minecraft {version} 的版本信息...");
        var vanilla = (await VanillaInstaller.EnumerableMinecraftAsync())
                      .FirstOrDefault(entry => string.Equals(entry.Id, version, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException($"未找到 Minecraft 版本“{version}”。");

        var loaders = await ResolveLoadersAsync(command.Loaders, vanilla.Id);
        var javaPath = MinecraftInstallationViewModel.GetJavaPath();
        if (MinecraftInstallationViewModel.RequiresJavaRuntime(loaders.Keys) && string.IsNullOrWhiteSpace(javaPath))
            throw new InvalidOperationException("所选加载器需要 Java 运行时，请先在设置中添加有效的 Java。");

        var versionId = string.IsNullOrWhiteSpace(command.InstanceId)
            ? CreateRecommendedVersionId(vanilla.Id, loaders)
            : command.InstanceId.Trim();
        if (versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"实例 ID“{versionId}”包含文件夹名称不允许的字符。");

        var task = MinecraftInstallationViewModel.CreateInstallationTask(vanilla, folder, versionId, loaders, javaPath);
        task.Start();
        _ = ModpackDetailsPage.ObserveInstallationAsync(task, window, $"Minecraft {versionId}");
        window.Notice($"已开始安装 Minecraft {versionId} 到“{folder.FolderName}”", NotificationType.Success);
    }

    private static async Task<Dictionary<LoaderKind, IInstallEntry>> ResolveLoadersAsync(
        List<PortalLoaderSpec> specs, string minecraftVersion)
    {
        var result = new Dictionary<LoaderKind, IInstallEntry>();
        foreach (var spec in specs)
        {
            var kind = ParseLoaderKind(spec.Kind);
            if (result.ContainsKey(kind))
                throw new InvalidOperationException($"加载器 {kind} 重复指定。");

            var candidates = (kind switch
            {
                LoaderKind.Fabric => await FabricInstaller.EnumerableFabricAsync(minecraftVersion),
                LoaderKind.Forge => await ForgeInstaller.EnumerableForgeAsync(minecraftVersion),
                LoaderKind.NeoForge => await ForgeInstaller.EnumerableForgeAsync(minecraftVersion, true),
                LoaderKind.Quilt => await QuiltInstaller.EnumerableQuiltAsync(minecraftVersion),
                LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(minecraftVersion))
                    .Cast<IInstallEntry>(),
                _ => throw new InvalidOperationException($"不支持的加载器：{kind}")
            }).ToList();

            var entry = spec.Version is null
                ? candidates.FirstOrDefault()
                : candidates.FirstOrDefault(candidate => string.Equals(
                    MinecraftInstallationViewModel.GetLoaderVersion(kind, candidate), spec.Version,
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException(spec.Version is null
                    ? $"Minecraft {minecraftVersion} 没有可用的 {kind}。"
                    : $"未找到 {kind} 版本“{spec.Version}”（Minecraft {minecraftVersion}）。");
            result[kind] = entry;
        }


        var primaries = result.Keys.Where(kind => kind != LoaderKind.OptiFine).ToList();
        if (primaries.Count > 1)
            throw new InvalidOperationException($"加载器 {string.Join("、", primaries)} 不能同时安装。");
        if (result.ContainsKey(LoaderKind.OptiFine) && primaries.Count == 1 && primaries[0] != LoaderKind.Forge)
            throw new InvalidOperationException($"OptiFine 只能单独安装或与 Forge 组合，不支持与 {primaries[0]} 组合。");

        return result;
    }

    private static LoaderKind ParseLoaderKind(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "fabric" => LoaderKind.Fabric,
            "forge" => LoaderKind.Forge,
            "neoforge" => LoaderKind.NeoForge,
            "quilt" => LoaderKind.Quilt,
            "optifine" => LoaderKind.OptiFine,
            _ => throw new InvalidOperationException(
                $"未知的加载器“{value}”，支持：fabric / forge / neoforge / quilt / optifine。")
        };
    }

    private static string CreateRecommendedVersionId(string minecraftVersion,
        Dictionary<LoaderKind, IInstallEntry> loaders)
    {
        if (loaders.Count == 0) return minecraftVersion;
        var names = loaders.Select(pair =>
            $"{pair.Key}-{MinecraftInstallationViewModel.GetLoaderVersion(pair.Key, pair.Value)}");
        return $"{minecraftVersion} {string.Join(" + ", names)}";
    }

    private static void StartModpackInstall(TopLevel window, PortalCommand command)
    {
        var source = command.Source!.Trim();
        var kind = ClassifyModpackSource(source);

        if (kind == ModpackSourceKind.LocalFile)
        {
            _ = ModpackDetailsPage.TryInstallFromPath(window, source);
            return;
        }

        var folder = ResolveInstallFolder(command.Folder);
        var displayName = kind switch
        {
            ModpackSourceKind.RemoteUrl => GetRemoteFileName(source),
            _ => source
        };
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"安装整合包：{displayName}", Description = "正在准备安装", Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消安装", Description = "取消此整合包安装", IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => InstallModpackAsync(context, command, kind, folder));
        task.Start();
        _ = ModpackDetailsPage.ObserveInstallationAsync(task, window, displayName);
        window.Notice($"已开始安装整合包到“{folder.FolderName}”", NotificationType.Success);
    }

    private static ModpackSourceKind ClassifyModpackSource(string source)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ModpackSourceKind.RemoteUrl;
        if (File.Exists(source)) return ModpackSourceKind.LocalFile;
        var looksLikePath = source.Contains('\\') || source.Contains('/') || source.Contains(':') ||
                            source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            source.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase);
        if (looksLikePath) throw new InvalidOperationException($"整合包文件不存在：{source}");
        return ModpackSourceKind.Project;
    }

    private static async Task InstallModpackAsync(TaskExecutionContext context, PortalCommand command,
        ModpackSourceKind kind, MinecraftFolderEntry folder)
    {
        var source = command.Source!.Trim();
        var instanceId = command.InstanceId;
        string? temporaryFolder = null;
        try
        {
            var archivePath = source;
            string? iconUrl = null;
            if (kind == ModpackSourceKind.RemoteUrl)
            {
                temporaryFolder = Path.Combine(Path.GetTempPath(), "Portal", "modpacks", Guid.NewGuid().ToString("N"));
                archivePath = Path.Combine(temporaryFolder, GetRemoteFileName(source));
                await RunStepAsync(context, "下载整合包", $"正在下载：{source}",
                    step => DownloadModpackAsync(step, source, archivePath));
                iconUrl = await TryGetIconUrlFromModrinthCdnAsync(source, context.CancellationToken);
            }
            else if (kind == ModpackSourceKind.Project)
            {
                var resolved = await RunStepAsync(context, "解析整合包项目", $"正在查找整合包：{source}",
                    step => ResolveProjectFileAsync(step, source, command.Provider, command.PackVersion));
                temporaryFolder = Path.Combine(Path.GetTempPath(), "Portal", "modpacks", Guid.NewGuid().ToString("N"));
                archivePath = Path.Combine(temporaryFolder, SanitizeFileName(resolved.FileName));
                iconUrl = resolved.IconUrl;
                await RunStepAsync(context, "下载整合包", $"正在下载：{resolved.DisplayName}",
                    step => DownloadModpackAsync(step, resolved.Url, archivePath, resolved.Size));
            }

            var (modpackSource, suggestedInstanceId) = await RunStepAsync(context, "解析整合包", "正在识别整合包类型",
                step => Task.Run(() => SniffModpack(archivePath), step.CancellationToken));
            var id = string.IsNullOrWhiteSpace(instanceId)
                ? string.IsNullOrWhiteSpace(suggestedInstanceId)
                    ? Path.GetFileNameWithoutExtension(archivePath)
                    : suggestedInstanceId
                : instanceId.Trim();
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException($"实例 ID“{id}”包含文件夹名称不允许的字符。");

            var instancePath = await ModpackDetailsPage.InstallLocalArchiveAsync(context, modpackSource, archivePath,
                folder.FolderPath, id);
            await ModpackDetailsPage.TrySaveProjectIconAsync(iconUrl, instancePath, context.CancellationToken);
        }
        finally
        {
            if (temporaryFolder is not null)
                Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(temporaryFolder))
                        {
                            Logger.Info($"清理外部命令整合包临时目录：{temporaryFolder}");
                            Directory.Delete(temporaryFolder, true);
                        }
                    }
                    catch (Exception exception)
                    {
                        Logger.Error($"清理外部命令整合包临时目录失败：{temporaryFolder}", exception);
                    }
                }).Forget("清理外部命令整合包临时目录");
        }
    }

    private static (ModDetailsSource Source, string? SuggestedInstanceId) SniffModpack(string archivePath)
    {
        if (ModpackSniffer.TrySniff(archivePath, out var source, out var suggestedInstanceId))
            return (source, suggestedInstanceId);

        throw new InvalidOperationException("无法识别的整合包：仅支持 Modrinth（.mrpack）与 CurseForge（.zip）整合包。");
    }

    private static async Task<ResolvedPackFile> ResolveProjectFileAsync(TaskExecutionContext context, string query,
        string? provider, string? packVersion)
    {
        var providers = provider switch
        {
            "modrinth" => new[] { "modrinth" },
            "curseforge" => ["curseforge"],
            _ => query.All(char.IsAsciiDigit) ? ["curseforge", "modrinth"] : ["modrinth", "curseforge"]
        };

        var errors = new List<string>();
        foreach (var name in providers)
        {
            context.SetDescription($"正在 {(name == "modrinth" ? "Modrinth" : "CurseForge")} 查找：{query}");
            try
            {
                var resolved = name == "modrinth"
                    ? await ResolveModrinthFileAsync(query, packVersion, context.CancellationToken)
                    : await ResolveCurseForgeFileAsync(query, packVersion, context.CancellationToken);
                context.SetDescription($"已找到：{resolved.DisplayName}");
                context.ReportProgress(1);
                return resolved;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
        }

        throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static async Task<ResolvedPackFile> ResolveModrinthFileAsync(string query, string? packVersion,
        CancellationToken cancellationToken)
    {
        var provider = new ModrinthProvider();
        ModrinthResource? project = null;
        try
        {
            var direct = await provider.SearchByProjectIdAsync(query, cancellationToken);
            if (string.IsNullOrEmpty(direct.ProjectType) ||
                string.Equals(direct.ProjectType, "modpack", StringComparison.OrdinalIgnoreCase))
                project = direct;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        project ??= (await provider.SearchAsync(query, projectType: "modpack", cancellationToken: cancellationToken))
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"Modrinth 上未找到整合包“{query}”。");

        ModrinthResourceFile? file = null;
        if (!string.IsNullOrWhiteSpace(packVersion))
        {
            try
            {
                var byVersionId = await provider.GetModFileByVersionIdAsync(packVersion, cancellationToken);
                if (byVersionId.ProjectId == project.ProjectId) file = byVersionId;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            file ??= (await provider.GetModFilesByProjectIdAsync(project.ProjectId, cancellationToken))
                     .FirstOrDefault(candidate =>
                         string.Equals(candidate.VersionNumber, packVersion, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(candidate.DisplayName, packVersion, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(candidate.VersionId, packVersion, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"整合包“{project.Name}”没有版本“{packVersion}”。");
        }
        else
        {
            file = (await provider.GetModFilesByProjectIdAsync(project.ProjectId, cancellationToken))
                   .OrderByDescending(candidate => candidate.Published).FirstOrDefault()
                   ?? throw new InvalidOperationException($"整合包“{project.Name}”没有可下载的版本。");
        }

        return new ResolvedPackFile(file.DownloadUrl, file.FileName, file.FileSize,
            $"{project.Name} {file.VersionNumber}", project.IconUrl);
    }

    private static async Task<ResolvedPackFile> ResolveCurseForgeFileAsync(string query, string? packVersion,
        CancellationToken cancellationToken)
    {
        var provider = new CurseforgeProvider();
        CurseforgeResource? project = null;
        if (long.TryParse(query, out var modId))
            project = (await provider.GetResourcesByModIdsAsync([modId], cancellationToken)).FirstOrDefault();
        project ??= (await provider.SearchResourcesPageAsync(new CurseforgeSearchOptions
                    {
                        ClassId = 4471,
                        GameId = 432,
                        SearchFilter = query,
                        SortField = SortField.Popularity,
                        SortOrder = SortOrder.Desc,
                        PageSize = 10
                    }, cancellationToken)).Items.FirstOrDefault()
                    ?? throw new InvalidOperationException($"CurseForge 上未找到整合包“{query}”。");

        var files = (await provider.GetModFilesAsync(project.Id, cancellationToken)).ToList();
        var file = string.IsNullOrWhiteSpace(packVersion)
            ? files.Where(candidate => candidate.IsAvailable && !candidate.IsServerPack)
                  .OrderByDescending(candidate => candidate.Published).FirstOrDefault()
              ?? throw new InvalidOperationException($"整合包“{project.Name}”没有可下载的文件。")
            : files.FirstOrDefault(candidate =>
                  candidate.Id.ToString() == packVersion ||
                  string.Equals(candidate.DisplayName, packVersion, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(candidate.FileName, packVersion, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"整合包“{project.Name}”没有文件“{packVersion}”。");

        var url = await ResolveCurseForgeDownloadUrlAsync(file, cancellationToken);
        return new ResolvedPackFile(url, file.FileName, file.FileLength, $"{project.Name} {file.DisplayName}",
            project.IconUrl);
    }

    private static async Task<string> ResolveCurseForgeDownloadUrlAsync(
        CurseforgeResourceFile file, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.DownloadUrl)) return file.DownloadUrl;


        var idText = file.Id.ToString();
        if (idText.Length <= 4)
            throw new InvalidOperationException($"无法获取 CurseForge 文件“{file.FileName}”的下载地址。");
        var encodedName = Uri.EscapeDataString(file.FileName);
        string[] candidates =
        [
            $"https://edge.forgecdn.net/files/{idText[..4]}/{idText[4..]}/{encodedName}",
            $"https://mediafiles.forgecdn.net/files/{idText[..4]}/{idText[4..]}/{encodedName}"
        ];
        foreach (var candidate in candidates)
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, candidate);
                using var response = await HttpUtil.Client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode) return candidate;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

        throw new InvalidOperationException($"无法获取 CurseForge 文件“{file.FileName}”的下载地址。");
    }

    private static async Task<string?> TryGetIconUrlFromModrinthCdnAsync(string url,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
                return null;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || !string.Equals(segments[0], "data", StringComparison.OrdinalIgnoreCase))
                return null;
            var project = await new ModrinthProvider().SearchByProjectIdAsync(segments[1], cancellationToken);
            return project.IconUrl;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(fileName) ? "modpack.zip" : fileName;
    }

    private static async Task DownloadModpackAsync(TaskExecutionContext context, string url, string destination,
        long size = -1)
    {
        context.SetRunning("正在下载整合包");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var request = new DownloadRequest(url, destination, size)
        {
            ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
            {
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                context.ReportProgress(progress.TotalBytes > 0
                    ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                    : null);
                context.SetDescription($"正在下载整合包：{DefaultDownloader.FormatSize(progress.Speed, true)}");
            }, DispatcherPriority.Background)
        };
        var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
        if (result.Type == DownloadResultType.Cancelled)
            throw new OperationCanceledException(context.CancellationToken);
        if (result.Type != DownloadResultType.Successful) throw result.Exception ?? new IOException("整合包下载失败。");
    }

    private static string GetRemoteFileName(string url)
    {
        var name = string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            name = Uri.UnescapeDataString(uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            name = "modpack.zip";
        if (!Path.HasExtension(name)) name += ".zip";
        return name;
    }

    private static void LaunchInstance(TopLevel window, PortalCommand command)
    {
        var id = command.InstanceId!.Trim();
        IEnumerable<MinecraftInstance> candidates = InstanceManager.Instance.Instances;
        if (!string.IsNullOrWhiteSpace(command.Folder))
        {
            var folderPath = ResolveFolderPathForLaunch(command.Folder);
            candidates = candidates.Where(instance =>
                string.Equals(NormalizePath(instance.FolderPath), folderPath, PathComparison));
        }

        var instance = candidates.FirstOrDefault(candidate => MatchesInstanceId(candidate, id))
                       ?? throw new InvalidOperationException(string.IsNullOrWhiteSpace(command.Folder)
                           ? $"未找到实例“{id}”。"
                           : $"文件夹“{command.Folder}”中未找到实例“{id}”。");

        var target = BuildLaunchTarget(instance, command);

        _ = MinecraftLaunchService.LaunchAsync(instance, window, MinecraftLaunchOptionsFactory.Create(instance,
            logSession =>
                MinecraftLogPage.Open(logSession, window)), target);
    }

    private static RecentPlayTarget? BuildLaunchTarget(MinecraftInstance instance, PortalCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.WorldFolder))
        {
            var worldFolder = command.WorldFolder.Trim();
            var savesPath = instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder);
            if (!Directory.Exists(Path.Combine(savesPath, worldFolder)))
                throw new InvalidOperationException($"实例“{instance.InstanceName}”的存档目录中未找到世界文件夹“{worldFolder}”。");
            return new RecentPlayTarget(instance, RecentPlayTargetType.World, worldFolder, worldFolder,
                $"存档·{worldFolder}", DateTime.Now);
        }

        if (!string.IsNullOrWhiteSpace(command.ServerAddress))
        {
            var address = command.ServerAddress.Trim();
            var port = command.ServerPort ?? 25565;
            return new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                $"server:{address}:{port}", address, $"服务器·{address}", DateTime.Now,
                ServerAddress: address, ServerPort: port);
        }

        return null;
    }

    private static bool MatchesInstanceId(MinecraftInstance instance, string id)
    {
        return string.Equals(instance.MinecraftEntry?.Id, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(NormalizePath(instance.InstanceFolderPath)), id,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(instance.InstanceName, id, StringComparison.OrdinalIgnoreCase);
    }

    private static MinecraftFolderEntry ResolveInstallFolder(string? specification)
    {
        var folders = Data.ConfigEntry.MinecraftFolders
            .Where(folder => folder.SupportsInstallation).ToList();
        if (string.IsNullOrWhiteSpace(specification))
        {
            var defaultFolder = Data.ConfigEntry.DefaultMinecraftFolder;
            if (defaultFolder is not null && defaultFolder.SupportsInstallation) return defaultFolder;
            return folders.FirstOrDefault()
                   ?? throw new InvalidOperationException("启动器中没有可用于安装的 Minecraft 文件夹，请先在设置中添加。");
        }

        var byName = folders.FirstOrDefault(folder =>
            string.Equals(folder.FolderName, specification, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;

        if (TryNormalizeFullPath(specification, out var fullPath))
        {
            var byPath = folders.FirstOrDefault(folder =>
                string.Equals(NormalizePath(folder.FolderPath), fullPath, PathComparison));
            if (byPath is not null) return byPath;


            if (Directory.Exists(fullPath))
            {
                var entry = new MinecraftFolderEntry
                {
                    FolderName = Path.GetFileName(fullPath) is { Length: > 0 } name ? name : fullPath,
                    FolderPath = fullPath
                };
                if (!entry.SupportsInstallation)
                    throw new InvalidOperationException($"文件夹“{specification}”不是可安装的 Portal MC 文件夹。");
                return entry;
            }
        }

        throw new InvalidOperationException($"未找到 Minecraft 文件夹“{specification}”，请先在设置中添加或检查名称/路径。");
    }

    private static string ResolveFolderPathForLaunch(string specification)
    {
        var byName = Data.ConfigEntry.MinecraftFolders.FirstOrDefault(folder =>
            string.Equals(folder.FolderName, specification, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return NormalizePath(byName.FolderPath);
        if (TryNormalizeFullPath(specification, out var fullPath)) return fullPath;
        throw new InvalidOperationException($"未找到 Minecraft 文件夹“{specification}”。");
    }

    private static bool TryNormalizeFullPath(string path, out string fullPath)
    {
        try
        {
            fullPath = NormalizePath(Path.GetFullPath(path));
            return true;
        }
        catch (Exception)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception)
        {
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 },
            operation);
        step.Start();
        await step.Completion;
        if (step.Exception is null) return;
        context.LogError($"子任务“{name}”失败。", step.Exception);
        throw new InvalidOperationException(step.Exception.Message, step.Exception);
    }

    private static async Task<T> RunStepAsync<T>(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task<T>> operation)
    {
        T? result = default;
        await RunStepAsync(context, name, description, async step => { result = await operation(step); });
        return result!;
    }

    private enum ModpackSourceKind
    {
        LocalFile,
        RemoteUrl,
        Project
    }

    private sealed record ResolvedPackFile(
        string Url,
        string FileName,
        long Size,
        string DisplayName,
        string? IconUrl = null);
}