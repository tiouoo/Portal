using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockWorldTemplates : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly MinecraftInstance? _instance;
    private readonly WorldTemplateService _worldTemplateService = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _hasLoaded, _isLoading, _isDisposed;
    private string _filter = string.Empty;

    public ObservableCollection<WorldTemplateItem> Items { get; } = [];
    public ObservableCollection<WorldTemplateItem> FilteredItems { get; } = [];
    public bool IsLoading { get => _isLoading; private set { if (_isLoading != value) { _isLoading = value; RaisePropertyChanged(nameof(IsLoading)); } } }
    public bool IsEmpty => !IsLoading && FilteredItems.Count == 0;
    public string CountText => IsLoading ? string.Empty : $"{FilteredItems.Count} 个";
    public int SelectedCount => Items.Count(item => item.IsSelected);
    public string SelectedCountText => $"批量操作{SelectedCount}个";
    public bool HasMultipleSelection => SelectedCount >= 1;

    public BedrockWorldTemplates() { InitializeComponent(); DataContext = this; }
    public BedrockWorldTemplates(MinecraftInstance instance) : this() => _instance = instance;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _ = LoadAsync(); }

    private async Task LoadAsync()
    {
        if (_hasLoaded || _instance == null) return;
        _hasLoaded = true; IsLoading = true; RaiseListProperties();
        try
        {
            var templates = await _worldTemplateService.ScanAsync(_instance, _disposeCancellation.Token);
            if (_isDisposed) return;
            foreach (var item in Items) item.Dispose();
            Items.Clear();
            foreach (var template in templates) Items.Add(new WorldTemplateItem(template));
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        finally { if (!_isDisposed) { IsLoading = false; RaiseListProperties(); } }
    }

    private async void OpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel && _instance != null)
            await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(_instance.GetSpecialFolder(MinecraftSpecialFolder.WorldTemplatesFolder)));
    }

    private void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e) { _hasLoaded = false; _ = LoadAsync(); }
    private void SearchBox_OnTextChanged(object? sender, TextChangedEventArgs e) { _filter = (sender as TextBox)?.Text ?? string.Empty; ApplyFilter(); }
    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(_filter) ? Items : Items.Where(item => item.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) || item.FileName.Contains(_filter, StringComparison.OrdinalIgnoreCase) || item.DescriptionText.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        FilteredItems.Clear(); foreach (var item in filtered) FilteredItems.Add(item); RaiseListProperties();
    }
    private void WorldTemplateCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed && (sender as Control)?.DataContext is WorldTemplateItem item) { item.IsSelected = !item.IsSelected; RaiseSelectionProperties(); }
    }
    private void SelectAll_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => true);
    private void ClearSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => false);
    private void InvertSelection_OnClick(object? sender, RoutedEventArgs e) => SetSelection(item => !item.IsSelected);
    private async void DeleteSelected_OnClick(object? sender, RoutedEventArgs e) { var selected = Items.Where(item => item.IsSelected).ToArray(); if (selected.Length >= 2 && await ConfirmDeleteAsync($"确定要永久删除选中的 {selected.Length} 个世界模板吗？此操作无法撤销。") == DialogResult.Yes) await DeleteAsync(selected); }
    private async void DeleteWorldTemplate_OnClick(object? sender, RoutedEventArgs e) { if (GetItem(sender) is { } item && await ConfirmDeleteAsync($"确定要永久删除世界模板“{item.DisplayName}”吗？此操作无法撤销。") == DialogResult.Yes) await DeleteAsync([item]); }
    private async void ShowWorldTemplateDetails_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item) return;
        await OverlayDialog.ShowStandardAsync(new TextBlock { Margin = new Thickness(24), Text = item.DetailsText, TextWrapping = TextWrapping.Wrap }, null, this.TryGetHostId(), new OverlayDialogOptions { Title = "世界模板详情", Buttons = DialogButton.OK, CanResize = false });
    }
    private async void OpenWorldTemplateFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        if (OperatingSystem.IsWindows()) { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Info.FilePath}\"") { UseShellExecute = true }); return; }
        await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(item.Info.FilePath)!));
    }
    private async Task<DialogResult> ConfirmDeleteAsync(string message) => await OverlayDialog.ShowStandardAsync(new TextBlock { Margin = new Thickness(24), Text = message, TextWrapping = TextWrapping.Wrap }, null, this.TryGetHostId(), new OverlayDialogOptions { Title = "删除世界模板", Mode = DialogMode.Error, Buttons = DialogButton.YesNo, OverrideYesButtonText = "删除", OverrideNoButtonText = "取消", CanLightDismiss = false, CanResize = false });
    private async Task DeleteAsync(IEnumerable<WorldTemplateItem> items)
    {
        var failed = 0; foreach (var item in items) try { Directory.Delete(item.Info.FilePath, true); } catch (IOException) { failed++; } catch (UnauthorizedAccessException) { failed++; }
        _hasLoaded = false; await LoadAsync();
        if (TopLevel.GetTopLevel(this) is { } topLevel) NotificationGateway.Notice(topLevel, failed == 0 ? "已删除所选世界模板" : $"删除完成，但有 {failed} 个世界模板操作失败", failed == 0 ? NotificationType.Success : NotificationType.Warning);
    }
    private static WorldTemplateItem? GetItem(object? sender) => (sender as Control)?.Tag as WorldTemplateItem;
    private void SetSelection(Func<WorldTemplateItem, bool> selection) { foreach (var item in Items) item.IsSelected = selection(item); RaiseSelectionProperties(); }
    private void RaiseListProperties() { RaisePropertyChanged(nameof(IsEmpty)); RaisePropertyChanged(nameof(CountText)); }
    private void RaiseSelectionProperties() { RaisePropertyChanged(nameof(SelectedCount)); RaisePropertyChanged(nameof(SelectedCountText)); RaisePropertyChanged(nameof(HasMultipleSelection)); }
    public new event PropertyChangedEventHandler? PropertyChanged;
    private void RaisePropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public void Dispose() { if (_isDisposed) return; _isDisposed = true; _disposeCancellation.Cancel(); foreach (var item in Items) item.Dispose(); _disposeCancellation.Dispose(); }
}

