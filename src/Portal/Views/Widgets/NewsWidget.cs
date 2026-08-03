using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Module.Widgets;
using Portal.ViewModels;
using Portal.Views.Components;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Widgets;

/// <summary>
/// 新闻小组件。展示最新一条 Minecraft 新闻（Java 版或基岩版）。
/// 支持右键切换过滤：全部 / 仅 Java 版 / 仅基岩版。
/// 2×1 系列采用水平布局（左图右文），可横向拓展到 6×1；
/// 2×2 系列采用垂直布局（模拟新闻页卡片），可纵向拓展到 2×6。
/// 拓展时图片大小不变，仅文字显示区域变大。
/// </summary>
public sealed class NewsWidget : IWidgetContent
{
    private NewsWidgetData? _data;
    private NewsFilterType _filter = NewsFilterType.All;
    private NewsEntry? _current;

    // UI 元素
    private readonly NewsImage _image;
    private readonly TextBlock _titleText;
    private readonly TextBlock _descText;
    private readonly TextBlock _dateText;
    private readonly TextBlock _editionText;
    private readonly Border _typeTag;
    private readonly TextBlock _typeText;
    private readonly TextBlock _emptyText;

    public NewsFilterType Filter => _filter;

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

    /// <summary>水平布局（2×1 ~ 6×1）：左侧正方形图标，右侧上描述、下 tag。</summary>
    private Control CreateHorizontalLayout()
    {
        _descText.MaxLines = 3;
        _descText.Margin = new Thickness(0, -2, 0, 0);
        
        // 图片固定正方形，边长跟随布局高度（由父容器拉伸）
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

        // 右侧上下两块：上=标题+描述，下=日期+tag
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

        // 图片 Dock.Left 固定 100×100；rightPanel 填充剩余宽度
        DockPanel.SetDock(imageBorder, Dock.Left);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(imageBorder);
        root.Children.Add(rightPanel);

        // 空状态文字叠加在最上层
        var overlay = new Grid();
        overlay.Children.Add(root);
        overlay.Children.Add(_emptyText);
        return overlay;
    }

    /// <summary>垂直布局（2×2 ~ 2×6）：上方图片，下方标题/描述/tag，模拟新闻页卡片。</summary>
    private Control CreateVerticalLayout()
    {
        // 图片高度固定 130，拓展时不变
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

        // 底部：日期（左）+ tag（右）
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

        // DockPanel 顺序：bottom Dock.Bottom，title Dock.Top，desc 填充剩余
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

        // 图片 Dock.Top 占满宽度、固定 130 高；infoPanel 填充剩余空间
        DockPanel.SetDock(imageBorder, Dock.Top);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(imageBorder);
        root.Children.Add(infoPanel);

        // 空状态文字叠加在最上层
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

        _filter = ParseFilter(_data.Filter);

        NewsService.NewsUpdated += OnNewsUpdated;
        Unloaded += (_, _) => NewsService.NewsUpdated -= OnNewsUpdated;

        RefreshNews();
    }

    private static NewsFilterType ParseFilter(string? s) => s switch
    {
        "Java" => NewsFilterType.Java,
        "Bedrock" => NewsFilterType.Bedrock,
        _ => NewsFilterType.All
    };

    private static string FilterToString(NewsFilterType f) => f switch
    {
        NewsFilterType.Java => "Java",
        NewsFilterType.Bedrock => "Bedrock",
        _ => "All"
    };

    private void OnNewsUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshNews);
    }

    private void RefreshNews()
    {
        IEnumerable<NewsEntry> list = _filter switch
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

        // 图片用 AsyncImageLoader 通过 Source 异步加载（NewsImage 已固定加载器）
        _image.Source = entry.ImageUrl;
        _titleText.Text = entry.Title;
        _descText.Text = string.IsNullOrEmpty(entry.ShortText) ? string.Empty : entry.ShortText + "...";
        _dateText.Text = entry.RelativeDate;
        _editionText.Text = entry.Edition == NewsEdition.Java ? "Java" : "基岩";
        _typeText.Text = entry.Type;
        _typeTag.IsVisible = !string.IsNullOrEmpty(entry.Type);
    }

    /// <summary>切换过滤模式并持久化。</summary>
    public void SetFilter(NewsFilterType filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        if (_data != null)
            _data.Filter = FilterToString(filter);
        RefreshNews();
    }

    public override void PerformClick()
    {
        // 小组件展示的就是 _current 这条新闻，点击直接进入它的详情页；
        // 如果没有可展示的新闻，则回退到新闻列表页。
        if (_current != null)
        {
            NewsDetailsPage.Open(TopLevel.GetTopLevel(this), _current);
            return;
        }

        if (TopLevel.GetTopLevel(this) is not TioTabWindowBase window)
            return;
        var tab = new TabEntry(window, new NewsPage());
        window.CreateTab(tab);
        window.SelectTab(tab);
    }
}