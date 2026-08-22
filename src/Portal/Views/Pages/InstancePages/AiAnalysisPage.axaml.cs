using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Portal.Localization;
using Portal.Module;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public partial class AiAnalysisPage : UserControl, ITioTabPage
{
    public AiAnalysisPage() : this(string.Empty, string.Empty)
    {
    }

    public AiAnalysisPage(string resultText, string displayName)
    {
        ResultText = resultText;
        PageInfo = new PageInfo
        {
            Title = string.Format(CommonLanguageManager.Instance.aiAnalysis_pageTitle.CurrentValue(), displayName),
            IconGlyph = "\ue62b", IconFont = IconResources.FontFamilyName
        };
        InitializeComponent();
        DataContext = this;
    }

    public string ResultText { get; }
    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; } = null!;

    public void OnClose()
    {
        DataContext = null;
    }

    public static void Open(string resultText, string displayName, TopLevel sender)
    {
        if (sender is not TioTabWindowBase window)
            return;
        var tab = new TabEntry(window, new AiAnalysisPage(resultText, displayName));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        await topLevel.Clipboard!.SetTextAsync(ResultText);
        topLevel.Notice(CommonLanguageManager.Instance.aiAnalysis_copied.CurrentValue(), NotificationType.Success);
    }
}
