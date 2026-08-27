using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module;
using Portal.Core.Minecraft.Services;
using Portal.Views.Pages;
using TioUi.Common.Extensions;

namespace Portal.Views.Widgets;

public partial class InstanceCard : UserControl
{
    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<InstanceCard, double>(nameof(CardHeight), 125);

    public static readonly StyledProperty<Thickness> CardMarginProperty =
        AvaloniaProperty.Register<InstanceCard, Thickness>(nameof(CardMargin), new Thickness(12, 12, 12, 5));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<InstanceCard, double>(nameof(IconSize), 40);

    public static readonly StyledProperty<Thickness> IconMarginProperty =
        AvaloniaProperty.Register<InstanceCard, Thickness>(nameof(IconMargin), new Thickness(0, 0, 8, 0));

    public static readonly StyledProperty<Thickness> TitleMarginProperty =
        AvaloniaProperty.Register<InstanceCard, Thickness>(nameof(TitleMargin), new Thickness(2, 3, 0, 0));

    public static readonly StyledProperty<TextWrapping> TitleWrappingProperty =
        AvaloniaProperty.Register<InstanceCard, TextWrapping>(nameof(TitleWrapping), TextWrapping.Wrap);

    public static readonly StyledProperty<double> ContentFontSizeProperty =
        AvaloniaProperty.Register<InstanceCard, double>(nameof(ContentFontSize), 12);

    public static readonly StyledProperty<Thickness> FolderMarginProperty =
        AvaloniaProperty.Register<InstanceCard, Thickness>(nameof(FolderMargin), new Thickness(0, 4, 0, 0));

    public static readonly StyledProperty<double> LaunchIconSizeProperty =
        AvaloniaProperty.Register<InstanceCard, double>(nameof(LaunchIconSize), 15);

    public static readonly StyledProperty<double> ActionIconSizeProperty =
        AvaloniaProperty.Register<InstanceCard, double>(nameof(ActionIconSize), 14);

    public static readonly RoutedEvent<RoutedEventArgs> FavoriteChangedEvent =
        RoutedEvent.Register<InstanceCard, RoutedEventArgs>(nameof(FavoriteChangedEvent), RoutingStrategies.Bubble);

    public InstanceCard()
    {
        InitializeComponent();
    }

    public double CardHeight
    {
        get => GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public Thickness CardMargin
    {
        get => GetValue(CardMarginProperty);
        set => SetValue(CardMarginProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public Thickness IconMargin
    {
        get => GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public Thickness TitleMargin
    {
        get => GetValue(TitleMarginProperty);
        set => SetValue(TitleMarginProperty, value);
    }

    public TextWrapping TitleWrapping
    {
        get => GetValue(TitleWrappingProperty);
        set => SetValue(TitleWrappingProperty, value);
    }

    public double ContentFontSize
    {
        get => GetValue(ContentFontSizeProperty);
        set => SetValue(ContentFontSizeProperty, value);
    }

    public Thickness FolderMargin
    {
        get => GetValue(FolderMarginProperty);
        set => SetValue(FolderMarginProperty, value);
    }

    public double LaunchIconSize
    {
        get => GetValue(LaunchIconSizeProperty);
        set => SetValue(LaunchIconSizeProperty, value);
    }

    public double ActionIconSize
    {
        get => GetValue(ActionIconSizeProperty);
        set => SetValue(ActionIconSizeProperty, value);
    }

    private void InstanceCard_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (DataContext is MinecraftInstance instance &&
            TopLevel.GetTopLevel(this) is { } topLevel)
        {
            InstanceDetailPage.Open(instance, topLevel);
        }
    }

    private void LaunchInstance_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance instance)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(instance,
                logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }

    private async void CreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance instance)
            return;

        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(instance));
    }

    private void FavoritedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance { Config: not null } instance)
            return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
        RaiseEvent(new RoutedEventArgs(FavoriteChangedEvent));
    }

    private void BlockInstance_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance instance)
            return;

        BlockListService.Instance.ToggleInstanceBlock(instance);
    }
}
