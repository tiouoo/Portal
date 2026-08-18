using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.Widgets;
using Portal.ViewModels;
using Portal.Views.Components;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Widgets;

public sealed class NewsWidget : IWidgetContent
{
    private readonly TextBlock _dateText;
    private readonly TextBlock _descText;
    private readonly TextBlock _editionText;
    private readonly TextBlock _emptyText;


    private readonly NewsImage _image;
    private readonly TextBlock _titleText;
    private readonly Border _typeTag;
    private readonly TextBlock _typeText;
    private NewsEntry? _current;
    private NewsWidgetData? _data;

    public NewsWidget(WidgetCellSize size)
    {
        Size = size;

        _image = new NewsImage((Uri?)null) { Stretch = Stretch.UniformToFill };
        _titleText = new TextBlock
        {
            FontSize = 15, MaxLines = 1,
            FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.WrapWithOverflow
        };
        _descText = new TextBlock
        {
            FontSize = 12,
            LineHeight = 16,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.WrapWithOverflow
        };
        _descText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InnerForegroundColor"));

        _dateText = new TextBlock { MaxLines = 1, FontSize = 12 };
        _dateText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InnerForegroundColor"));

        _editionText = new TextBlock { MaxLines = 1, FontSize = 11 };
        _editionText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InnerForegroundColor"));

        _typeText = new TextBlock { MaxLines = 1, FontSize = 11 };
        _typeText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InnerForegroundColor"));

        _typeTag = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 1),
            Child = _typeText
        };
        _typeTag.Bind(Border.BorderBrushProperty, this.GetResourceObservable("TranslucentBorderBrush"));
        _typeTag.Bind(Border.BackgroundProperty, this.GetResourceObservable("TranslucentBackgroundColor"));

        _emptyText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "暂无新闻", MaxLines = 1
        };
        _emptyText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("InnerForegroundColor"));

        Content = size.Rows == 1 ? CreateHorizontalLayout() : CreateVerticalLayout();
    }

    public NewsFilterType Filter { get; private set; } = NewsFilterType.All;

    private Control CreateHorizontalLayout()
    {
        _descText.MaxLines = 3;
        _descText.Margin = new Thickness(0, -2, 0, 0);


        var imageBorder = new Border
        {
            ClipToBounds = true,
            CornerRadius = new CornerRadius(10),
            Width = 100,
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 10, 0),
            Child = _image
        };

        var editionTag = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 1),
            Child = _editionText
        };
        editionTag.Bind(Border.BorderBrushProperty, this.GetResourceObservable("TranslucentBorderBrush"));
        editionTag.Bind(Border.BackgroundProperty, this.GetResourceObservable("TranslucentBackgroundColor"));

        var tagPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { editionTag, _typeTag }
        };


        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, -5),
            Children = { _dateText, tagPanel }
        };
        var topRow = new StackPanel
        {
            Spacing = 4,
            Children = { _titleText, _descText }
        };
        var rightPanel = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 10, 12, 10)
        };
        DockPanel.SetDock(bottomRow, Dock.Bottom);
        rightPanel.Children.Add(bottomRow);
        rightPanel.Children.Add(topRow);


        DockPanel.SetDock(imageBorder, Dock.Left);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(imageBorder);
        root.Children.Add(rightPanel);


        var overlay = new Grid();
        overlay.Children.Add(root);
        overlay.Children.Add(_emptyText);
        return overlay;
    }

    private Control CreateVerticalLayout()
    {
        var imageBorder = new Border
        {
            ClipToBounds = true,
            CornerRadius = new CornerRadius(10),
            Height = 130,
            Margin = new Thickness(12, 12, 12, 0),
            Child = _image
        };

        var editionTag = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 1),
            Child = _editionText
        };
        editionTag.Bind(Border.BorderBrushProperty, this.GetResourceObservable("TranslucentBorderBrush"));
        editionTag.Bind(Border.BackgroundProperty, this.GetResourceObservable("TranslucentBackgroundColor"));


        var bottomPanel = new Panel
        {
            Margin = new Thickness(0, 3, 0, 0),
            Children =
            {
                _dateText,
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { editionTag, _typeTag }
                }
            }
        };


        var infoPanel = new DockPanel
        {
            Margin = new Thickness(14, 10, 14, 8),
            LastChildFill = true
        };
        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        infoPanel.Children.Add(bottomPanel);
        DockPanel.SetDock(_titleText, Dock.Top);
        infoPanel.Children.Add(_titleText);
        infoPanel.Children.Add(_descText);


        DockPanel.SetDock(imageBorder, Dock.Top);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(imageBorder);
        root.Children.Add(infoPanel);


        var overlay = new Grid();
        overlay.Children.Add(root);
        overlay.Children.Add(_emptyText);
        return overlay;
    }

    public override void Initialize(WidgetLayoutData layout)
    {
        _data = layout.Data as NewsWidgetData;
        if (_data == null)
        {
            _data = new NewsWidgetData();
            layout.Data = _data;
        }

        Filter = ParseFilter(_data.Filter);

        NewsService.NewsUpdated += OnNewsUpdated;
        Unloaded += (_, _) => NewsService.NewsUpdated -= OnNewsUpdated;

        RefreshNews();
    }

    private static NewsFilterType ParseFilter(string? s)
    {
        return s switch
        {
            "Java" => NewsFilterType.Java,
            "Bedrock" => NewsFilterType.Bedrock,
            _ => NewsFilterType.All
        };
    }

    private static string FilterToString(NewsFilterType f)
    {
        return f switch
        {
            NewsFilterType.Java => "Java",
            NewsFilterType.Bedrock => "Bedrock",
            _ => "All"
        };
    }

    private void OnNewsUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshNews);
    }

    private void RefreshNews()
    {
        var list = Filter switch
        {
            NewsFilterType.Java => NewsService.JavaNews,
            NewsFilterType.Bedrock => NewsService.BedrockNews,
            _ => NewsService.JavaNews.Concat(NewsService.BedrockNews)
        };
        _current = list.OrderByDescending(x => x.Date).FirstOrDefault();
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var entry = _current;
        var hasNews = entry != null;
        _emptyText.IsVisible = !hasNews;

        if (entry == null)
        {
            _image.Source = null;
            _titleText.Text = string.Empty;
            _descText.Text = string.Empty;
            _dateText.Text = string.Empty;
            _editionText.Text = string.Empty;
            _typeText.Text = string.Empty;
            _typeTag.IsVisible = false;
            return;
        }


        _image.Source = entry.ImageUrl;
        _titleText.Text = entry.Title;
        _descText.Text = string.IsNullOrEmpty(entry.ShortText) ? string.Empty : entry.ShortText + "...";
        _dateText.Text = entry.RelativeDate;
        _editionText.Text = entry.Edition == NewsEdition.Java ? "Java" : "基岩";
        _typeText.Text = entry.Type;
        _typeTag.IsVisible = !string.IsNullOrEmpty(entry.Type);
    }

    public void SetFilter(NewsFilterType filter)
    {
        if (Filter == filter) return;
        Filter = filter;
        if (_data != null)
            _data.Filter = FilterToString(filter);
        RefreshNews();
    }

    public override void PerformClick()
    {
        if (_current != null)
        {
            NewsDetailsPage.Open(TopLevel.GetTopLevel(this)!, _current);
            return;
        }

        if (TopLevel.GetTopLevel(this) is not TioTabWindowBase window)
            return;
        var tab = new TabEntry(window, new NewsPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}