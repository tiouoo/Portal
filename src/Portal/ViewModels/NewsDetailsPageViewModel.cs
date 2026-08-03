using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Module.News;

namespace Portal.ViewModels;

public partial class NewsDetailsPageViewModel : ObservableObject
{
    private readonly NewsEntry _entry;
    private bool _loaded;
    private bool _disposed;

    public NewsDetailsPageViewModel(NewsEntry entry)
    {
        _entry = entry;
        Title = entry.Title;
        Version = entry.Version;
        Type = entry.Type;
        ImageUrl = entry.ImageUrl;
        Date = entry.Date;
        Edition = entry.Edition;
        RelativeDate = entry.RelativeDate;
    }

    [ObservableProperty]
    public partial ObservableCollection<Control> BodyControls { get; set; } = [];

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string Version { get; set; }
    [ObservableProperty] public partial string Type { get; set; }
    [ObservableProperty] public partial string ImageUrl { get; set; }
    [ObservableProperty] public partial DateTime Date { get; set; }
    [ObservableProperty] public partial string RelativeDate { get; set; }
    [ObservableProperty] public partial NewsEdition Edition { get; set; }

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string ErrorText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsCached { get; set; }
    [ObservableProperty] public partial DateTime? FetchedAt { get; set; }

    public string EditionDisplay => Edition == NewsEdition.Java ? "Java" : "基岩";
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public bool HasType => !string.IsNullOrWhiteSpace(Type);

    public async Task LoadAsync()
    {
        if (_loaded || _disposed) return;
        _loaded = true;
        IsLoading = true;
        HasError = false;
        try
        {
            var detail = await NewsDetailsService.GetAsync(_entry);
            if (_disposed) return;
            if (detail == null)
            {
                HasError = true;
                ErrorText = "无法加载新闻正文，请检查网络连接后重试。";
                return;
            }

            // 用详情接口返回的字段更新一遍（防止列表缓存里的字段过期）。
            Title = detail.Title;
            Version = detail.Version;
            Type = detail.Type;
            if (!string.IsNullOrEmpty(detail.ImageUrl)) ImageUrl = detail.ImageUrl;
            Date = detail.Date;
            FetchedAt = detail.FetchedAt;
            IsCached = true;

            await RenderBodyAsync(detail.Body);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disposed) return;
        IsLoading = true;
        HasError = false;
        try
        {
            var detail = await NewsDetailsService.RefreshAsync(_entry);
            if (_disposed) return;
            if (detail == null)
            {
                HasError = true;
                ErrorText = "刷新失败，请稍后重试。";
                return;
            }
            Title = detail.Title;
            Version = detail.Version;
            Type = detail.Type;
            if (!string.IsNullOrEmpty(detail.ImageUrl)) ImageUrl = detail.ImageUrl;
            Date = detail.Date;
            FetchedAt = detail.FetchedAt;
            IsCached = true;
            await RenderBodyAsync(detail.Body);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = $"刷新失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RenderBodyAsync(string html)
    {
        if (_disposed) return;
        var doc = await Task.Run(() => NewsHtmlRenderer.Parse(html));
        if (_disposed) return;

        // 先挂一个空集合，让 ItemsRepeater 立即绑定到新集合，后续分批 Add 触发增量渲染。
        BodyControls = new ObservableCollection<Control>();

        // 分批创建并添加控件，每批之间让出 UI 线程，避免一次性创建大量控件导致界面卡顿。
        // 配合 ItemsRepeater 虚拟化，未进入视口的块不会触发 measure/arrange。
        const int batchSize = 8;
        var batch = new List<Control>(batchSize);
        foreach (var control in NewsHtmlRenderer.RenderEnumerable(doc))
        {
            if (_disposed) return;
            batch.Add(control);
            if (batch.Count >= batchSize)
            {
                foreach (var c in batch) BodyControls.Add(c);
                batch.Clear();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }
        if (_disposed) return;
        foreach (var c in batch) BodyControls.Add(c);
    }

    public void Dispose()
    {
        _disposed = true;
        BodyControls.Clear();
    }
}
