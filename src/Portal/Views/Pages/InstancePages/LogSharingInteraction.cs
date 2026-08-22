using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using AvaloniaEdit.Document;
using Portal.Core.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Classes;
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
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logShare_noShareable.CurrentValue(),
                displayName), NotificationType.Warning);
            return;
        }

        topLevel.Notice(CommonLanguageManager.Instance.logShare_sharing.CurrentValue());

        LogShareResult[] results;
        try
        {
            results = await LogSharingService.ShareAllAsync(document.Text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logShare_failedWithMessage.CurrentValue(),
                ex.Message), NotificationType.Error);
            return;
        }

        var succeeded = results.Where(result => result.IsSuccess).ToArray();
        var failed = results.Where(result => !result.IsSuccess).ToArray();

        if (succeeded.Length == 0)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logShare_failedWithMessage.CurrentValue(),
                string.Join('；', failed.Select(f => $"{f.Platform}：{f.Error}"))), NotificationType.Error);
            return;
        }

        foreach (var f in failed)
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logShare_platformFailed.CurrentValue(),
                f.Platform, f.Error), NotificationType.Warning);

        await OverlayDialog.ShowCustomAsync<LogShareResultDialog, LogShareResultDialogViewModel, object>(
            new LogShareResultDialogViewModel(results), view.TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false
            });
    }

    public static Task AnalyseAiAsync(Control view, TextDocument document, string displayName)
    {
        var topLevel = TopLevel.GetTopLevel(view);
        if (topLevel is null)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logShare_noAnalyzable.CurrentValue(),
                displayName), NotificationType.Warning);
            return Task.CompletedTask;
        }

        AiAnalysisPage.Open(document.Text, displayName, topLevel);
        return Task.CompletedTask;
    }
}
