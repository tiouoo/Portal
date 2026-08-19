using System.Xml;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Portal.Services;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

public partial class MinecraftLogPage : UserControl, ITioTabPage
{
    private const int MaximumVisibleLogLines = 10_000;
    private readonly List<MinecraftLogEntry> _entries = [];
    private readonly IHighlightingDefinition _highlighting;
    private readonly MinecraftLogSession? _logSession;

    public MinecraftLogPage(MinecraftLogSession logSession)
    {
        _logSession = logSession;
        _highlighting = LoadHighlighting();
        InitializeComponent();
        DataContext = this;
        ConfigureEditor();
        _entries.AddRange(logSession.GetEntries());
        RefreshVisibleEntries();
        logSession.LogReceived += OnLogReceived;
        LogEditor.Options.AllowScrollBelowDocument = false;
    }

    public MinecraftLogPage()
    {
        _highlighting = LoadHighlighting();
        InitializeComponent();
        DataContext = this;
        ConfigureEditor();
    }

    public MinecraftInstance? Instance => _logSession?.Instance;

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.minecraftLog_pageTitle.CurrentValue(),
        Icon = StreamGeometry.Parse(
            "F1 M640,640z M0,0z M128,128C128,92.7,156.7,64,192,64L341.5,64C358.5,64,374.8,70.7,386.8,82.7L493.3,189.3C505.3,201.3,512,217.6,512,234.6L512,512C512,547.3,483.3,576,448,576L192,576C156.7,576,128,547.3,128,512L128,416 188.3,416 237.1,478.7C242.2,485.3 250.4,488.7 258.7,487.8 267,486.9 274.2,481.7 277.8,474.2L320.7,383 330.6,402.8C334.7,410.9,343,416.1,352.1,416.1L424.1,416.1C437.4,416.1 448.1,405.4 448.1,392.1 448.1,378.8 437.4,368.1 424.1,368.1L366.9,368.1 341.5,317.4C337.4,309.2 328.9,304 319.7,304.1 310.5,304.2 302.2,309.6 298.3,317.9L251,418.5 219,377.4C214.4,371.4,207.4,368,200,368L128,368 128,128z M336,122.5L336,216C336,229.3,346.7,240,360,240L453.5,240 336,122.5z")
    };

    public TabEntry HostTab { get; set; } = null!;

    public void OnClose()
    {
        if (_logSession != null)
            _logSession.LogReceived -= OnLogReceived;


        _entries.Clear();
        LogEditor.SyntaxHighlighting = null;
        LogEditor.Document = new TextDocument();
        DataContext = null;
    }

    public Task<bool> RequestCloseAsync()
    {
        return Task.FromResult(true);
    }

    public static void Open(MinecraftLogSession logSession, TopLevel sender)
    {
        if (InstanceDeletionCoordinator.IsDeleting(logSession.Instance))
            return;

        TioTabWindowBase window;
        if (sender is not TioTabWindowBase window1)
            window = UiProperty.ActiveWindow as TioTabWindowBase ?? UiProperty.TabWindow;
        else
            window = window1;
        if (window is null) return;

        var tab = new TabEntry(window, new MinecraftLogPage(logSession))
            { Title = string.Format(CommonLanguageManager.Instance.minecraftLog_tabTitle.CurrentValue(),
                logSession.Instance.InstanceName) };
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private void OnLogReceived(MinecraftLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _entries.Add(entry);
            if (_entries.Count > MaximumVisibleLogLines)
                _entries.RemoveAt(0);
            if (IsLogLevelEnabled(entry.Level))
            {
                LogEditor.Document.Insert(LogEditor.Document.TextLength, entry.Text + Environment.NewLine);
                TrimDocument();
            }

            ScrollToLatest();
        });
    }

    private void FilterChanged(object? sender, RoutedEventArgs e)
    {
        if (InformationFilter == null)
            return;

        RefreshVisibleEntries();
        ScrollToLatest();
    }

    private void RefreshVisibleEntries()
    {
        LogEditor.Document.Text = string.Join(Environment.NewLine,
            _entries.Where(entry => IsLogLevelEnabled(entry.Level)).Select(entry => entry.Text));
    }

    private void TrimDocument()
    {
        var document = LogEditor.Document;
        while (document.LineCount > MaximumVisibleLogLines + 1)
        {
            var firstLine = document.GetLineByNumber(1);
            document.Remove(firstLine.Offset, firstLine.TotalLength);
        }
    }

    private bool IsLogLevelEnabled(MinecraftLogLevel level)
    {
        return level switch
        {
            MinecraftLogLevel.Information => InformationFilter.IsChecked == true,
            MinecraftLogLevel.Warning => WarningFilter.IsChecked == true,
            MinecraftLogLevel.Error => ErrorFilter.IsChecked == true,
            MinecraftLogLevel.Debug => DebugFilter.IsChecked == true,
            MinecraftLogLevel.Trace => TraceFilter.IsChecked == true,
            _ => OtherFilter.IsChecked == true
        };
    }

    private void ScrollToLatest()
    {
        if (AutoScrollCheckBox?.IsChecked == true && LogEditor.Document.TextLength > 0)
            LogEditor.ScrollToEnd();
    }

    private void ConfigureEditor()
    {
        LogEditor.Document = new TextDocument();
        LogEditor.SyntaxHighlighting = _highlighting;
    }

    private static IHighlightingDefinition LoadHighlighting()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Portal/Assets/Highlighting/MinecraftLog.xshd"));
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = ExportLogAsync();
    }

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e)
    {
        LogEditor.SelectAll();
    }

    private void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        LogEditor.Copy();
    }

    private void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = ExportLogAsync();
    }

    private async Task ExportLogAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var logContent = LogEditor.Document.Text;
        if (string.IsNullOrWhiteSpace(logContent))
        {
            topLevel.Notice(CommonLanguageManager.Instance.logs_noExportableLog.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.logs_exportTitle.CurrentValue(),
            DefaultExtension = "log",
            SuggestedFileName = $"{GetSuggestedFileName()}-{DateTime.Now:yyyyMMdd-HHmmss}",
            FileTypeChoices =
                [new FilePickerFileType(CommonLanguageManager.Instance.logs_logFile.CurrentValue())
                {
                    Patterns = ["*.log"]
                }]
        });
        if (file == null)
            return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(logContent);
            topLevel.Notice(CommonLanguageManager.Instance.logs_exported.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logs_exportFailed.CurrentValue(), ex.Message),
                NotificationType.Error);
        }
    }

    private string GetSuggestedFileName()
    {
        var instanceName = _logSession?.Instance.InstanceName;
        if (string.IsNullOrWhiteSpace(instanceName))
            return CommonLanguageManager.Instance.logs_minecraftLog.CurrentValue();

        return string.Concat(instanceName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }
}