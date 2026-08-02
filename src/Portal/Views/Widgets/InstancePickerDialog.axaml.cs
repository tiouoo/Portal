using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using TioUi.Common.Interfaces;

namespace Portal.Views.Widgets;

public partial class InstancePickerDialog : UserControl
{
    public InstancePickerDialog() => InitializeComponent();

    private void InstanceItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (sender is Control { Tag: MinecraftInstance instance } && DataContext is InstancePickerDialogViewModel vm)
            vm.Confirm(instance);
    }
}

public partial class InstancePickerDialogViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<MinecraftInstance> FilteredInstances { get; } = [];

    public InstancePickerDialogViewModel()
    {
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim();
        var list = InstanceManager.Instance.Instances
            .Where(i => string.IsNullOrEmpty(keyword) ||
                        i.InstanceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        i.FolderName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        FilteredInstances.Clear();
        foreach (var instance in list)
            FilteredInstances.Add(instance);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, null);

    public void Confirm(MinecraftInstance instance) =>
        RequestClose?.Invoke(this, instance);

    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}
