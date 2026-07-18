using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;

namespace Portal.Views.Pages.InstancePages;

public partial class Mods : UserControl, INotifyPropertyChanged
{
    private readonly MinecraftInstance? _instance;
    private readonly ModService _modService = new();
    private bool _hasLoaded;
    private bool _isLoading;
    private string _filter = string.Empty;

    public ObservableCollection<ModItem> Items { get; } = [];
    public ObservableCollection<ModItem> FilteredItems { get; } = [];
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            RaisePropertyChanged(nameof(IsLoading));
        }
    }

    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string ModCountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";

    public Mods()
    {
        InitializeComponent();
        DataContext = this;
    }

    public Mods(MinecraftInstance instance) : this() => _instance = instance;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;

        _hasLoaded = true;
        IsLoading = true;
        RaiseListProperties();
        var mods = await _modService.ScanAsync(_instance);
        Items.Clear();
        foreach (var mod in mods)
            Items.Add(new ModItem(mod));
        ApplyFilter();
        IsLoading = false;
        RaiseListProperties();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter) ? Items : Items.Where(item =>
            item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
            item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear();
        foreach (var item in filtered)
            FilteredItems.Add(item);
        RaiseListProperties();
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || _instance == null) return;
        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.ModsFolder)));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _hasLoaded = false;
        _ = LoadAsync();
    }

    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilter();
    }

    private void RaiseListProperties()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ModCountText));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ModItem(ModInfo info)
{
    public string DisplayName => info.DisplayName;
    public string FileName => info.FileName;
    public string DescriptionText => info.Description ?? "没有可用的模组描述";
    public bool IsDisabled => info.IsDisabled;
}
