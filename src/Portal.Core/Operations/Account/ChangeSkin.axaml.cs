using System.ComponentModel;
using System.Numerics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteSkinViewer3D.Avalonia.Controls;
using LiteSkinViewer3D.Shared.Enums;
using Pointer = LiteSkinViewer3D.Shared.Enums.PointerType;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public partial class ChangeSkin : UserControl
{
    public ChangeSkin()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SkinViewer.PointerMoved += OnPointerMoved;
        SkinViewer.PointerPressed += OnPointerPressed;
        SkinViewer.PointerReleased += OnPointerReleased;
        SkinViewer.PointerWheelChanged += OnPointerWheelChanged;
    }

    private void OnPointerMoved(object? s, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var type = Pointer.None;
        var prop = e.GetCurrentPoint(this).Properties;
        if (prop.IsLeftButtonPressed) type = Pointer.PointerLeft;
        else if (prop.IsRightButtonPressed) type = Pointer.PointerRight;
        SkinViewer.UpdatePointerMoved(type, new Vector2((float)pos.X, (float)pos.Y));
    }

    private void OnPointerPressed(object? s, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        var prop = e.GetCurrentPoint(this).Properties;
        var type = Pointer.None;
        if (prop.IsLeftButtonPressed) type = Pointer.PointerLeft;
        else if (prop.IsRightButtonPressed) type = Pointer.PointerRight;
        SkinViewer.UpdatePointerPressed(type, new Vector2((float)pos.X, (float)pos.Y));
    }

    private void OnPointerReleased(object? s, PointerReleasedEventArgs e)
    {
        var pos = e.GetPosition(this);
        var prop = e.GetCurrentPoint(this).Properties;
        SkinViewer.UpdatePointerReleased(Pointer.None, new Vector2((float)pos.X, (float)pos.Y));
    }

    private void OnPointerWheelChanged(object? s, PointerWheelEventArgs e)
    {
        SkinViewer.UpdatePointerWheelChanged(e.Delta.Y > 0);
    }
}

public static class ChangeSkinDialog
{
    public static async Task<string?> Show(string? hostId, string? currentSkinPath)
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Center
        };

        var result = await OverlayDialog.ShowCustomAsync<ChangeSkin, ChangeSkinViewModel, string?>(
            new ChangeSkinViewModel(currentSkinPath), hostId: hostId, options: options);

        return result;
    }
}

public partial class ChangeSkinViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty]
    public partial string? SkinPath { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public ChangeSkinViewModel(string? currentSkinPath)
    {
        SkinPath = currentSkinPath;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    partial void OnSkinPathChanged(string? value)
    {
        (ConfirmCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool CanConfirm()
    {
        return !string.IsNullOrEmpty(SkinPath) && System.IO.File.Exists(SkinPath);
    }

    private void Confirm()
    {
        RequestClose?.Invoke(this, SkinPath);
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}
