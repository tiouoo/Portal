using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Portal.Core.Minecraft.Services;
using Portal.Views.Pages.InstancePages;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;

namespace Portal.Views.Pages.DownloadPages;

public static class BedrockResourceDownload
{
    public static async Task DownloadAsync(TopLevel topLevel, JavaResourceDefinition definition, JavaResourceFileItem file)
    {
        if (!OperatingSystem.IsWindows())
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
}
