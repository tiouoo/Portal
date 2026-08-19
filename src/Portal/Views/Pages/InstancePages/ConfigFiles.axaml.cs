using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using Tomlyn;
using Tomlyn.Model;

namespace Portal.Views.Pages.InstancePages;

public partial class ConfigFiles : UserControl, IDisposable, INotifyPropertyChanged
{
    private static readonly Geometry FolderIcon = Geometry.Parse("M3 5h5l2 2h11v12H3z");
    private static readonly Geometry FileIcon = Geometry.Parse("M6 2h8l5 5v15H6z M14 2v6h5");
    private readonly List<ConfigTreeItem> _allRootItems = [];
    private readonly IHighlightingDefinition _highlighting;
    private bool _isWordWrap;
    private string _searchText = string.Empty;

    private ConfigEditorTab? _selectedTab;

    public ConfigFiles()
    {
        ConfigPath = string.Empty;
        _highlighting = LoadHighlighting();
        InitializeComponent();
        DataContext = this;
    }

    public ConfigFiles(MinecraftInstance instance)
    {
        ConfigPath = instance.GetSpecialFolder(MinecraftSpecialFolder.ConfigFolder);
        _highlighting = LoadHighlighting();
        LoadTree();
        InitializeComponent();
        DataContext = this;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public string ConfigPath { get; }
    public ObservableCollection<ConfigTreeItem> RootItems { get; } = [];
    public ObservableCollection<ConfigEditorTab> Tabs { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplySearchFilter();
        }
    }

    public bool IsWordWrap
    {
        get => _isWordWrap;
        set
        {
            if (_isWordWrap == value) return;
            _isWordWrap = value;
            OnPropertyChanged();
        }
    }

