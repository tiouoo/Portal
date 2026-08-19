using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.News;
using Portal.Localization;

namespace Portal.ViewModels;

public partial class NewsDetailsPageViewModel : ObservableObject
{
    private readonly NewsEntry _entry;
    private bool _disposed;
    private bool _loaded;

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
        NewsDetailsService.NewsDetailUpdated += OnNewsDetailUpdated;
    }

    [ObservableProperty] public partial ObservableCollection<Control> BodyControls { get; set; } = [];

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

    public string EditionDisplay =>
        Edition == NewsEdition.Bedrock
            ? CommonLanguageManager.Instance.news_editionBedrock.CurrentValue()
            : "Java";
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
                ErrorText = CommonLanguageManager.Instance.news_loadFailed.CurrentValue();
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
            ErrorText = string.Format(CommonLanguageManager.Instance.news_loadFailedWithMessage.CurrentValue(), ex.Message);
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
                ErrorText = CommonLanguageManager.Instance.news_refreshFailed.CurrentValue();
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
            ErrorText = string.Format(CommonLanguageManager.Instance.news_refreshFailedWithMessage.CurrentValue(), ex.Message);
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


        BodyControls = new ObservableCollection<Control>();


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
        NewsDetailsService.NewsDetailUpdated -= OnNewsDetailUpdated;
        BodyControls.Clear();
    }

    private void OnNewsDetailUpdated(NewsDetail detail)
    {
        if (_disposed || detail.Id != _entry.Id) return;
        Dispatcher.UIThread.Post(async () =>
        {
            if (_disposed) return;
            Title = detail.Title;
            Version = detail.Version;
            Type = detail.Type;
            if (!string.IsNullOrEmpty(detail.ImageUrl)) ImageUrl = detail.ImageUrl;
            Date = detail.Date;
            FetchedAt = detail.FetchedAt;
            IsCached = true;
            await RenderBodyAsync(detail.Body);
        });
    }
}