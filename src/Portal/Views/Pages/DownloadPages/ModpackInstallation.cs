using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Iridium.Models.Resources;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Installer.Modpack;
using MinecraftLaunch.Utilities;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

internal static class ModpackInstallation
{
    public static async Task HandleVersionFileClickAsync(JavaResourceDetailsViewModel viewModel, TopLevel topLevel,
        JavaResourceFileItem file)
    {
        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(viewModel.Name),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.modpack_downloadStep.CurrentValue(),
                Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result is null) return;
        if (result.Destination == ModpackDownloadDestination.SaveAs)
        {
            await SaveAsAsync(topLevel, file);
            return;
        }

        if (result.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;
        StartInstallation(topLevel, viewModel.Target.Source, file, viewModel.IconUrl, result);
    }

    /// <summary>从搜索结果一键安装整合包：解析最新版本 → 选择目标 → 安装。</summary>
    public static async Task InstallFromSearchAsync(TopLevel topLevel, JavaResourceDetailsTarget target,
        string? iconUrl, string? suggestedName)
    {
        var loading = new QuickDownloadLoadingDialogViewModel(
            string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                target.Definition.DisplayName));
        var loadingDialog = OverlayDialog
            .ShowCustomAsync<QuickDownloadLoadingDialog, QuickDownloadLoadingDialogViewModel,
                object?>(loading, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = string.Format(CommonLanguageManager.Instance.quickDownload_title.CurrentValue(),
                    target.Definition.DisplayName), Buttons = DialogButton.None,
                CanLightDismiss = false, CanResize = false
            });
        try
        {
            IReadOnlyList<JavaResourceFileItem> files = target.Source switch
            {
                ModDetailsSource.Modrinth =>
                    (await IridiumResourceClients.Modrinth.GetProjectFilesAsync(target.ProjectId))
                    .Select(JavaResourceFileItem.From).ToArray(),
                ModDetailsSource.CurseForge => (await IridiumResourceClients.CurseForge.GetProjectFilesAsync(
                        target.ProjectId))
                    .Select(JavaResourceFileItem.From).ToArray(),
                _ => []
            };
            var file = files.OrderByDescending(item => item.Published).FirstOrDefault();
            if (file is null)
                throw new InvalidDataException(CommonLanguageManager.Instance.quickDownload_noFiles.CurrentValue());
            loading.Close();
            await loadingDialog;

            await InstallFromFileAsync(topLevel, target.Source, file, iconUrl,
                SanitizeInstanceId(string.IsNullOrWhiteSpace(suggestedName)
                    ? file.DisplayName
                    : suggestedName));
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Modpack] One-click install cancelled for {target.ProjectId}: {exception}");
            loading.Fail();
            await loadingDialog;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            loading.Fail();
            await loadingDialog;
        }
    }

    /// <summary>展示整合包安装对话框（选择目标文件夹与实例 ID），随后直接安装最新版本。</summary>
    public static async Task InstallFromFileAsync(TopLevel topLevel, ModDetailsSource source,
        JavaResourceFileItem file, string? iconUrl, string? suggestedInstanceId)
    {
        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(suggestedInstanceId ?? file.DisplayName),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.modpack_downloadStep.CurrentValue(),
                Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result is null) return;
        if (result.Destination == ModpackDownloadDestination.SaveAs)
        {
            await SaveAsAsync(topLevel, file);
            return;
        }

        if (result.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;
        StartInstallation(topLevel, source, file, iconUrl, result);
    }

    private static string SanitizeInstanceId(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name.Trim();
    }

    public static async Task InstallLocalAsync(TopLevel topLevel, string archivePath, ModDetailsSource source,
        string suggestedInstanceId)
    {
        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(
                string.IsNullOrWhiteSpace(suggestedInstanceId)
                    ? Path.GetFileNameWithoutExtension(archivePath)
                    : suggestedInstanceId,
                false),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.modpack_installTitle.CurrentValue(),
                Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result?.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;

        var displayName = Path.GetFileName(archivePath);
        Logger.Info(
            $"[Modpack] Queuing local {source} modpack installation from {archivePath} to {result.Folder.FolderPath} as {result.InstanceId}.");
        var task = TaskManager.Instance.CreateTask(new TaskOptions
            {
                Name = string.Format(CommonLanguageManager.Instance.modpack_installTaskName.CurrentValue(),
                    displayName), Description = CommonLanguageManager.Instance.modpack_preparingInstall.CurrentValue(),
                Progress = 0,
                Actions =
                [
                    CreateCancelInstallAction()
                ]
            },
            context => InstallLocalArchiveAsync(context, source, archivePath, result.Folder!.FolderPath,
                result.InstanceId!));
        task.Start();
        _ = ObserveInstallationAsync(task, topLevel, displayName);
    }

    public static async Task TryInstallFromPath(TopLevel topLevel, string path)
    {
        if (!TryGetModpack(path, out var archivePath, out var source, out var suggestedInstanceId))
        {
            Logger.Warning($"[Modpack] Rejected invalid local modpack archive {path}.");
            topLevel.Notice(CommonLanguageManager.Instance.modpack_invalidFile.CurrentValue(), NotificationType.Error);
            return;
        }

        var result = await OverlayDialog.ShowCustomAsync<ModpackInstallDialog, ModpackInstallDialogViewModel,
            ModpackInstallDialogResult>(new ModpackInstallDialogViewModel(
                string.IsNullOrWhiteSpace(suggestedInstanceId)
                    ? Path.GetFileNameWithoutExtension(archivePath)
                    : suggestedInstanceId,
                false),
            topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.modpack_installTitle.CurrentValue(),
                Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result?.Folder is null || string.IsNullOrWhiteSpace(result.InstanceId)) return;

        var displayName = Path.GetFileName(archivePath);
        Logger.Info(
            $"[Modpack] Queuing imported {source} modpack {archivePath} to {result.Folder.FolderPath} as {result.InstanceId}.");
        var task = TaskManager.Instance.CreateTask(new TaskOptions
            {
                Name = string.Format(CommonLanguageManager.Instance.modpack_installTaskName.CurrentValue(),
                    displayName), Description = CommonLanguageManager.Instance.modpack_preparingInstall.CurrentValue(),
                Progress = 0,
                Actions =
                [
                    CreateCancelInstallAction()
                ]
            },
            context => InstallLocalArchiveAsync(context, source, archivePath, result.Folder!.FolderPath,
                result.InstanceId!));
        task.Start();
        _ = ObserveInstallationAsync(task, topLevel, displayName);
    }

    public static bool TryGetModpack(string path, out string archivePath, out ModDetailsSource source,
        out string suggestedInstanceId)
    {
        archivePath = string.Empty;
        source = default;
        suggestedInstanceId = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (!ModpackSniffer.TrySniff(path, out source, out var sniffedInstanceId)) return false;

        archivePath = path;
        suggestedInstanceId = sniffedInstanceId ?? string.Empty;
        return true;
    }

    private static TaskActionDefinition CreateCancelInstallAction()
    {
        return new TaskActionDefinition
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
        };
    }

    private static async Task SaveAsAsync(TopLevel topLevel, JavaResourceFileItem file)
    {
        var selected = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.modpack_saveAsTitle.CurrentValue(),
            SuggestedFileName = file.FileName,
            FileTypeChoices = [new FilePickerFileType(CommonLanguageManager.Instance.modpack_fileType.CurrentValue())
            {
                Patterns = ["*.mrpack", "*.zip"]
            }]
        });
        var destination = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;
        Logger.Info($"[Modpack] Exporting {file.FileName} to {destination}.");
        JavaResourceDownload.StartDownload(topLevel, JavaResourceDefinitions.Modpack, file, destination);
    }

    private static void StartInstallation(TopLevel topLevel, ModDetailsSource source, JavaResourceFileItem file,
        string? iconUrl, ModpackInstallDialogResult selection)
    {
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = string.Format(CommonLanguageManager.Instance.modpack_installTaskName.CurrentValue(),
                file.DisplayName), Description = CommonLanguageManager.Instance.modpack_preparingInstall.CurrentValue(),
            Progress = 0,
            Actions =
            [
                CreateCancelInstallAction()
            ]
        }, context => InstallAsync(context, source, file, iconUrl, selection));
        task.Start();
        _ = ObserveInstallationAsync(task, topLevel, file.DisplayName);
    }

    private static async Task InstallAsync(TaskExecutionContext context, ModDetailsSource source,
        JavaResourceFileItem file,
        string? iconUrl, ModpackInstallDialogResult selection)
    {
        var folder = selection.Folder!.FolderPath;
        var instanceId = selection.InstanceId!;
        var isPortalMc = MinecraftFolderLayout.TryFindPortalMcRoot(folder, out var portalMcRoot);
        var installFolder = isPortalMc ? Path.Combine(portalMcRoot, "meta") : folder;
        var instancesRoot = isPortalMc ? Path.Combine(portalMcRoot, "instances") : null;
        var instancePath = Path.Combine(instancesRoot ?? Path.Combine(folder, "versions"), instanceId);
        if (Directory.Exists(instancePath))
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.modpack_instanceIdExists.CurrentValue(), instanceId));
        var temporaryFolder = Path.Combine(Path.GetTempPath(), "Portal", "modpacks", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(temporaryFolder, Path.GetFileName(file.FileName));
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Modpack] Installing remote {source} modpack {file.FileName} to {instancePath}.");
        try
        {
            await Task.Run(() => Directory.CreateDirectory(temporaryFolder));
            await RunStepAsync(context, CommonLanguageManager.Instance.modpack_downloadArchiveStep.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.modpack_downloading.CurrentValue(), file.FileName),
                step => DownloadArchiveAsync(step, file, archivePath));
            var minecraft = source switch
            {
                ModDetailsSource.Modrinth => await InstallModrinthAsync(context, installFolder, instanceId, archivePath,
                    GetForgeJavaPath(), instancesRoot),
                ModDetailsSource.CurseForge => await InstallCurseForgeAsync(context, installFolder, instanceId,
                    archivePath,
                    GetForgeJavaPath(), instancesRoot),
                _ => throw new NotSupportedException(CommonLanguageManager.Instance.modpack_unsupportedSource.CurrentValue())
            };
            await TrySaveProjectIconAsync(iconUrl, instancePath, context.CancellationToken);
            await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_refreshInstancesStep.CurrentValue(),
                CommonLanguageManager.Instance.minecraft_scanningNewInstances.CurrentValue(), step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription(string.Format(
                    CommonLanguageManager.Instance.minecraft_instancesRefreshed.CurrentValue(), instanceId));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription(string.Format(
                CommonLanguageManager.Instance.modpack_installComplete.CurrentValue(), instanceId));
            Logger.Info(
                $"[Modpack] Installed remote modpack {file.FileName} as {minecraft.Id} in {stopwatch.Elapsed}.");
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug(
                $"[Modpack] Remote installation of {file.FileName} was cancelled after {stopwatch.Elapsed}: {exception}");
            await DeleteDirectoryAsync(instancePath);
            await DeletePortalMcTemporaryLoaderAsync(instancesRoot, installFolder, instanceId);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteDirectoryAsync(instancePath);
            await DeletePortalMcTemporaryLoaderAsync(instancesRoot, installFolder, instanceId);
            throw;
        }
        finally
        {
            await DeleteDirectoryAsync(temporaryFolder);
        }
    }

    private static Task DeletePortalMcTemporaryLoaderAsync(string? instancesRoot, string installFolder,
        string instanceId)
    {
        if (instancesRoot is null) return Task.CompletedTask;
        return DeleteDirectoryAsync(Path.Combine(installFolder, "versions", $"{instanceId}.portal-tmp"));
    }

    internal static async Task<string> InstallLocalArchiveAsync(TaskExecutionContext context, ModDetailsSource source,
        string archivePath,
        string folder, string instanceId)
    {
        var isPortalMc = MinecraftFolderLayout.TryFindPortalMcRoot(folder, out var portalMcRoot);
        var installFolder = isPortalMc ? Path.Combine(portalMcRoot, "meta") : folder;
        var instancesRoot = isPortalMc ? Path.Combine(portalMcRoot, "instances") : null;
        var instancePath = Path.Combine(instancesRoot ?? Path.Combine(folder, "versions"), instanceId);
        if (Directory.Exists(instancePath))
            throw new InvalidOperationException(string.Format(
                CommonLanguageManager.Instance.modpack_instanceIdExists.CurrentValue(), instanceId));
        var stopwatch = Stopwatch.StartNew();
        Logger.Info($"[Modpack] Installing local {source} modpack {archivePath} to {instancePath}.");

        try
        {
            var minecraft = source switch
            {
                ModDetailsSource.Modrinth => await InstallModrinthAsync(context, installFolder, instanceId, archivePath,
                    GetForgeJavaPath(), instancesRoot),
                ModDetailsSource.CurseForge => await InstallCurseForgeAsync(context, installFolder, instanceId,
                    archivePath, GetForgeJavaPath(), instancesRoot),
                _ => throw new NotSupportedException(CommonLanguageManager.Instance.modpack_unsupportedSource.CurrentValue())
            };
            await RunStepAsync(context, CommonLanguageManager.Instance.modpack_importSettingsStep.CurrentValue(),
                CommonLanguageManager.Instance.modpack_importSettingsDescription.CurrentValue(), step =>
            {
                ImportPortalSettings(instancePath, instancesRoot is not null);
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_refreshInstancesStep.CurrentValue(),
                CommonLanguageManager.Instance.minecraft_scanningNewInstances.CurrentValue(), step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription(string.Format(
                    CommonLanguageManager.Instance.minecraft_instancesRefreshed.CurrentValue(), instanceId));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription(string.Format(
                CommonLanguageManager.Instance.modpack_installComplete.CurrentValue(), instanceId));
            Logger.Info($"[Modpack] Installed local modpack {archivePath} as {minecraft.Id} in {stopwatch.Elapsed}.");
            return instancePath;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug(
                $"[Modpack] Local installation of {archivePath} was cancelled after {stopwatch.Elapsed}: {exception}");
            await DeleteDirectoryAsync(instancePath);
            await DeletePortalMcTemporaryLoaderAsync(instancesRoot, installFolder, instanceId);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteDirectoryAsync(instancePath);
            await DeletePortalMcTemporaryLoaderAsync(instancesRoot, installFolder, instanceId);
            throw;
        }
    }

    private static Task DeleteDirectoryAsync(string directory)
    {
        return Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception exception)
            {
                Logger.Warning($"[Modpack] Failed to clean up {directory}: {exception}");
            }
        });
    }

    private static async Task DownloadArchiveAsync(TaskExecutionContext context, JavaResourceFileItem file,
        string destination)
    {
        context.SetRunning(string.Format(CommonLanguageManager.Instance.modpack_downloading.CurrentValue(),
            file.FileName));
        var request = new DownloadRequest(file.DownloadUrl, destination, file.FileSize)
        {
            ProgressChanged = DownloadProgressReporter.Create(context,
                speed => string.Format(CommonLanguageManager.Instance.modpack_downloadingArchiveSpeed.CurrentValue(),
                    DefaultDownloader.FormatSize(speed, true)))
        };
        var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
        if (result.Type == DownloadResultType.Cancelled)
            throw new OperationCanceledException(context.CancellationToken);
        if (result.Type != DownloadResultType.Successful)
            throw result.Exception ?? new IOException(CommonLanguageManager.Instance.modpack_downloadFailed.CurrentValue());
    }

    internal static async Task TrySaveProjectIconAsync(string? iconUrl, string instancePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return;

        try
        {
            Logger.Info($"[Modpack] Downloading optional project icon from {iconUrl} to {instancePath}.");
            using var response =
                await HttpUtil.Client.GetAsync(iconUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(instancePath);
            var iconPath = Path.Combine(instancePath, "icon.png");
            var temporaryPath = iconPath + ".tmp";
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = File.Create(temporaryPath))
            {
                await source.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporaryPath, iconPath, true);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Modpack] Optional project icon download was cancelled: {exception}");
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Modpack] Failed to save optional project icon to {instancePath}: {exception}");
        }
    }

    private static async Task<MinecraftEntry> InstallModrinthAsync(TaskExecutionContext context, string folder,
        string id,
        string archivePath, string? javaPath, string? instancesRoot)
    {
        var entry = await RunStepAsync(context, CommonLanguageManager.Instance.modpack_parseModpackStep.CurrentValue(),
            CommonLanguageManager.Instance.modpack_readingModrinthManifest.CurrentValue(), step =>
        {
            return Task.Run(() =>
            {
                var parsed = ModrinthModpackInstaller.ParseModpackInstallEntry(archivePath);
                step.ReportProgress(1);
                return parsed;
            }, step.CancellationToken);
        });
        var loaderTask = RunStepAsync(context, CommonLanguageManager.Instance.modpack_prepareLoaderStep.CurrentValue(),
            CommonLanguageManager.Instance.modpack_fetchingLoader.CurrentValue(), step =>
            ModrinthModpackInstaller.ParseModLoaderEntryAsync(entry, step.CancellationToken));
        var vanillaTask = GetVanillaEntryAsync(context, entry.McVersion);
        await Task.WhenAll(loaderTask, vanillaTask);
        var loader = await loaderTask;
        var vanilla = await vanillaTask;
        var hasLoader = loader is not null;
        var sourceRoots = MinecraftResourceRoots.ResolveForInstall(Data.ConfigEntry.MinecraftFolders, folder);
        if (loader is not null)
            javaPath = await EnsureJavaRuntimeAsync(loader, javaPath, entry.McVersion, context,
                context.CancellationToken);
        var effectiveLoaderId = instancesRoot is not null && id.Equals(vanilla.Id, StringComparison.OrdinalIgnoreCase)
            ? $"{id}.portal-tmp"
            : id;
        if (instancesRoot is not null && !effectiveLoaderId.Equals(id, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() =>
            {
                var stale = Path.Combine(folder, "versions", effectiveLoaderId);
                if (Directory.Exists(stale)) Directory.Delete(stale, true);
            });
        var vanillaInstaller = VanillaInstaller.Create(folder, vanilla, hasLoader ? null : id);
        vanillaInstaller.SourceRootDirectories = sourceRoots;
        var vanillaInstallation = RunInstallerStepAsync(context,
            CommonLanguageManager.Instance.minecraft_installVanillaStep.CurrentValue(),
            string.Format(CommonLanguageManager.Instance.minecraft_installingMinecraft.CurrentValue(),
                entry.McVersion),
            vanillaInstaller);
        var modpackWorkingPath = hasLoader
            ? Path.Combine(folder, "versions", effectiveLoaderId)
            : instancesRoot is not null
                ? Path.Combine(instancesRoot, id)
                : Path.Combine(folder, "versions", id);
        var filesInstallation = RunModpackFilesStepAsync(context,
            CommonLanguageManager.Instance.modpack_installFilesStep.CurrentValue(),
            CommonLanguageManager.Instance.modpack_downloadingMods.CurrentValue(),
            new ModrinthModpackInstaller
            {
                MinecraftFolder = folder, ModpackPath = archivePath, Entry = entry, Minecraft = null!,
                WorkingPath = modpackWorkingPath
            });
        var minecraft = await vanillaInstallation;
        if (loader is not null)
            minecraft = await RunInstallerStepAsync(context,
                string.Format(CommonLanguageManager.Instance.minecraft_installLoaderStep.CurrentValue(),
                    GetLoaderName(loader)),
                CommonLanguageManager.Instance.modpack_installingLoader.CurrentValue(),
                CreateModLoaderInstaller(loader, folder, effectiveLoaderId, javaPath, minecraft, sourceRoots));
        await filesInstallation;
        if (instancesRoot is not null)
        {
            if (hasLoader)
                await MovePortalMcInstanceAsync(context, folder, effectiveLoaderId, id, instancesRoot);
            else
                WritePortalMcVanillaInstanceAsync(context, instancesRoot, id, id);
        }

        return minecraft;
    }

    private static void WritePortalMcVanillaInstanceAsync(TaskExecutionContext context, string instancesRoot,
        string instanceId, string vanillaId)
    {
        Directory.CreateDirectory(instancesRoot);
        var instanceDirectory = Path.Combine(instancesRoot, instanceId);
        Directory.CreateDirectory(instanceDirectory);
        var jsonPath = Path.Combine(instanceDirectory, $"{instanceId}.json");
        using var stream = File.Create(jsonPath);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("id", instanceId);
        writer.WriteString("inheritsFrom", vanillaId);
        writer.WriteString("mainClass", "net.minecraft.client.main.Main");
        writer.WritePropertyName("libraries");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static async Task<MinecraftEntry> InstallCurseForgeAsync(TaskExecutionContext context, string folder,
        string id,
        string archivePath, string? javaPath, string? instancesRoot)
    {
        var entry = await RunStepAsync(context, CommonLanguageManager.Instance.modpack_parseModpackStep.CurrentValue(),
            CommonLanguageManager.Instance.modpack_readingCurseForgeManifest.CurrentValue(), step =>
        {
            return Task.Run(() =>
            {
                var parsed = CurseforgeModpackInstaller.ParseModpackInstallEntry(archivePath);
                step.ReportProgress(1);
                return parsed;
            }, step.CancellationToken);
        });
        var loadersTask = RunStepAsync(context, CommonLanguageManager.Instance.modpack_prepareLoaderStep.CurrentValue(),
            CommonLanguageManager.Instance.modpack_fetchingLoader.CurrentValue(), async step =>
        {
            var result = new List<IInstallEntry>();
            await foreach (var loader in CurseforgeModpackInstaller.ParseModLoaderEntryByManifestAsync(entry,
                               step.CancellationToken))
                result.Add(loader);
            step.ReportProgress(1);
            return result;
        });
        var vanillaTask = GetVanillaEntryAsync(context, entry.McVersion);
        await Task.WhenAll(loadersTask, vanillaTask);
        var loaders = await loadersTask;
        var vanilla = await vanillaTask;
        var sourceRoots = MinecraftResourceRoots.ResolveForInstall(Data.ConfigEntry.MinecraftFolders, folder);
        foreach (var loader in loaders)
            javaPath = await EnsureJavaRuntimeAsync(loader, javaPath, entry.McVersion, context,
                context.CancellationToken);

        var effectiveLoaderId = instancesRoot is not null && id.Equals(vanilla.Id, StringComparison.OrdinalIgnoreCase)
            ? $"{id}.portal-tmp"
            : id;
        if (instancesRoot is not null && !effectiveLoaderId.Equals(id, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() =>
            {
                var stale = Path.Combine(folder, "versions", effectiveLoaderId);
                if (Directory.Exists(stale)) Directory.Delete(stale, true);
            });
        var vanillaInstaller = VanillaInstaller.Create(folder, vanilla, loaders.Count > 0 ? null : id);
        vanillaInstaller.SourceRootDirectories = sourceRoots;
        var vanillaInstallation = RunInstallerStepAsync(context,
            CommonLanguageManager.Instance.minecraft_installVanillaStep.CurrentValue(),
            string.Format(CommonLanguageManager.Instance.minecraft_installingMinecraft.CurrentValue(),
                entry.McVersion),
            vanillaInstaller);
        var modpackWorkingPath = loaders.Count > 0
            ? Path.Combine(folder, "versions", effectiveLoaderId)
            : instancesRoot is not null
                ? Path.Combine(instancesRoot, id)
                : Path.Combine(folder, "versions", id);
        var filesInstallation = RunModpackFilesStepAsync(context,
            CommonLanguageManager.Instance.modpack_installFilesStep.CurrentValue(),
            CommonLanguageManager.Instance.modpack_parsingMods.CurrentValue(),
            new CurseforgeModpackInstaller
            {
                MinecraftFolder = folder, ModpackPath = archivePath, Entry = entry, Minecraft = null!,
                WorkingPath = modpackWorkingPath
            });
        var minecraft = await vanillaInstallation;
        var hasLoader = loaders.Count > 0;
        foreach (var loader in loaders)
            minecraft = await RunInstallerStepAsync(context,
                string.Format(CommonLanguageManager.Instance.minecraft_installLoaderStep.CurrentValue(),
                    GetLoaderName(loader)),
                CommonLanguageManager.Instance.modpack_installingLoader.CurrentValue(),
                CreateModLoaderInstaller(loader, folder, effectiveLoaderId, javaPath, minecraft, sourceRoots));
        await filesInstallation;
        if (instancesRoot is not null)
        {
            if (hasLoader)
                await MovePortalMcInstanceAsync(context, folder, effectiveLoaderId, id, instancesRoot);
            else
                WritePortalMcVanillaInstanceAsync(context, instancesRoot, id, id);
        }

        return minecraft;
    }

    private static void ImportPortalSettings(string instancePath, bool isPortalMc)
    {
        var portalFolder = Path.Combine(instancePath, "Portal");
        if (!Directory.Exists(portalFolder))
            return;

        try
        {
            var exportedConfigPath = Path.Combine(portalFolder, MinecraftInstance.PortablePortalConfigFileName);
            if (File.Exists(exportedConfigPath))
            {
                var targetConfigPath = isPortalMc
                    ? MinecraftInstance.GetExternalConfigPath(MinecraftFolderKind.PortalMc, instancePath)
                    : Path.Combine(instancePath, MinecraftInstance.PortablePortalConfigFileName);
                var configDirectory = Path.GetDirectoryName(targetConfigPath);
                if (!string.IsNullOrEmpty(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                    File.Copy(exportedConfigPath, targetConfigPath, true);
                    Logger.Info($"[Modpack] Imported Portal settings to {targetConfigPath}.");
                }
            }

            var exportedIconPath = Path.Combine(portalFolder, MinecraftInstance.PortablePortalIconFileName);
            if (File.Exists(exportedIconPath))
            {
                var targetIconPath = isPortalMc
                    ? MinecraftInstance.GetExternalConfigPath(MinecraftFolderKind.PortalMc, instancePath) + ".png"
                    : Path.Combine(instancePath, MinecraftInstance.PortablePortalIconFileName);
                var iconDirectory = Path.GetDirectoryName(targetIconPath);
                if (!string.IsNullOrEmpty(iconDirectory))
                {
                    Directory.CreateDirectory(iconDirectory);
                    File.Copy(exportedIconPath, targetIconPath, true);
                    Logger.Info($"[Modpack] Imported Portal icon to {targetIconPath}.");
                }
            }

            Directory.Delete(portalFolder, true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Modpack] Failed to import Portal settings from {portalFolder}: {exception}");
        }
    }

    private static Task MovePortalMcInstanceAsync(TaskExecutionContext context, string metadataRoot,
        string effectiveLoaderId, string instanceId, string instancesRoot)
    {
        return RunStepAsync(context, CommonLanguageManager.Instance.minecraft_createInstanceStep.CurrentValue(),
            CommonLanguageManager.Instance.minecraft_generatingInstanceConfig.CurrentValue(), step =>
        {
            var loaderVersionDirectory = Path.Combine(metadataRoot, "versions", effectiveLoaderId);
            Directory.CreateDirectory(instancesRoot);
            if (Directory.Exists(loaderVersionDirectory))
            {
                Directory.Move(loaderVersionDirectory, Path.Combine(instancesRoot, instanceId));
                if (!effectiveLoaderId.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    var jsonFile = Path.Combine(instancesRoot, instanceId, $"{effectiveLoaderId}.json");
                    if (File.Exists(jsonFile))
                        File.Move(jsonFile, Path.Combine(instancesRoot, instanceId, $"{instanceId}.json"));
                    var jarFile = Path.Combine(instancesRoot, instanceId, $"{effectiveLoaderId}.jar");
                    if (File.Exists(jarFile))
                        File.Move(jarFile, Path.Combine(instancesRoot, instanceId, $"{instanceId}.jar"));
                }
            }

            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }

    private static async Task<string?> EnsureJavaRuntimeAsync(IInstallEntry loader, string? javaPath,
        string minecraftVersion, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        if (loader is not ForgeInstallEntry || (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath)))
            return javaPath;
        var runtime = await JavaAutoInstallCoordinator.EnsureAsync(
            MinecraftInstallationViewModel.GetRecommendedJavaVersion(minecraftVersion),
            progress => MinecraftInstallationTasks.ReportJavaInstallProgress(context, progress), cancellationToken);
        return runtime?.JavaPath ?? throw new InvalidOperationException(
            CommonLanguageManager.Instance.modpack_forgeJavaRequired.CurrentValue());
    }

    private static string? GetForgeJavaPath()
    {
        return MinecraftInstallationTasks.GetJavaPath();
    }

    private static async Task<VersionManifestEntry> GetVanillaEntryAsync(TaskExecutionContext context,
        string minecraftVersion)
    {
        return await RunStepAsync(context, CommonLanguageManager.Instance.modpack_prepareVanillaStep.CurrentValue(),
            string.Format(CommonLanguageManager.Instance.modpack_findingMinecraft.CurrentValue(), minecraftVersion),
            async step =>
        {
            var version = (await VanillaInstaller.EnumerableMinecraftAsync(step.CancellationToken))
                .FirstOrDefault(candidate => candidate.Id == minecraftVersion);
            if (version is null)
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.modpack_minecraftVersionNotFound.CurrentValue());
            step.ReportProgress(1);
            return version;
        });
    }

    private static InstallerBase CreateModLoaderInstaller(IInstallEntry entry, string folder, string id,
        string? javaPath,
        MinecraftEntry inheritedMinecraft, IEnumerable<string> sourceRoots)
    {
        InstallerBase installer = entry switch
        {
            ForgeInstallEntry forge => new ForgeInstaller
            {
                MinecraftFolder = folder, JavaPath = javaPath!, Entry = forge, CustomId = id,
                InheritedMinecraft = inheritedMinecraft
            },
            FabricInstallEntry fabric => new FabricInstaller
            {
                MinecraftFolder = folder, Entry = fabric, CustomId = id, InheritedMinecraft = inheritedMinecraft
            },
            QuiltInstallEntry quilt => new QuiltInstaller
            {
                MinecraftFolder = folder, Entry = quilt, CustomId = id, InheritedMinecraft = inheritedMinecraft
            },
            _ => throw new NotSupportedException(string.Format(
                CommonLanguageManager.Instance.modpack_unsupportedLoader.CurrentValue(), entry.GetType().Name))
        };
        installer.SourceRootDirectories = sourceRoots;
        return installer;
    }

    private static string GetLoaderName(IInstallEntry entry)
    {
        return entry switch
        {
            ForgeInstallEntry forge => forge.IsNeoforge ? "NeoForge" : "Forge",
            FabricInstallEntry => "Fabric",
            QuiltInstallEntry => "Quilt",
            _ => CommonLanguageManager.Instance.modpack_genericLoaderName.CurrentValue()
        };
    }

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name,
        string description,
        InstallerBase installer)
    {
        return await RunStepAsync(context, name, description, async step =>
        {
            Exception? installationFailure = null;
            installer.ProgressChanged += CreateInstallerProgressReporter(step);
            installer.Completed += (_, completed) =>
            {
                if (!completed.IsSuccessful)
                    installationFailure ??= completed.Exception ??
                                            new InvalidOperationException(
                                                CommonLanguageManager.Instance.modpack_installerNoFailureReason
                                                    .CurrentValue());
            };
            try
            {
                var minecraft = await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
                if (installationFailure is not null)
                    throw new InvalidOperationException(
                        string.Format(CommonLanguageManager.Instance.modpack_stepFailed.CurrentValue(), name),
                        installationFailure);
                return minecraft;
            }
            catch when (installationFailure is not null)
            {
                throw new InvalidOperationException(
                    string.Format(CommonLanguageManager.Instance.modpack_stepFailed.CurrentValue(), name),
                    installationFailure);
            }
        });
    }

    private static Task RunModpackFilesStepAsync(TaskExecutionContext context, string name, string description,
        InstallerBase installer)
    {
        return RunStepAsync(context, name, description, async step =>
        {
            installer.ProgressChanged += CreateInstallerProgressReporter(step);

            switch (installer)
            {
                case ModrinthModpackInstaller modrinth:
                    await RunInBackgroundAsync(modrinth.InstallFilesAsync, step.CancellationToken);
                    break;
                case CurseforgeModpackInstaller curseforge:
                    await RunInBackgroundAsync(curseforge.InstallFilesAsync, step.CancellationToken);
                    break;
                default:
                    throw new NotSupportedException(string.Format(
                        CommonLanguageManager.Instance.modpack_unsupportedInstaller.CurrentValue(),
                        installer.GetType().Name));
            }
        });
    }

    private static Task RunInBackgroundAsync(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    private static Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    private static EventHandler<InstallProgressChangedEventArgs> CreateInstallerProgressReporter(
        TaskExecutionContext context)
    {
        InstallProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return (_, progress) =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);

                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                    ReportInstallerProgress(context, current);
            }, DispatcherPriority.Background);
        };
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

    private static void ReportInstallerProgress(TaskExecutionContext context, InstallProgressChangedEventArgs progress)
    {
        context.ReportProgress(progress.Progress);
        var count = progress.TotalStepTaskCount > 0
            ? $" {progress.FinishedStepTaskCount}/{progress.TotalStepTaskCount}"
            : string.Empty;
        var speed = progress.IsStepSupportSpeed && progress.Speed >= 0
            ? $"，{DefaultDownloader.FormatSize(progress.Speed, true)}"
            : string.Empty;
        context.SetDescription($"{GetInstallStepDescription(progress.StepName)}{count}{speed}");
    }

    private static string GetInstallStepDescription(InstallStep step, InstallStep primaryStep = InstallStep.Undefined)
    {
        return step switch
        {
            InstallStep.DownloadVersionJson => CommonLanguageManager.Instance.minecraft_stepDownloadingVersionJson
                .CurrentValue(),
            InstallStep.ParseMinecraft => CommonLanguageManager.Instance.modpack_stepParsingMinecraft.CurrentValue(),
            InstallStep.DownloadAssetIndexFile => CommonLanguageManager.Instance.minecraft_stepDownloadingAssetIndex
                .CurrentValue(),
            InstallStep.DownloadLibraries => CommonLanguageManager.Instance.modpack_stepDownloadingLibraries.CurrentValue(),
            InstallStep.CopyLibraries => CommonLanguageManager.Instance.modpack_stepCopyingLibraries.CurrentValue(),
            InstallStep.DownloadPackage => CommonLanguageManager.Instance.minecraft_stepDownloadingPackage.CurrentValue(),
            InstallStep.ParsePackage => CommonLanguageManager.Instance.minecraft_stepParsingPackage.CurrentValue(),
            InstallStep.WriteVersionJsonAndSomeDependencies => CommonLanguageManager.Instance
                .modpack_stepWritingLoaderConfig.CurrentValue(),
            InstallStep.RunInstallProcessor => CommonLanguageManager.Instance.minecraft_stepRunningInstallProcessor
                .CurrentValue(),
            InstallStep.ParseDownloadUrls => CommonLanguageManager.Instance.modpack_stepParsingModUrls.CurrentValue(),
            InstallStep.RedirectInvalidMod => CommonLanguageManager.Instance.modpack_stepProcessingModUrls.CurrentValue(),
            InstallStep.DownloadMods => CommonLanguageManager.Instance.modpack_stepDownloadingMods.CurrentValue(),
            InstallStep.ExtractModpack => CommonLanguageManager.Instance.modpack_stepExtractingModpack.CurrentValue(),
            _ when primaryStep != InstallStep.Undefined => GetInstallStepDescription(primaryStep),
            _ => CommonLanguageManager.Instance.modpack_stepInstalling.CurrentValue()
        };
    }

    internal static async Task ObserveInstallationAsync(ManagedTask task, TopLevel topLevel, string name)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await task.Completion;
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[Modpack] Installation {name} was cancelled after {stopwatch.Elapsed}: {exception}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        if (task.Status == ManagedTaskStatus.Completed)
        {
            Logger.Info($"[Modpack] Installation {name} completed in {stopwatch.Elapsed}.");
            Dispatcher.UIThread.Post(() => topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.modpack_installCompleteNotice.CurrentValue(), name),
                NotificationType.Success));
        }
        else if (task.Status == ManagedTaskStatus.Faulted)
        {
            Dispatcher.UIThread.Post(() =>
                topLevel.Notice(string.Format(CommonLanguageManager.Instance.modpack_installFailedNotice.CurrentValue(),
                    name, GetRootCauseMessage(task.Exception) ?? task.ErrorMessage ??
                    CommonLanguageManager.Instance.modpack_checkTaskLog.CurrentValue()), NotificationType.Error));
        }
    }

    private static string? GetRootCauseMessage(Exception? exception)
    {
        while (exception?.InnerException is not null) exception = exception.InnerException;
        return exception?.Message;
    }
}
