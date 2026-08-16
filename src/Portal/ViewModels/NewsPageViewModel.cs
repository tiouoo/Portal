using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.Extensions;

namespace Portal.ViewModels;

public partial class NewsPageViewModel : ObservableObject
{
    
    public static NewsPageViewModel Instance { get; } = new();

    private List<NewsEntry> _javaNews = [];
    private List<NewsEntry> _bedrockNews = [];
    private int _newsRefreshVersion;

    public ObservableCollection<NewsEntry> FilteredNews { get; } = [];

    public List<NewsFilterOption> FilterOptions { get; } =
    [
        new() { DisplayText = "全部", Type = NewsFilterType.All },
        new() { DisplayText = "Java 版", Type = NewsFilterType.Java },
        new() { DisplayText = "基岩版", Type = NewsFilterType.Bedrock }
    ];

    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial NewsFilterOption? SelectedFilter { get; set; }
    [ObservableProperty] public partial DateTime? SelectedStartDate { get; set; } = DateTime.Now.AddMonths(-1);

    
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
        
        Dispatcher.UIThread.Post(HandleNewsUpdate);
    }

    private void HandleNewsUpdate()
    {
        _javaNews = NewsService.JavaNews ?? [];
        _bedrockNews = NewsService.BedrockNews ?? [];

        IsVisible = _javaNews.Count > 0 || _bedrockNews.Count > 0;

        ApplyFilter();
        ScheduleImageRefresh();
    }

    private void ScheduleImageRefresh()
    {
        var refreshVersion = ++_newsRefreshVersion;
        _ = RefreshImagesAfterInitialLoadAsync(refreshVersion);
    }

    private async Task RefreshImagesAfterInitialLoadAsync(int refreshVersion)
    {
        
        
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        Dispatcher.UIThread.Post(() =>
        {
            if (refreshVersion == _newsRefreshVersion)
                ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        
        
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