    public ConfigEditorTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value)) return;
            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOpenTabs));
            OnPropertyChanged(nameof(CanFormatSelectedFile));
            ApplySelectedTab(value);
        }
    }

    public bool HasOpenTabs => SelectedTab != null;
    public bool CanFormatSelectedFile => SelectedTab != null && IsFormatSupported(SelectedTab.FilePath);

    public void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
        Tabs.Clear();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private static IHighlightingDefinition LoadHighlighting()
    {
        var uri = new Uri("avares://Portal/Assets/Highlighting/Config.xshd");
        using var stream = AssetLoader.Open(uri);
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void LoadTree()
    {
        _allRootItems.Clear();
        RootItems.Clear();
        var root = new DirectoryInfo(ConfigPath);
        foreach (var item in ReadDirectory(root))
            _allRootItems.Add(item);
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        RootItems.Clear();
        foreach (var item in _allRootItems)
        {
            var filteredItem = FilterItem(item, SearchText);
            if (filteredItem != null)
                RootItems.Add(filteredItem);
        }
    }

    private static ConfigTreeItem? FilterItem(ConfigTreeItem item, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return item;

        var filteredChildren = new ObservableCollection<ConfigTreeItem>();
        foreach (var child in item.Children)
        {
            var filteredChild = FilterItem(child, searchText);
            if (filteredChild != null)
                filteredChildren.Add(filteredChild);
        }

        if (!item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) && filteredChildren.Count == 0)
            return null;

        return new ConfigTreeItem(item.Name, item.FullPath, item.IsDirectory, item.Icon, filteredChildren);
    }

    private static IEnumerable<ConfigTreeItem> ReadDirectory(DirectoryInfo directory)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = directory.GetFileSystemInfos();
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var entry in entries.OrderByDescending(x => x is DirectoryInfo)
                     .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            if (entry is DirectoryInfo childDirectory)
            {
                var children = entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    ? []
                    : new ObservableCollection<ConfigTreeItem>(ReadDirectory(childDirectory));
                yield return new ConfigTreeItem(entry.Name, entry.FullName, true, FolderIcon, children);
            }
            else
            {
                yield return new ConfigTreeItem(entry.Name, entry.FullName, false, FileIcon, []);
            }
    }

    private async void FileTree_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.OfType<ConfigTreeItem>().FirstOrDefault() is not { IsDirectory: false } item)
            return;

        var existing =
            Tabs.FirstOrDefault(x => string.Equals(x.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        try
        {
            var tab = await ConfigEditorTab.LoadAsync(item.FullPath);
            existing = Tabs.FirstOrDefault(x =>
                string.Equals(x.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                tab.Dispose();
                SelectedTab = existing;
                return;
            }

            Tabs.Add(tab);
            SelectedTab = tab;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(string.Format(CommonLanguageManager.Instance.configFiles_openFailed.CurrentValue(),
                exception.Message));
        }
    }

    private void ApplySelectedTab(ConfigEditorTab? value)
    {
        if (Editor == null) return;
        Editor.Document = value?.Document ?? new TextDocument();
        Editor.SyntaxHighlighting = value == null ? null : _highlighting;
    }

    private void EditorTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
            SelectedTab = listBox.SelectedItem as ConfigEditorTab;
    }


    private void EditorTabs_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Control || e.Delta.Y == 0) return;
        ListBox.Scroll.Offset = new Vector(
            Math.Clamp(ListBox.Scroll.Offset.X - e.Delta.Y * 40, 0, ListBox.Scroll.Extent.Width),
            ListBox.Scroll.Offset.Y);
        e.Handled = true;
    }

    public void FileTree_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || sender is not ScrollViewer scrollViewer || e.Delta.Y == 0)
            return;

        var maxOffset = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        scrollViewer.Offset = new Vector(
            Math.Clamp(scrollViewer.Offset.X - e.Delta.Y * 40, 0, maxOffset),
            scrollViewer.Offset.Y);
        e.Handled = true;
    }

    private async void CloseTab_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ConfigEditorTab tab })
            await CloseTabAsync(tab);
        e.Handled = true;
    }

    private async Task<bool> CloseTabAsync(ConfigEditorTab tab)
    {
        if (tab.IsDirty)
        {
            var action = await ConfirmUnsavedAsync(string.Format(
                CommonLanguageManager.Instance.configFiles_unsavedChanges.CurrentValue(), tab.FileName));
            if (action == UnsavedFilesAction.Cancel) return false;
            if (action == UnsavedFilesAction.Save && !await SaveAsync(tab)) return false;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (SelectedTab == null) return;
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = SaveAsync(SelectedTab);
        }
        else if (CanFormatSelectedFile && e.Key == Key.F &&
                 e.KeyModifiers.HasFlag(KeyModifiers.Shift | KeyModifiers.Alt))
        {
            e.Handled = true;
            _ = FormatAsync();
        }
    }

    private void Undo_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Undo();
    }

    private void Redo_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Redo();
    }

    private void Cut_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Cut();
    }

    private void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Copy();
    }

    private void Paste_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.Paste();
    }

    private void SelectAll_OnClick(object? sender, RoutedEventArgs e)
    {
        Editor.SelectAll();
    }

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedTab != null)
            _ = SaveAsync(SelectedTab);
    }

    private void OpenPath(string path)
    {
        _ = this.GetTopLevel().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    private void Format_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = FormatAsync();
    }

    private async Task FormatAsync()
    {
        if (SelectedTab == null || !IsFormatSupported(SelectedTab.FilePath)) return;

        try
        {
            var extension = Path.GetExtension(SelectedTab.FilePath);
            var formatted = extension.Equals(".toml", StringComparison.OrdinalIgnoreCase)
                ? TomlSerializer.Serialize(TomlSerializer.Deserialize<TomlTable>(SelectedTab.Document.Text)!,
                    new TomlSerializerOptions { WriteIndented = true })
                : FormatJson(SelectedTab.Document.Text);
            SelectedTab.Document.Text = formatted.TrimEnd() + Environment.NewLine;
        }
        catch (JsonException exception)
        {
            await ShowErrorAsync(string.Format(CommonLanguageManager.Instance.configFiles_jsonError.CurrentValue(),
                exception.Message));
        }
        catch (TomlException exception)
        {
            await ShowErrorAsync(string.Format(CommonLanguageManager.Instance.configFiles_tomlError.CurrentValue(),
                exception.Message));
        }
    }

    private static bool IsFormatSupported(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() is ".json" or ".jsonc" or ".json5" or ".mcmeta"
            or ".toml";
    }

    private static string FormatJson(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var readerOptions = new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Allow
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            IndentSize = 2,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            var reader = new Utf8JsonReader(bytes, readerOptions);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        writer.WriteStartObject();
                        break;
                    case JsonTokenType.EndObject:
                        writer.WriteEndObject();
                        break;
                    case JsonTokenType.StartArray:
                        writer.WriteStartArray();
                        break;
                    case JsonTokenType.EndArray:
                        writer.WriteEndArray();
                        break;
                    case JsonTokenType.PropertyName:
                        writer.WritePropertyName(reader.GetString()!);
                        break;
                    case JsonTokenType.String:
                        writer.WriteStringValue(reader.GetString());
                        break;
                    case JsonTokenType.Number:
                        writer.WriteRawValue(Encoding.UTF8.GetString(reader.ValueSpan), skipInputValidation: true);
                        break;
                    case JsonTokenType.True:
                        writer.WriteBooleanValue(true);
                        break;
                    case JsonTokenType.False:
                        writer.WriteBooleanValue(false);
                        break;
                    case JsonTokenType.Null:
                        writer.WriteNullValue();
                        break;
                    case JsonTokenType.Comment:
                        writer.WriteCommentValue(reader.GetComment());
                        break;
                }
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<bool> SaveAsync(ConfigEditorTab tab)
    {
        try
        {
            await tab.SaveAsync();
            return true;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(string.Format(CommonLanguageManager.Instance.configFiles_saveFailed.CurrentValue(),
                exception.Message));
            return false;
        }
    }

    public async Task<bool> RequestCloseAsync()
    {
        var dirtyTabs = Tabs.Where(x => x.IsDirty).ToList();
        if (dirtyTabs.Count == 0) return true;

        var message = dirtyTabs.Count == 1
            ? string.Format(CommonLanguageManager.Instance.configFiles_unsavedChanges.CurrentValue(),
                dirtyTabs[0].FileName)
            : string.Format(CommonLanguageManager.Instance.configFiles_unsavedChangesMultiple.CurrentValue(),
                dirtyTabs.Count);
        var action = await ConfirmUnsavedAsync(message);
        if (action == UnsavedFilesAction.Cancel) return false;
        if (action == UnsavedFilesAction.Discard) return true;

        foreach (var tab in dirtyTabs)
            if (!await SaveAsync(tab))
                return false;
        return true;
    }

    private async Task<UnsavedFilesAction> ConfirmUnsavedAsync(string message)
    {
        return await OverlayDialog.ShowCustomAsync<UnsavedFilesDialog, UnsavedFilesDialogViewModel, UnsavedFilesAction>(
            new UnsavedFilesDialogViewModel(message),
            this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                CanLightDismiss = false,
                CanResize = false,
                IsCloseButtonVisible = false
            });
    }

    private async Task ShowErrorAsync(string message)
    {
        await OverlayDialog.ShowStandardAsync(
            new TextBlock { Margin = new Thickness(24), Text = message, TextWrapping = TextWrapping.Wrap },
            null,
            this.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = CommonLanguageManager.Instance.configFiles_title.CurrentValue(),
                Mode = DialogMode.Error,
                Buttons = DialogButton.OK,
                CanLightDismiss = false
            });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private async void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsMiddleButtonPressed) return;
        if (sender is DockPanel { Tag: ConfigEditorTab tab })
            await CloseTabAsync(tab);
        e.Handled = true;
    }

    private void Folder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        OpenPath(ConfigPath);
    }

    private void File_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = this.GetTopLevel().Launcher.LaunchFileInfoAsync(new FileInfo(SelectedTab.FilePath));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        LoadTree();
    }
}

