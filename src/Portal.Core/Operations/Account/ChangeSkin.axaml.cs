using System.ComponentModel;
using System.IO;
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
using Pointer = LiteSkinViewer3D.Shared.Enums.PointerType;
using Portal.Core.Minecraft.Classes;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Core.Operations.Account;

public partial class ChangeSkin : UserControl
{
    private float _initialY;

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
        SkinViewer.UpdatePointerMoved(type, new Vector2((float)pos.X * 2f, _initialY));
    }

    private void OnPointerPressed(object? s, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        _initialY = (float)pos.Y;
        var prop = e.GetCurrentPoint(this).Properties;
        var type = Pointer.None;
        if (prop.IsLeftButtonPressed) type = Pointer.PointerLeft;
        else if (prop.IsRightButtonPressed) type = Pointer.PointerRight;
        SkinViewer.UpdatePointerPressed(type, new Vector2((float)pos.X, _initialY));
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
            new ChangeSkinViewModel(currentSkinPath, false), hostId: hostId, options: options);

        return result;
    }

    public static async Task<string?> Preview(string? hostId, MinecraftAccount account)
    {
        var skinPath = Path.Combine(Path.GetTempPath(), $"preview_{account.Name}.png");
        await File.WriteAllBytesAsync(skinPath, Convert.FromBase64String(account.Skin));

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
            new ChangeSkinViewModel(skinPath, true), hostId: hostId, options: options);

        try { File.Delete(skinPath); } catch { }
        return result;
    }
}

public partial class ChangeSkinViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty]
    public partial string? SkinPath { get; set; }

    [ObservableProperty]
    public partial bool IsPreview { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public ChangeSkinViewModel(string? currentSkinPath, bool isPreview = false)
    {
        SkinPath = currentSkinPath;
        IsPreview = isPreview;
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
