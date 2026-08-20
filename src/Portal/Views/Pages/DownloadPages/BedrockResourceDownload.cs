using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Iridium.Extensions.Resources;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.DownloadPages;

public static class BedrockResourceDownload
{
    public static async Task QuickDownloadAsync(TopLevel topLevel, JavaResourceDetailsTarget target)
    {
        var destination = await BedrockPackageImportDialog.SelectDestinationAsync(topLevel, target.Definition);
        if (destination is null) return;

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
            Logger.Info($"[BedrockDownload] Loading latest file for project {target.ProjectId}.");
            var files = await IridiumResourceClients.CurseForge.GetFilesAsync(long.Parse(target.ProjectId));
            var file = files.Select(file => JavaResourceFileItem.From(file.ToResourceFile()))
                .OrderByDescending(item => item.Published)
                .FirstOrDefault();
            if (file is null)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.bedrockResourceDownload_noFileFound.CurrentValue());

            loading.Close();
            await loadingDialog;
            await DownloadAndImportAsync(topLevel, target.Definition, file, destination);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[BedrockDownload] Quick download cancelled for project {target.ProjectId}: {exception}");
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

    public static async Task DownloadAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file)
    {
        if (BedrockInstallationService.DefaultInstaller is null)
        {
            await JavaResourceDownload.DownloadAsync(topLevel, definition, file);
            return;
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            topLevel.Notice(CommonLanguageManager.Instance.bedrockResourceDownload_missingExtension.CurrentValue(),
                NotificationType.Error);
            return;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), "Portal", $"{Guid.NewGuid():N}{extension}");
        Logger.Info($"[BedrockDownload] Downloading {file.FileName} to temporary path {temporaryPath} for import.");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        var task = JavaResourceDownload.StartDownload(topLevel, definition, file, temporaryPath);
        try
        {
            await task.Completion;
            if (task.Status != ManagedTaskStatus.Completed) return;
            var inspection = new BedrockPackageImportService().Inspect(temporaryPath);
            await BedrockPackageImportDialog.ImportAsync(topLevel, temporaryPath, inspection);
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.bedrockResourceDownload_importFailed.CurrentValue(),
                exception.Message), NotificationType.Error);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException exception)
            {
                Logger.Warning($"[BedrockDownload] Failed to delete temporary package {temporaryPath}: {exception}");
            }
        }
    }

    private static async Task DownloadAndImportAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file, BedrockPackageImportDialogResult destination)
    {
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            topLevel.Notice(CommonLanguageManager.Instance.bedrockResourceDownload_missingExtension.CurrentValue(),
                NotificationType.Error);
            return;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), "Portal", $"{Guid.NewGuid():N}{extension}");
        Logger.Info(
            $"[BedrockDownload] Downloading {file.FileName} to temporary path {temporaryPath} for direct import.");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        var task = JavaResourceDownload.StartDownload(topLevel, definition, file, temporaryPath);
        try
        {
            await task.Completion;
            if (task.Status != ManagedTaskStatus.Completed) return;
            var inspection = new BedrockPackageImportService().Inspect(temporaryPath);
            await Task.Run(() => new BedrockPackageImportService().Import(temporaryPath, inspection,
                destination.Instance, destination.WorldUserId));
            topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.bedrockResourceDownload_imported.CurrentValue(), file.FileName),
                NotificationType.Success);
            Logger.Info($"[BedrockDownload] Imported {file.FileName} into {destination.Instance.InstanceName}.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.bedrockResourceDownload_importFailed.CurrentValue(),
                exception.Message), NotificationType.Error);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException exception)
            {
                Logger.Warning($"[BedrockDownload] Failed to delete temporary package {temporaryPath}: {exception}");
            }
        }
    }
}