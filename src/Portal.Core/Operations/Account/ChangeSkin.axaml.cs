using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteSkinViewer3D.Avalonia.Controls;
using LiteSkinViewer3D.Shared.Enums;
using Pointer = LiteSkinViewer3D.Shared.Enums.PointerType;
using Portal.Core.Minecraft.Classes;
using TioUi.Common;
using TioUi.Common.Interfaces;
using TioUi.Controls;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Core.Operations.Account;

public partial class ChangeSkin : UserControl
{
    private float _initialY;
    private Pointer _pressedPointer;

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
        SkinViewer.RenderingFailed += OnSkinViewerRenderingFailed;
        SkinViewer.RenderMode = SkinRenderMode.MSAA;
        SkinViewer.IsTopLayer3D = true;
    }

    private void OnPointerMoved(object? s, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var type = Pointer.None;
        var prop = e.GetCurrentPoint(this).Properties;
        if (prop.IsLeftButtonPressed) type = Pointer.PointerLeft;
        else if (prop.IsRightButtonPressed) type = Pointer.PointerRight;
        if (type == Pointer.PointerRight)
        {
            SkinViewer.UpdatePointerMoved(type, new Vector2((float)pos.X, (float)pos.Y));
            return;
        }

        var isViewLocked = (DataContext as ChangeSkinViewModel)?.IsViewLocked == true;
        var point = isViewLocked
            ? new Vector2((float)pos.X * 8f, _initialY)
            : new Vector2((float)pos.X * 8f, (float)pos.Y * 8f);
        SkinViewer.UpdatePointerMoved(type, point);
    }

    private void OnPointerPressed(object? s, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        _initialY = (float)pos.Y;
        var prop = e.GetCurrentPoint(this).Properties;
        var type = Pointer.None;
        if (prop.IsLeftButtonPressed) type = Pointer.PointerLeft;
        else if (prop.IsRightButtonPressed) type = Pointer.PointerRight;
        _pressedPointer = type;
        var isViewLocked = (DataContext as ChangeSkinViewModel)?.IsViewLocked == true;
        var point = isViewLocked
            ? new Vector2((float)pos.X, _initialY)
            : new Vector2((float)pos.X * 8f, _initialY * 8f);
        SkinViewer.UpdatePointerPressed(type, point);
    }

    private void OnIsViewLockedChanged(object? sender, RoutedEventArgs e)
    {
        SkinViewer.Reset();
    }

    private void OnPointerReleased(object? s, PointerReleasedEventArgs e)
    {
        var pos = e.GetPosition(this);
        SkinViewer.UpdatePointerReleased(_pressedPointer, new Vector2((float)pos.X, (float)pos.Y));
        _pressedPointer = Pointer.None;
    }

    private void OnPointerWheelChanged(object? s, PointerWheelEventArgs e)
    {
        SkinViewer.UpdatePointerWheelChanged(e.Delta.Y > 0);
    }

    private void OnSkinViewerRenderingFailed(object? sender, Exception e)
    {
        TopLevel.GetTopLevel(this)?.Notice("皮肤渲染失败", NotificationType.Error);
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
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
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
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
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

    [ObservableProperty] public partial bool IsPreview { get; set; }

    [ObservableProperty] public partial bool IsViewLocked { get; set; } = true;

    public string Title => IsPreview ? "预览皮肤" : "更换皮肤";

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
