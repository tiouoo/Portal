using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using MinecraftLaunch.Components.Provider;
using Portal.Bedrock.Standard.Interface;
using Portal.Core.Minecraft.Services;
using Portal.Views.Pages.InstancePages;
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

        var loading = new QuickDownloadLoadingDialogViewModel($"下载{target.Definition.DisplayName}");
        var loadingDialog = OverlayDialog.ShowCustomAsync<QuickDownloadLoadingDialog, QuickDownloadLoadingDialogViewModel,
            object?>(loading, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = $"下载{target.Definition.DisplayName}", Buttons = DialogButton.None,
                CanLightDismiss = false, CanResize = false
            });
        try
        {
            var files = await new CurseforgeProvider().GetModFilesAsync(long.Parse(target.ProjectId));
            var file = files.Select(JavaResourceFileItem.From).OrderByDescending(item => item.Published).FirstOrDefault();
            if (file is null) throw new InvalidDataException("未找到可下载的基岩版资源文件。");

            loading.Close();
            await loadingDialog;
            await DownloadAndImportAsync(topLevel, target.Definition, file, destination);
        }
        catch
        {
            loading.Fail();
            await loadingDialog;
        }
    }

    public static async Task DownloadAsync(TopLevel topLevel, JavaResourceDefinition definition, JavaResourceFileItem file)
    {
        if (BedrockInstallationService.DefaultInstaller is null)
        {
            await JavaResourceDownload.DownloadAsync(topLevel, definition, file);
            return;
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            NotificationGateway.Notice(topLevel, "下载文件缺少基岩版包扩展名。", NotificationType.Error);
            return;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), "Portal", $"{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        var task = JavaResourceDownload.StartDownload(topLevel, definition, file, temporaryPath);
        try
        {
            await task.Completion;
            if (task.Status != Tio.Avalonia.Standard.Modules.Tasks.ManagedTaskStatus.Completed) return;
            var inspection = new BedrockPackageImportService().Inspect(temporaryPath);
            await BedrockPackageImportDialog.ImportAsync(topLevel, temporaryPath, inspection);
        }
        catch (Exception exception)
        {
            NotificationGateway.Notice(topLevel, $"无法导入基岩版包：{exception.Message}", NotificationType.Error);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
        }
    }

    private static async Task DownloadAndImportAsync(TopLevel topLevel, JavaResourceDefinition definition,
        JavaResourceFileItem file, BedrockPackageImportDialogResult destination)
    {
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            NotificationGateway.Notice(topLevel, "下载文件缺少基岩版包扩展名。", NotificationType.Error);
            return;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), "Portal", $"{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        var task = JavaResourceDownload.StartDownload(topLevel, definition, file, temporaryPath);
        try
        {
            await task.Completion;
            if (task.Status != Tio.Avalonia.Standard.Modules.Tasks.ManagedTaskStatus.Completed) return;
            var inspection = new BedrockPackageImportService().Inspect(temporaryPath);
            await Task.Run(() => new BedrockPackageImportService().Import(temporaryPath, inspection,
                destination.Instance, destination.WorldUserId));
            NotificationGateway.Notice(topLevel, $"{file.FileName} 已导入", NotificationType.Success);
        }
        catch (Exception exception)
        {
            NotificationGateway.Notice(topLevel, $"无法导入基岩版包：{exception.Message}", NotificationType.Error);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
        }
    }
}
