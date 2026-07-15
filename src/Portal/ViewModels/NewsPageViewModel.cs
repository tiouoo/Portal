using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.ViewModels;

public partial class NewsPageViewModel : ObservableObject
{
    // 1. 声明全局唯一的单例实例
    public static NewsPageViewModel Instance { get; } = new();

    private List<NewsEntry> _javaNews = [];
    private List<NewsEntry> _bedrockNews = [];

    public ObservableCollection<NewsEntry> FilteredNews { get; } = [];

    public List<NewsFilterOption> FilterOptions { get; } =
    [
        new() { DisplayText = "全部", Type = NewsFilterType.All },
        new() { DisplayText = "Java 版", Type = NewsFilterType.Java },
        new() { DisplayText = "基岩版", Type = NewsFilterType.Bedrock }
    ];

    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial NewsFilterOption? SelectedFilter { get; set; }
    [ObservableProperty] public partial DateTime? SelectedStartDate { get; set; } = DateTime.Now.AddMonths(-3);

    // 2. 将构造函数私有化，确保只能通过 Instance 访问
    private NewsPageViewModel()
    {
        SelectedFilter = FilterOptions[0];
        NewsService.NewsUpdated += OnNewsUpdated;
        HandleNewsUpdate();
    }

    partial void OnSelectedFilterChanged(NewsFilterOption? value) => ApplyFilter();

    partial void OnSelectedStartDateChanged(DateTime? value) => ApplyFilter();

    private void OnNewsUpdated(object? sender, EventArgs e)
    {
        HandleNewsUpdate();
    }

    private void HandleNewsUpdate()
    {
        _javaNews = NewsService.JavaNews ?? [];
        _bedrockNews = NewsService.BedrockNews ?? [];

        IsVisible = _javaNews.Count > 0 || _bedrockNews.Count > 0;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        // 由于所有页面共用这个 Collection，这里的 Clear 和 AddRange 
        // 会让所有绑定了此数据的 UI 视图同步刷新，且计算只做一次
        FilteredNews.Clear();
        var filter = SelectedFilter?.Type ?? NewsFilterType.All;
        IEnumerable<NewsEntry> list = filter switch
        {
            NewsFilterType.Java => _javaNews,
            NewsFilterType.Bedrock => _bedrockNews,
            _ => _javaNews.Concat(_bedrockNews)
        };

        if (SelectedStartDate.HasValue)
        {
            var cutoffDate = SelectedStartDate.Value.Date;
            list = list.Where(x => x.Date.Date >= cutoffDate);
        }

        list = list.OrderByDescending(x => x.Date);
        FilteredNews.AddRange(list);
    }

    // 3. 移除了旧的 Dispose 方法，因为单例不需要在页面关闭时注销
}

public class NewsFilterOption
{
    public string DisplayText { get; set; } = string.Empty;
    public NewsFilterType Type { get; set; }
}

public enum NewsFilterType

{
    All,
    Java,
    Bedrock
}