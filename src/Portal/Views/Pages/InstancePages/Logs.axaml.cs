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

public partial class Logs : UserControl
{
    private readonly IHighlightingDefinition _highlighting;
    private readonly string? _logsPath;

    public Logs()
    {
        _highlighting = LoadHighlighting();
        InitializeComponent();
        DataContext = this;
        ConfigureEditor();
        LogEditor.Options.AllowScrollBelowDocument = false;
    }

    public Logs(MinecraftInstance instance) : this()
    {
        _logsPath = instance.GetSpecialFolder(MinecraftSpecialFolder.LogsFolder);
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
        if (string.IsNullOrEmpty(_logsPath))
            return;

        var files = await Task.Run(() =>
        {
            if (!Directory.Exists(_logsPath))
                return [];

            return Directory.EnumerateFiles(_logsPath)
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
        catch (IOException ex) when (IsFileLocked(ex) &&
                                     Path.GetFileName(path).Equals("latest.log", StringComparison.OrdinalIgnoreCase))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
                topLevel.Notice(CommonLanguageManager.Instance.logs_latestLocked.CurrentValue(),
                    NotificationType.Warning);

            var nextLog =
                LogFiles.FirstOrDefault(file => !string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
            if (nextLog != null)
                LogFileSelector.SelectedItem = nextLog;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
                topLevel.Notice(string.Format(CommonLanguageManager.Instance.logs_readFailed.CurrentValue(),
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

    private static bool IsFileLocked(IOException exception)
    {
        return (exception.HResult & 0xffff) is 32 or 33;
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
            CommonLanguageManager.Instance.logs_log.CurrentValue());
    }

    private void AnalyseAi_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = LogSharingInteraction.AnalyseAiAsync(this, LogEditor.Document,
            CommonLanguageManager.Instance.logs_log.CurrentValue());
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
            topLevel.Notice(CommonLanguageManager.Instance.logs_noExportableLog.CurrentValue(),
                NotificationType.Warning);
            return;
        }

        var selectedFileName = (LogFileSelector.SelectedItem as InstanceLogFileItem)?.Name;
        var suggestedFileName = Path.GetFileNameWithoutExtension(selectedFileName) ??
                                CommonLanguageManager.Instance.logs_minecraftLog.CurrentValue();
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = CommonLanguageManager.Instance.logs_exportTitle.CurrentValue(),
            DefaultExtension = "log",
            SuggestedFileName = $"{suggestedFileName}-{DateTime.Now:yyyyMMdd-HHmmss}",
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
            await writer.WriteAsync(LogEditor.Document.Text);
            topLevel.Notice(CommonLanguageManager.Instance.logs_exported.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.logs_exportFailed.CurrentValue(), ex.Message),
                NotificationType.Error);
        }
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_logsPath))
            await TopLevel.GetTopLevel(this).Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_logsPath));
    }
}

public sealed record InstanceLogFileItem(string Name, string Path);