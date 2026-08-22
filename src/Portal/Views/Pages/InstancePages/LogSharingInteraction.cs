using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> AiAnalysisCache = new();

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

    public static async Task AnalyseAiAsync(Control view, TextDocument document, string displayName)
    {
        var topLevel = TopLevel.GetTopLevel(view);
        if (topLevel is null)
            return;

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logShare_noAnalyzable.CurrentValue(),
                displayName), NotificationType.Warning);
            return;
        }

        var key = ComputeContentKey(document.Text);

        if (AiAnalysisCache.TryGetValue(key, out var cached) && cached.Value.IsCompletedSuccessfully)
        {
            ShowResultNotice(topLevel, cached.Value.Result, displayName);
            return;
        }

        topLevel.Notice(CommonLanguageManager.Instance.aiAnalysis_analyzing.CurrentValue());

        try
        {
            var task = AiAnalysisCache.GetOrAdd(key, _ => new Lazy<Task<string>>(() => AnalyseCoreAsync(document.Text)))
                .Value;
            var result = await task;
            ShowResultNotice(topLevel, result, displayName);
        }
        catch (Exception ex)
        {
            AiAnalysisCache.TryRemove(key, out _);
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.aiAnalysis_failed.CurrentValue(), ex.Message),
                NotificationType.Error);
        }
    }

    private static Task<string> AnalyseCoreAsync(string content)
    {
        return LogSharingService.AnalyseAiAsync(content, null, CancellationToken.None);
    }

    private static string ComputeContentKey(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static void ShowResultNotice(TopLevel topLevel, string result, string displayName)
    {
        topLevel.Notice(new NotificationOptions
        {
            Type = NotificationType.Success,
            Expiration = TimeSpan.Zero,
            Content = CommonLanguageManager.Instance.aiAnalysis_complete.CurrentValue(),
            OperateButtons =
            [
                new OperateButtonEntry(CommonLanguageManager.Instance.aiAnalysis_viewResult.CurrentValue(),
                    _ => AiAnalysisPage.Open(result, displayName, topLevel), closeOnClick: true)
            ]
        });
    }
}
