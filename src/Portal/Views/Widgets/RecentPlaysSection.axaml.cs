using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.ViewModels;

namespace Portal.Views.Widgets;

public partial class RecentPlaysSection : UserControl
{
    public static readonly StyledProperty<Thickness> HeaderMarginProperty =
        AvaloniaProperty.Register<RecentPlaysSection, Thickness>(nameof(HeaderMargin), new Thickness(15, 0, 0, 0));

    public static readonly StyledProperty<double> MinItemHeightProperty =
        AvaloniaProperty.Register<RecentPlaysSection, double>(nameof(MinItemHeight), 0);

    public static readonly RoutedEvent<RoutedEventArgs> RefreshRequestedEvent =
        RoutedEvent.Register<RecentPlaysSection, RoutedEventArgs>(nameof(RefreshRequestedEvent),
            RoutingStrategies.Bubble);

    public RecentPlaysSection()
    {
        InitializeComponent();
    }

    public Thickness HeaderMargin
    {
        get => GetValue(HeaderMarginProperty);
        set => SetValue(HeaderMarginProperty, value);
    }

    public double MinItemHeight
    {
        get => GetValue(MinItemHeightProperty);
        set => SetValue(MinItemHeightProperty, value);
    }

    private void RefreshRecentPlays_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(RefreshRequestedEvent));
    }

    private void RecentPlaysRepeater_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is RecentPlaysViewModelBase viewModel)
            viewModel.SetRecentPlayWidth(e.NewSize.Width);
    }
}
