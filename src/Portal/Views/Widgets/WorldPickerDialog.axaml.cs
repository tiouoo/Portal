using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using TioUi.Common.Interfaces;

namespace Portal.Views.Widgets;

public partial class WorldPickerDialog : UserControl
{
    public WorldPickerDialog()
    {
        InitializeComponent();
    }

    private void WorldItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (sender is Control { Tag: WorldPickItem item } && DataContext is WorldPickerDialogViewModel vm)
            vm.Confirm(item);
    }
}

public partial class WorldPickerDialogViewModel : ObservableObject, IDialogContext
{
    private readonly MinecraftInstance _instance;
    private readonly WorldSaveService _saveService = new();
    private List<WorldPickItem> _allItems = [];
    [ObservableProperty] private bool _isEmpty;

    [ObservableProperty] private string _searchText = string.Empty;

    public WorldPickerDialogViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        _ = LoadAsync();
    }

    public ObservableCollection<WorldPickItem> FilteredItems { get; } = [];

    public string InstanceHint => $"实例：{_instance.InstanceName}";

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    private async Task LoadAsync()
    {
        var saves = await _saveService.ScanAsync(_instance);
        _allItems = saves.Select(s => new WorldPickItem(s)).ToList();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim();
        var list = string.IsNullOrEmpty(keyword)
            ? _allItems
            : _allItems.Where(i => i.FolderName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                                   i.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredItems.Clear();
        foreach (var item in list)
            FilteredItems.Add(item);
        IsEmpty = FilteredItems.Count == 0;
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Confirm(WorldPickItem item)
    {
        RequestClose?.Invoke(this, item);
    }
}

public sealed class WorldPickItem(WorldSaveInfo info)
{
    public WorldSaveInfo Info { get; } = info;
    public string FolderName => Info.FolderName;
    public string DisplayName => string.IsNullOrWhiteSpace(Info.LevelName) ? Info.FolderName : Info.LevelName;
    public string Summary => $"{Info.Version ?? "未知版本"}·{GetGameModeText(Info.GameMode)}";
    public string LastPlayedText => $"最近游玩：{Info.LastPlayedTime ?? Info.LastWriteTime:yyyy-MM-dd HH:mm}";

    public Bitmap? Icon
    {
        get
        {
            var path = Info.IconPath;
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? new Bitmap(path) : null;
        }
    }

    private static string GetGameModeText(int? gameMode)
    {
        return gameMode switch { 0 => "生存", 1 => "创造", 2 => "冒险", 3 => "旁观", _ => "未知模式" };
    }
}