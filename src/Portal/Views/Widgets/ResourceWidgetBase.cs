using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Classes.Entries;
using Portal.Core.Module.Widgets;
using Portal.Core.Services.SystemResources;

namespace Portal.Views.Widgets;

public abstract class ResourceWidgetBase : IWidgetContent
{
    public static readonly DirectProperty<ResourceWidgetBase, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, string>(nameof(Title), o => o.Title, (o, v) => o.Title = v);

    public static readonly DirectProperty<ResourceWidgetBase, string> PrimaryTextProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, string>(nameof(PrimaryText), o => o.PrimaryText,
            (o, v) => o.PrimaryText = v);

    public static readonly DirectProperty<ResourceWidgetBase, string> SecondaryTextProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, string>(nameof(SecondaryText), o => o.SecondaryText,
            (o, v) => o.SecondaryText = v);

    public static readonly DirectProperty<ResourceWidgetBase, double> PercentageProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, double>(nameof(Percentage), o => o.Percentage,
            (o, v) => o.Percentage = v);

    public static readonly DirectProperty<ResourceWidgetBase, double> ProgressValueProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, double>(nameof(ProgressValue), o => o.ProgressValue,
            (o, v) => o.ProgressValue = v);

    public static readonly DirectProperty<ResourceWidgetBase, double> ProgressMaximumProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, double>(nameof(ProgressMaximum), o => o.ProgressMaximum,
            (o, v) => o.ProgressMaximum = v);

    public static readonly DirectProperty<ResourceWidgetBase, string?> IconGlyphProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, string?>(nameof(IconGlyph),
            o => o.IconGlyph, (o, v) => o.IconGlyph = v);

    public static readonly DirectProperty<ResourceWidgetBase, bool> HasSecondaryTextProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, bool>(nameof(HasSecondaryText), o => o.HasSecondaryText,
            (o, v) => o.HasSecondaryText = v);

    public static readonly DirectProperty<ResourceWidgetBase, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<ResourceWidgetBase, bool>(nameof(IsLoading), o => o.IsLoading,
            (o, v) => o.IsLoading = v);

    private bool _hasSecondaryText = true;
    private string? _iconGlyph;
    private bool _isLoading = true;
    private double _percentage;
    private string _primaryText = "--";
    private double _progressMaximum = 100;
    private double _progressValue;
    private string _secondaryText = string.Empty;
    private string _title = string.Empty;

    protected ResourceWidgetBase(WidgetCellSize size)
    {
        Size = size;
        Content = CreateView(size);
    }

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public string PrimaryText
    {
        get => _primaryText;
        set => SetAndRaise(PrimaryTextProperty, ref _primaryText, value);
    }

    public string SecondaryText
    {
        get => _secondaryText;
        set => SetAndRaise(SecondaryTextProperty, ref _secondaryText, value);
    }

    public double Percentage
    {
        get => _percentage;
        set => SetAndRaise(PercentageProperty, ref _percentage, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetAndRaise(ProgressValueProperty, ref _progressValue, value);
    }

    public double ProgressMaximum
    {
        get => _progressMaximum;
        set => SetAndRaise(ProgressMaximumProperty, ref _progressMaximum, value);
    }

    public string? IconGlyph
    {
        get => _iconGlyph;
        set => SetAndRaise(IconGlyphProperty, ref _iconGlyph, value);
    }

    public bool HasSecondaryText
    {
        get => _hasSecondaryText;
        set => SetAndRaise(HasSecondaryTextProperty, ref _hasSecondaryText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetAndRaise(IsLoadingProperty, ref _isLoading, value);
    }

    public abstract ResourceKind ResourceKind { get; }

    public override void Initialize(WidgetLayoutData layout)
    {
        SystemResourceService.Instance.Updated += OnServiceUpdated;
        Unloaded += (_, _) => SystemResourceService.Instance.Updated -= OnServiceUpdated;


        OnServiceUpdated(SystemResourceService.Instance, SystemResourceService.Instance.Latest);
    }

    private void OnServiceUpdated(object? sender, ResourceSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnUpdate(snapshot);
            IsLoading = false;
        });
    }

    protected abstract void OnUpdate(ResourceSnapshot snapshot);

    private UserControl CreateView(WidgetCellSize size)
    {
        UserControl view = size switch
        {
            (1, 1) => new ResourceWidgetView1x1(),
            (2, 1) => new ResourceWidgetView2x1(),
            (2, 2) => new ResourceWidgetView2x2(),
            _ => new ResourceWidgetView1x1()
        };
        view.DataContext = this;
        return view;
    }
}

public enum ResourceKind
{
    Cpu,
    Memory,
    Disk,
    Network,
    Gpu
}