public sealed record ConfigTreeItem(
    string Name,
    string FullPath,
    bool IsDirectory,
    Geometry Icon,
    ObservableCollection<ConfigTreeItem> Children);

public partial class ConfigEditorTab : ObservableObject, IDisposable
{
    private string _savedText;

    private ConfigEditorTab(string filePath, string text, Encoding encoding)
    {
        FilePath = filePath;
        Encoding = encoding;
        _savedText = text;
        Document = new TextDocument(text);
        Document.TextChanged += Document_OnTextChanged;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public TextDocument Document { get; }
    public Encoding Encoding { get; }
    public string Header => IsDirty ? $"*{FileName}" : FileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial bool IsDirty { get; private set; }

    public void Dispose()
    {
        Document.TextChanged -= Document_OnTextChanged;
    }

    public static async Task<ConfigEditorTab> LoadAsync(string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = await reader.ReadToEndAsync();
        return new ConfigEditorTab(filePath, text, reader.CurrentEncoding);
    }

    private void Document_OnTextChanged(object? sender, EventArgs e)
    {
        IsDirty = !string.Equals(Document.Text, _savedText, StringComparison.Ordinal);
    }

    public async Task SaveAsync()
    {
        await using var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, Encoding);
        await writer.WriteAsync(Document.Text);
        _savedText = Document.Text;
        IsDirty = false;
    }
}