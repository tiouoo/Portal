using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Portal.Services;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

/// <summary>
/// 日志页面共用的分享 / AI 分析交互流程。
/// </summary>
internal static class LogSharingInteraction
{
    /// <summary>将日志分享到 LogShare.CN 与 mclo.gs，并展示结果对话框。</summary>
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

    /// <summary>使用 LogShare.CN 的免费大模型流式分析日志。</summary>
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