using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Views.Pages.InstancePages;

public partial class CrashReports : UserControl
{
    private readonly string? _crashReportsPath;
    private readonly IHighlightingDefinition _highlighting;

    public CrashReports()
    {
        _highlighting = LoadHighlighting();
        InitializeComponent();
        DataContext = this;
        ConfigureEditor();
        LogEditor.Options.AllowScrollBelowDocument = false;
    }

    public CrashReports(MinecraftInstance instance) : this()
    {
        _crashReportsPath = instance.GetSpecialFolder(MinecraftSpecialFolder.CrashReportsFolder);
        AttachedToVisualTree += async (_, _) => await RefreshLogFilesAsync();
    }

    public ObservableCollection<InstanceLogFileItem> LogFiles { get; } = [];

    private void ConfigureEditor()
    {
        LogEditor.Document = new TextDocument();
        LogEditor.SyntaxHighlighting = _highlighting;
        LogEditor.Options.AllowScrollBelowDocument = false;
    }

    private static IHighlightingDefinition LoadHighlighting()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Portal/Assets/Highlighting/MinecraftLog.xshd"));
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private async Task RefreshLogFilesAsync()
    {
        if (string.IsNullOrEmpty(_crashReportsPath))
            return;

        var files = await Task.Run(() =>
        {
            if (!Directory.Exists(_crashReportsPath))
                return [];

            return Directory.EnumerateFiles(_crashReportsPath)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new InstanceLogFileItem(file.Name, file.FullName))
                .ToArray();
        });

        var selectedPath = (LogFileSelector.SelectedItem as InstanceLogFileItem)?.Path;
        LogFiles.Clear();
        foreach (var file in files)
            LogFiles.Add(file);
        LogFileSelector.SelectedItem =
            LogFiles.FirstOrDefault(file => file.Path == selectedPath) ?? LogFiles.FirstOrDefault();
    }

    private async void LogFileSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LogFileSelector.SelectedItem is not InstanceLogFileItem { Path: { } path })
            return;

        try
        {
            LogEditor.Document.Text = await ReadLogAsync(path);
            LogEditor.ScrollToHome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
                topLevel.Notice(string.Format(CommonLanguageManager.Instance.crashReports_readFailed.CurrentValue(),
                    ex.Message), NotificationType.Error);
        }
    }

    private static async Task<string> ReadLogAsync(string path)
    {
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            return DecodeLogText(await File.ReadAllBytesAsync(path));

        await using var fileStream = File.OpenRead(path);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await using var buffer = new MemoryStream();
        await gzipStream.CopyToAsync(buffer);
        return DecodeLogText(buffer.ToArray());
    }

    private static string DecodeLogText(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = RefreshLogFilesAsync();
    }

    private void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = ExportLogAsync();
    }

    private void Share_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = LogSharingInteraction.ShareAsync(this, LogEditor.Document,
            CommonLanguageManager.Instance.crashReports_crashReport.CurrentValue());
    }

    private void AnalyseAi_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = LogSharingInteraction.AnalyseAiAsync(this, LogEditor.Document,
            CommonLanguageManager.Instance.crashReports_crashReport.CurrentValue());
    }

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e)
    {
        LogEditor.SelectAll();
    }

    private void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        LogEditor.Copy();
    }

    private async Task ExportLogAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        if (string.IsNullOrWhiteSpace(LogEditor.Document.Text))
        {
            topLevel.Notice(CommonLanguageManager.Instance.crashReports_noExportable.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        var selectedFileName = (LogFileSelector.SelectedItem as InstanceLogFileItem)?.Name;
        var suggestedFileName = Path.GetFileNameWithoutExtension(selectedFileName) ??
                                CommonLanguageManager.Instance.crashReports_minecraftCrashReport.CurrentValue();
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.crashReports_exportTitle.CurrentValue(),
            DefaultExtension = "txt",
            SuggestedFileName = $"{suggestedFileName}-{DateTime.Now:yyyyMMdd-HHmmss}",
            FileTypeChoices =
                [new FilePickerFileType(CommonLanguageManager.Instance.crashReports_textFile.CurrentValue())
                {
                    Patterns = ["*.txt", "*.log"]
                }]
        });
        if (file == null)
            return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(LogEditor.Document.Text);
            topLevel.Notice(CommonLanguageManager.Instance.crashReports_exported.CurrentValue(),
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logs_exportFailed.CurrentValue(), ex.Message),
                NotificationType.Error);
        }
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_crashReportsPath))
            await TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_crashReportsPath));
    }
}