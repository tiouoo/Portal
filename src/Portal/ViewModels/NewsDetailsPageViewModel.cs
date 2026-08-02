using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Module.News;

namespace Portal.ViewModels;

/// <summary>新闻详情页视图模型。每个详情标签页独立持有，不复用单例。</summary>
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

    public ObservableCollection<Control> BodyControls { get; } = [];

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

            RenderBody(detail.Body);
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
            RenderBody(detail.Body);
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

    private void RenderBody(string html)
    {
        // 在 UI 线程上构建控件树（Avalonia 控件必须依附 UI 线程）。
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            BodyControls.Clear();
            var controls = NewsHtmlRenderer.Render(html);
            foreach (var c in controls) BodyControls.Add(c);
        });
    }

    public void Dispose()
    {
        _disposed = true;
        BodyControls.Clear();
    }
}
