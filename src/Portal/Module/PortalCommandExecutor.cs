using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Iridium.Enums.Resources;
using Iridium.Extensions.Resources;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Utilities;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Module.Ipc;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
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
            Logger.Info(string.Format(LogLanguageManager.Instance.ipc_commandExecuting.CurrentValue(), command.Kind));
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
            Logger.Error(string.Format(LogLanguageManager.Instance.ipc_commandExecuteFailed.CurrentValue(), exception));
            window.Notice(string.Format(CommonLanguageManager.Instance.ipc_commandExecuteFailed.CurrentValue(),
                exception.Message), NotificationType.Error);
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
        window.Notice(string.Format(CommonLanguageManager.Instance.minecraft_fetchingVersionInfo.CurrentValue(), version));
        var vanilla = (await VanillaInstaller.EnumerableMinecraftAsync())
                      .FirstOrDefault(entry => string.Equals(entry.Id, version, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException(string.Format(
                          CommonLanguageManager.Instance.minecraft_versionNotFound.CurrentValue(), version));

        var loaders = await ResolveLoadersAsync(command.Loaders, vanilla.Id);
        var javaPath = MinecraftInstallationViewModel.GetJavaPath();
        if (MinecraftInstallationViewModel.RequiresJavaRuntime(loaders.Keys) && string.IsNullOrWhiteSpace(javaPath))
            throw new InvalidOperationException(CommonLanguageManager.Instance.minecraft_loaderNeedsJava.CurrentValue());

        var versionId = string.IsNullOrWhiteSpace(command.InstanceId)
            ? CreateRecommendedVersionId(vanilla.Id, loaders)
            : command.InstanceId.Trim();
        if (versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.minecraft_invalidInstanceId.CurrentValue(), versionId));

        var task = MinecraftInstallationViewModel.CreateInstallationTask(vanilla, folder, versionId, loaders, javaPath);
        task.Start();
        _ = ModpackInstallation.ObserveInstallationAsync(task, window, $"Minecraft {versionId}");
        window.Notice(string.Format(CommonLanguageManager.Instance.minecraft_installStartedToFolder.CurrentValue(),
            versionId, folder.FolderName), NotificationType.Success);
    }

    private static async Task<Dictionary<LoaderKind, IInstallEntry>> ResolveLoadersAsync(
        List<PortalLoaderSpec> specs, string minecraftVersion)
    {
        var result = new Dictionary<LoaderKind, IInstallEntry>();
        foreach (var spec in specs)
        {
            var kind = ParseLoaderKind(spec.Kind);
            if (result.ContainsKey(kind))
                throw new InvalidOperationException(string.Format(
                    CommonLanguageManager.Instance.minecraft_loaderDuplicate.CurrentValue(), kind));

            var candidates = (kind switch
            {
                LoaderKind.Fabric => await FabricInstaller.EnumerableFabricAsync(minecraftVersion),
                LoaderKind.Forge => await ForgeInstaller.EnumerableForgeAsync(minecraftVersion),
                LoaderKind.NeoForge => await ForgeInstaller.EnumerableForgeAsync(minecraftVersion, true),
                LoaderKind.Quilt => await QuiltInstaller.EnumerableQuiltAsync(minecraftVersion),
                LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(minecraftVersion))
                    .Cast<IInstallEntry>(),
                _ => throw new InvalidOperationException(string.Format(
                    CommonLanguageManager.Instance.minecraft_unsupportedLoader.CurrentValue(), kind))
            }).ToList();

            var entry = spec.Version is null
                ? candidates.FirstOrDefault()
                : candidates.FirstOrDefault(candidate => string.Equals(
                    MinecraftInstallationViewModel.GetLoaderVersion(kind, candidate), spec.Version,
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException(spec.Version is null
                    ? string.Format(CommonLanguageManager.Instance.minecraft_loaderNotAvailableForVersion.CurrentValue(),
                        minecraftVersion, kind)
                    : string.Format(CommonLanguageManager.Instance.minecraft_loaderVersionNotFound.CurrentValue(),
                        kind, spec.Version, minecraftVersion));
            result[kind] = entry;
        }


        var primaries = result.Keys.Where(kind => kind != LoaderKind.OptiFine).ToList();
        if (primaries.Count > 1)
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.minecraft_loadersCannotInstallTogether.CurrentValue(),
                string.Join("、", primaries)));
        if (result.ContainsKey(LoaderKind.OptiFine) && primaries.Count == 1 && primaries[0] != LoaderKind.Forge)
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.minecraft_optifineCombinationInvalid.CurrentValue(), primaries[0]));

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
            _ => throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.minecraft_unknownLoader.CurrentValue(), value))
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
            _ = ModpackInstallation.TryInstallFromPath(window, source);
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
            Name = string.Format(CommonLanguageManager.Instance.modpack_installTaskName.CurrentValue(), displayName),
            Description = CommonLanguageManager.Instance.modpack_preparingInstall.CurrentValue(), Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.modpack_cancelInstall.CurrentValue(),
                    Description = CommonLanguageManager.Instance.modpack_cancelInstallDescription.CurrentValue(),
                    IconKey = "Cancel",
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
        _ = ModpackInstallation.ObserveInstallationAsync(task, window, displayName);
        window.Notice(string.Format(CommonLanguageManager.Instance.modpack_installStartedToFolder.CurrentValue(),
            folder.FolderName), NotificationType.Success);
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
        if (looksLikePath) throw new InvalidOperationException(string.Format(
            CommonLanguageManager.Instance.modpack_fileNotFound.CurrentValue(), source));
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
                await RunStepAsync(context,
                    CommonLanguageManager.Instance.modpack_downloadStep.CurrentValue(),
                    string.Format(CommonLanguageManager.Instance.modpack_downloading.CurrentValue(), source),
                    step => DownloadModpackAsync(step, source, archivePath));
                iconUrl = await TryGetIconUrlFromModrinthCdnAsync(source, context.CancellationToken);
            }
            else if (kind == ModpackSourceKind.Project)
            {
                var resolved = await RunStepAsync(context,
                    CommonLanguageManager.Instance.modpack_resolveProjectStep.CurrentValue(),
                    string.Format(CommonLanguageManager.Instance.modpack_findingModpack.CurrentValue(), source),
                    step => ResolveProjectFileAsync(step, source, command.Provider, command.PackVersion));
                temporaryFolder = Path.Combine(Path.GetTempPath(), "Portal", "modpacks", Guid.NewGuid().ToString("N"));
                archivePath = Path.Combine(temporaryFolder, SanitizeFileName(resolved.FileName));
                iconUrl = resolved.IconUrl;
                await RunStepAsync(context,
                    CommonLanguageManager.Instance.modpack_downloadStep.CurrentValue(),
                    string.Format(CommonLanguageManager.Instance.modpack_downloading.CurrentValue(),
                        resolved.DisplayName),
                    step => DownloadModpackAsync(step, resolved.Url, archivePath, resolved.Size));
            }

            var (modpackSource, suggestedInstanceId) = await RunStepAsync(context,
                CommonLanguageManager.Instance.modpack_parseModpackStep.CurrentValue(),
                CommonLanguageManager.Instance.modpack_identifyingModpackType.CurrentValue(),
                step => Task.Run(() => SniffModpack(archivePath), step.CancellationToken));
            var id = string.IsNullOrWhiteSpace(instanceId)
                ? string.IsNullOrWhiteSpace(suggestedInstanceId)
                    ? Path.GetFileNameWithoutExtension(archivePath)
                    : suggestedInstanceId
                : instanceId.Trim();
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidOperationException(string.Format(
                    CommonLanguageManager.Instance.minecraft_invalidInstanceId.CurrentValue(), id));

            var instancePath = await ModpackInstallation.InstallLocalArchiveAsync(context, modpackSource, archivePath,
                folder.FolderPath, id);
            await ModpackInstallation.TrySaveProjectIconAsync(iconUrl, instancePath, context.CancellationToken);
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
                            Logger.Info(string.Format(
                                LogLanguageManager.Instance.modpack_cleanupExternalCommandTempDir.CurrentValue(),
                                temporaryFolder));
                            Directory.Delete(temporaryFolder, true);
                        }
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(string.Format(
                            LogLanguageManager.Instance.modpack_cleanupExternalCommandTempDirFailed.CurrentValue(),
                            temporaryFolder), exception);
                    }
                }).Forget(CommonLanguageManager.Instance.modpack_cleanupTempDirForget.CurrentValue());
        }
    }

    private static (ModDetailsSource Source, string? SuggestedInstanceId) SniffModpack(string archivePath)
    {
        if (ModpackSniffer.TrySniff(archivePath, out var source, out var suggestedInstanceId))
            return (source, suggestedInstanceId);

        throw new InvalidOperationException(
            CommonLanguageManager.Instance.modpack_unrecognizedModpack.CurrentValue());
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
            context.SetDescription(string.Format(CommonLanguageManager.Instance.modpack_searchingOn.CurrentValue(),
                name == "modrinth" ? "Modrinth" : "CurseForge", query));
            try
            {
                var resolved = name == "modrinth"
                    ? await ResolveModrinthFileAsync(query, packVersion, context.CancellationToken)
                    : await ResolveCurseForgeFileAsync(query, packVersion, context.CancellationToken);
                context.SetDescription(string.Format(CommonLanguageManager.Instance.modpack_found.CurrentValue(),
                    resolved.DisplayName));
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
        Iridium.Models.Modrinth.ModrinthProject? project = null;
        try
        {
            var direct = await IridiumResourceClients.Modrinth.GetProjectAsync(query, cancellationToken);
            if (direct is not null && (string.IsNullOrEmpty(direct.ProjectType) ||
                                       string.Equals(direct.ProjectType, "modpack", StringComparison.OrdinalIgnoreCase)))
                project = direct;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        project ??= await ResolveModrinthProjectBySearchAsync(query, cancellationToken)
                    ?? throw new InvalidOperationException(string.Format(
                        CommonLanguageManager.Instance.modpack_modrinthPackNotFound.CurrentValue(), query));

        Iridium.Models.Modrinth.ModrinthVersion? file = null;
        if (!string.IsNullOrWhiteSpace(packVersion))
        {
            try
            {
                var byVersionId = await IridiumResourceClients.Modrinth.GetVersionAsync(packVersion, cancellationToken);
                if (byVersionId is { ProjectId: { } projectId } && projectId == project.Id) file = byVersionId;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            file ??= (await IridiumResourceClients.Modrinth.GetFilesAsync(project.Id!,
                        cancellationToken: cancellationToken))
                     .FirstOrDefault(candidate =>
                         string.Equals(candidate.VersionNumber, packVersion, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(candidate.Name, packVersion, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(candidate.Id, packVersion, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException(string.Format(
                         CommonLanguageManager.Instance.modpack_packVersionNotFound.CurrentValue(),
                         project.Title, packVersion));
        }
        else
        {
            file = (await IridiumResourceClients.Modrinth.GetFilesAsync(project.Id!,
                        cancellationToken: cancellationToken))
                   .OrderByDescending(candidate => candidate.DatePublished).FirstOrDefault()
                   ?? throw new InvalidOperationException(string.Format(
                       CommonLanguageManager.Instance.modpack_packNoDownloadableVersion.CurrentValue(),
                       project.Title));
        }

        var entry = file.ToResourceFile().PrimaryFile;
        return new ResolvedPackFile(entry?.Url ?? string.Empty, entry?.FileName ?? string.Empty, entry?.Size ?? 0,
            $"{project.Title} {file.VersionNumber}", project.IconUrl);
    }

    private static async Task<Iridium.Models.Modrinth.ModrinthProject?> ResolveModrinthProjectBySearchAsync(
        string query, CancellationToken cancellationToken)
    {
        var result = await IridiumResourceClients.Modrinth.SearchAsync(new Iridium.Models.Resources.ResourceSearchOptions
        {
            Source = ResourceSource.Modrinth,
            Type = ResourceType.Modpack,
            Query = query,
            PageSize = 10
        }, cancellationToken);
        var hit = result.Hits.FirstOrDefault();
        return hit?.ProjectId is { } projectId
            ? await IridiumResourceClients.Modrinth.GetProjectAsync(projectId, cancellationToken)
            : null;
    }

    private static async Task<ResolvedPackFile> ResolveCurseForgeFileAsync(string query, string? packVersion,
        CancellationToken cancellationToken)
    {
        Iridium.Models.CurseForge.CurseForgeProject? project = null;
        if (long.TryParse(query, out var modId))
            project = await IridiumResourceClients.CurseForge.GetProjectAsync(modId, cancellationToken);
        project ??= (await IridiumResourceClients.CurseForge.SearchAsync(new Iridium.Models.Resources.ResourceSearchOptions
                    {
                        Source = ResourceSource.CurseForge,
                        Type = ResourceType.Modpack,
                        Query = query,
                        PageSize = 10
                    }, cancellationToken)).Items.FirstOrDefault()
                    ?? throw new InvalidOperationException(string.Format(
                        CommonLanguageManager.Instance.modpack_curseForgePackNotFound.CurrentValue(), query));

        var files = (await IridiumResourceClients.CurseForge.GetFilesAsync(project.Id,
            cancellationToken: cancellationToken)).ToList();
        var file = string.IsNullOrWhiteSpace(packVersion)
            ? files.Where(candidate => candidate.IsAvailable != false && candidate.IsServerPack != true)
                  .OrderByDescending(candidate => candidate.FileDate).FirstOrDefault()
              ?? throw new InvalidOperationException(string.Format(
                  CommonLanguageManager.Instance.modpack_packNoDownloadableFile.CurrentValue(), project.Name))
            : files.FirstOrDefault(candidate =>
                  candidate.Id.ToString() == packVersion ||
                  string.Equals(candidate.DisplayName, packVersion, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(candidate.FileName, packVersion, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException(string.Format(
                  CommonLanguageManager.Instance.modpack_packFileNotFound.CurrentValue(), project.Name, packVersion));

        var url = await ResolveCurseForgeDownloadUrlAsync(file, cancellationToken);
        return new ResolvedPackFile(url, file.FileName ?? string.Empty, file.FileLength ?? 0,
            $"{project.Name} {file.DisplayName}", project.Logo?.ThumbnailUrl ?? project.Logo?.Url);
    }

    private static async Task<string> ResolveCurseForgeDownloadUrlAsync(
        Iridium.Models.CurseForge.CurseForgeFile file, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.DownloadUrl)) return file.DownloadUrl;


        var idText = file.Id.ToString();
        if (idText.Length <= 4)
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.modpack_curseForgeDownloadUrlFailed.CurrentValue(), file.FileName));
        var encodedName = Uri.EscapeDataString(file.FileName ?? string.Empty);
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

        throw new InvalidOperationException(string.Format(
            CommonLanguageManager.Instance.modpack_curseForgeDownloadUrlFailed.CurrentValue(), file.FileName));
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
            var project = await IridiumResourceClients.Modrinth.GetProjectAsync(segments[1], cancellationToken);
            return project?.IconUrl;
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
        context.SetRunning(CommonLanguageManager.Instance.modpack_downloadingModpack.CurrentValue());
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var request = new DownloadRequest(url, destination, size)
        {
            ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
            {
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                context.ReportProgress(progress.TotalBytes > 0
                    ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                    : null);
                context.SetDescription(string.Format(
                    CommonLanguageManager.Instance.modpack_downloadingModpackWithSpeed.CurrentValue(),
                    DefaultDownloader.FormatSize(progress.Speed, true)));
            }, DispatcherPriority.Background)
        };
        var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
        if (result.Type == DownloadResultType.Cancelled)
            throw new OperationCanceledException(context.CancellationToken);
        if (result.Type != DownloadResultType.Successful)
            throw result.Exception ?? new IOException(CommonLanguageManager.Instance.modpack_downloadFailed.CurrentValue());
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
                           ? string.Format(CommonLanguageManager.Instance.minecraft_instanceNotFound.CurrentValue(), id)
                           : string.Format(CommonLanguageManager.Instance.minecraft_instanceNotFoundInFolder.CurrentValue(),
                               command.Folder, id));

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
                throw new InvalidOperationException(string.Format(
                    CommonLanguageManager.Instance.minecraft_worldFolderNotFound.CurrentValue(),
                    instance.InstanceName, worldFolder));
            return new RecentPlayTarget(instance, RecentPlayTargetType.World, worldFolder, worldFolder,
                string.Format(CommonLanguageManager.Instance.recentPlay_worldDescription.CurrentValue(), worldFolder),
                DateTime.Now);
        }

        if (!string.IsNullOrWhiteSpace(command.ServerAddress))
        {
            var address = command.ServerAddress.Trim();
            var port = command.ServerPort ?? 25565;
            return new RecentPlayTarget(instance, RecentPlayTargetType.Server,
                $"server:{address}:{port}", address,
                string.Format(CommonLanguageManager.Instance.recentPlay_serverDescription.CurrentValue(), address),
                DateTime.Now, ServerAddress: address, ServerPort: port);
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
                   ?? throw new InvalidOperationException(
                       CommonLanguageManager.Instance.minecraft_noInstallableFolder.CurrentValue());
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
                    throw new InvalidOperationException(string.Format(
                        CommonLanguageManager.Instance.minecraft_folderNotInstallable.CurrentValue(), specification));
                return entry;
            }
        }

        throw new InvalidOperationException(string.Format(
            CommonLanguageManager.Instance.minecraft_minecraftFolderNotFound.CurrentValue(), specification));
    }

    private static string ResolveFolderPathForLaunch(string specification)
    {
        var byName = Data.ConfigEntry.MinecraftFolders.FirstOrDefault(folder =>
            string.Equals(folder.FolderName, specification, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return NormalizePath(byName.FolderPath);
        if (TryNormalizeFullPath(specification, out var fullPath)) return fullPath;
        throw new InvalidOperationException(string.Format(
            CommonLanguageManager.Instance.minecraft_minecraftFolderNotFoundShort.CurrentValue(), specification));
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
        context.LogError(string.Format(LogLanguageManager.Instance.modpack_subtaskFailed.CurrentValue(), name),
            step.Exception);
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