public sealed class WorldTemplateItem(WorldTemplateInfo info) : INotifyPropertyChanged, IDisposable
{
    private bool _isSelected;
    public WorldTemplateInfo Info { get; } = info;
    public string DisplayName => Info.DisplayName;
    public string FileName => Info.FileName;
    public string DescriptionText => string.IsNullOrWhiteSpace(Info.Description) ? "没有可用的世界模板描述" : Info.Description;
    public string BaseGameVersionText => Info.BaseGameVersion ?? "未知";
    public string PackSummary => $"资源包 {Info.ResourcePacks.Count} 个，行为包 {Info.BehaviorPacks.Count} 个";
    public string DetailsText => $"名称：{DisplayName}\n文件夹：{FileName}\nUUID：{Info.Uuid ?? "未知"}\n版本：{Info.Version ?? "未知"}\n基础游戏版本：{BaseGameVersionText}\n模块 UUID：{(Info.ModuleUuids.Count == 0 ? "未知" : string.Join("、", Info.ModuleUuids))}\n\n默认资源包：\n{FormatPacks(Info.ResourcePacks)}\n\n默认行为包：\n{FormatPacks(Info.BehaviorPacks)}\n\n{DescriptionText}";
    public Bitmap? Icon { get; } = CreateIcon(info.IconData);
    public bool HasIcon => Icon != null;
    public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } }
    private static string FormatPacks(IReadOnlyList<WorldTemplatePackReference> packs) => packs.Count == 0 ? "无" : string.Join('\n', packs.Select(pack => $"{pack.PackId} | 子包：{pack.Subpack ?? "默认"} | 版本：{pack.Version ?? "未知"}"));
    private static Bitmap? CreateIcon(byte[]? data) { if (data == null) return null; try { return new Bitmap(new MemoryStream(data)); } catch (InvalidDataException) { return null; } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Dispose() => Icon?.Dispose();
}
