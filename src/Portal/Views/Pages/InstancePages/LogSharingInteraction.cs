using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Portal.Core.Services;
using Portal.Services;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

internal static class LogSharingInteraction
{
        public static async Task ShareAsync(Control view, TextDocument document, string displayName)
    {
        var topLevel = TopLevel.GetTopLevel(view);
        if (topLevel is null)
            return;

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            NotificationGateway.Notice(topLevel, $"没有可分享的{displayName}", NotificationType.Warning);
            return;
        }

        NotificationGateway.Notice(topLevel, "分享中…", NotificationType.Information);

        LogShareResult[] results;
        try
        {
            results = await LogSharingService.ShareAllAsync(document.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            NotificationGateway.Notice(topLevel, $"分享失败：{ex.Message}", NotificationType.Error);
            return;
        }

        var succeeded = results.Where(result => result.IsSuccess).ToArray();
        var failed = results.Where(result => !result.IsSuccess).ToArray();

        if (succeeded.Length == 0)
        {
            NotificationGateway.Notice(topLevel,
                $"分享失败：{string.Join('；', failed.Select(f => $"{f.Platform}：{f.Error}"))}",
                NotificationType.Error);
            return;
        }

        foreach (var f in failed)
            NotificationGateway.Notice(topLevel, $"{f.Platform} 分享失败：{f.Error}", NotificationType.Warning);

        await OverlayDialog.ShowCustomAsync<LogShareResultDialog, LogShareResultDialogViewModel, object>(
            new LogShareResultDialogViewModel(results), view.TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false
            });
    }

        public static async Task AnalyseAiAsync(Control view, TextDocument document, string displayName)
    {
        var topLevel = TopLevel.GetTopLevel(view);
        if (topLevel is null)
            return;

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            NotificationGateway.Notice(topLevel, $"没有可分析的{displayName}", NotificationType.Warning);
            return;
        }

        var viewModel = new AiAnalysisDialogViewModel();
        var dialog = OverlayDialog.ShowCustomAsync<AiAnalysisDialog, AiAnalysisDialogViewModel, object>(
            viewModel, view.TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false
            });

        try
        {
            await LogSharingService.AnalyseAiAsync(document.Text,
                chunk => Dispatcher.UIThread.Post(() => viewModel.Append(chunk)), CancellationToken.None);
            Dispatcher.UIThread.Post(viewModel.Complete);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => viewModel.Fail(ex.Message));
        }

        await dialog;
    }
}