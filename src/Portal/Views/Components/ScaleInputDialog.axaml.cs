using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TioUi.Common.Interfaces;

namespace Portal.Views.Components;

public partial class ScaleInputDialog : UserControl
{
    public ScaleInputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PercentTextBox.Focus();
            PercentTextBox.SelectAll();
        };
    }
}

public partial class ScaleInputDialogViewModel(double currentScale) : ObservableObject, IDialogContext
{
    [ObservableProperty]
    public partial string PercentText { get; set; } =
        ((int)Math.Round(currentScale * 100)).ToString(CultureInfo.InvariantCulture);

    [ObservableProperty] public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText != null;

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void Confirm()
    {
        var trimmed = PercentText?.Trim() ?? string.Empty;
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            || percent < 50 || percent > 500)
        {
            ErrorText = $"请输入 50 ~ 500 之间的整数百分比（当前输入：\"{trimmed}\"）。";
            return;
        }

        RequestClose?.Invoke(this, percent / 100.0);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}