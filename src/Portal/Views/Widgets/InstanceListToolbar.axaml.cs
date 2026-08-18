using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Portal.Views.Widgets;

public partial class InstanceListToolbar : UserControl
{
    public static readonly StyledProperty<bool> ShowFolderFilterProperty =
        AvaloniaProperty.Register<InstanceListToolbar, bool>(nameof(ShowFolderFilter), false);

    public static readonly StyledProperty<bool> ShowOpenInstancesTitleProperty =
        AvaloniaProperty.Register<InstanceListToolbar, bool>(nameof(ShowOpenInstancesTitle), true);

    public static readonly StyledProperty<bool> ShowTitleIconProperty =
        AvaloniaProperty.Register<InstanceListToolbar, bool>(nameof(ShowTitleIcon), false);

    public static readonly RoutedEvent<RoutedEventArgs> RefreshRequestedEvent =
        RoutedEvent.Register<InstanceListToolbar, RoutedEventArgs>(nameof(RefreshRequestedEvent),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> OpenInstancesRequestedEvent =
        RoutedEvent.Register<InstanceListToolbar, RoutedEventArgs>(nameof(OpenInstancesRequestedEvent),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ImportModpackRequestedEvent =
        RoutedEvent.Register<InstanceListToolbar, RoutedEventArgs>(nameof(ImportModpackRequestedEvent),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> AddFolderRequestedEvent =
        RoutedEvent.Register<InstanceListToolbar, RoutedEventArgs>(nameof(AddFolderRequestedEvent),
            RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> CreateInstanceRequestedEvent =
        RoutedEvent.Register<InstanceListToolbar, RoutedEventArgs>(nameof(CreateInstanceRequestedEvent),
            RoutingStrategies.Bubble);

    public InstanceListToolbar()
    {
        InitializeComponent();
    }

    public bool ShowFolderFilter
    {
        get => GetValue(ShowFolderFilterProperty);
        set => SetValue(ShowFolderFilterProperty, value);
    }

    public bool ShowOpenInstancesTitle
    {
        get => GetValue(ShowOpenInstancesTitleProperty);
        set => SetValue(ShowOpenInstancesTitleProperty, value);
    }

    public bool ShowTitleIcon
    {
        get => GetValue(ShowTitleIconProperty);
        set => SetValue(ShowTitleIconProperty, value);
    }

    private void RefreshInstance_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(RefreshRequestedEvent));
    }

    private void ButtonOpenInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(OpenInstancesRequestedEvent));
    }

    private void ImportModpack_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ImportModpackRequestedEvent));
    }

    private void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(AddFolderRequestedEvent));
    }

    private void ToDownload_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CreateInstanceRequestedEvent));
    }
}
