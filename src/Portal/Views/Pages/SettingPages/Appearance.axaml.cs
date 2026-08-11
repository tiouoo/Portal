using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using Portal.Views.Components;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("界面外观", "设置/界面外观", "Appearance")]
public partial class Appearance : DataUserControl
{
    public Appearance()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            ListBox.SelectedIndex = (int)Const.Data.ConfigEntry.Theme;
            UpdateApplyButtonState();
        };
        ListBox.SelectionChanged += (_, _) =>
        {
            if (ListBox.SelectedIndex == -1) return;
            Const.Data.ConfigEntry.Theme = (TioUi.Shared.Theme)ListBox.SelectedIndex;
        };
    }

    public object IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private void AppScaleSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        => UpdateApplyButtonState();

    private void ApplyScale_OnClick(object? sender, RoutedEventArgs e)
    {
        ApplyScale(AppScaleSlider.Value);
    }

    private async void CustomScale_OnClick(object? sender, RoutedEventArgs e)
    {
        var result = await OverlayDialog.ShowCustomAsync<ScaleInputDialog, ScaleInputDialogViewModel, double?>(
            new ScaleInputDialogViewModel(Const.Data.ConfigEntry.AppScale), hostId: this.TryGetHostId());
        if (result is { } scale)
        {
            if (scale is < 0.5 or > 5) return;
            ApplyScale(scale);
        }
    }

    private void ApplyScale(double scale)
    {
        Const.Data.ConfigEntry.AppScale = scale;
        UpdateApplyButtonState();
    }

    private void UpdateApplyButtonState()
    {
        var applied = Const.Data.ConfigEntry.AppScale;
        var pending = AppScaleSlider.Value;
        ApplyScaleButton.IsEnabled = Math.Abs(pending - applied) > 0.0001;
    }
}