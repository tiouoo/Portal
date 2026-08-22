using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Portal.Core.Services;
using Portal.Localization;
using Portal.Module;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.InstancePages;

public partial class AiAnalysisPage : UserControl, ITioTabPage, INotifyPropertyChanged
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> AnalysisCache = new();

    private readonly string _content;
    private readonly string _displayName;
    private string _resultText = string.Empty;
    private string? _errorMessage;
    private bool _isAnalyzing;
    private bool _isComplete;
    private bool _isFailed;

    public AiAnalysisPage() : this(string.Empty, string.Empty)
    {
    }

    public AiAnalysisPage(string content, string displayName)
    {
        _content = content;
        _displayName = displayName;
        PageInfo = new PageInfo
        {
            Title = string.Format(CommonLanguageManager.Instance.aiAnalysis_pageTitle.CurrentValue(), displayName),
            IconGlyph = "\ue62b", IconFont = IconResources.FontFamilyName
        };
        InitializeComponent();
        DataContext = this;
        _ = AnalyzeAsync();
    }

    public string ResultText
    {
        get => _resultText;
        private set
        {
            if (_resultText == value) return;
            _resultText = value;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (_isAnalyzing == value) return;
            _isAnalyzing = value;
            OnPropertyChanged();
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set
        {
            if (_isComplete == value) return;
            _isComplete = value;
            OnPropertyChanged();
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        private set
        {
            if (_isFailed == value) return;
            _isFailed = value;
            OnPropertyChanged();
        }
    }

    public PageInfo PageInfo { get; init; }
    public TabEntry HostTab { get; set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnClose()
    {
        DataContext = null;
    }

    public static void Open(string content, string displayName, TopLevel sender)
    {
        if (sender is not TioTabWindowBase window)
            return;
        var tab = new TabEntry(window, new AiAnalysisPage(content, displayName));
        window.CreateTab(tab);
        window.SelectTab(tab);
    }

    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(_content))
            return;

        IsAnalyzing = true;
        IsComplete = false;
        IsFailed = false;

        try
        {
            var task = AnalysisCache.GetOrAdd(ComputeContentKey(_content),
                _ => new Lazy<Task<string>>(() => LogSharingService.AnalyseAiAsync(_content, null, CancellationToken.None)))
                .Value;
            var result = await task;
            ResultText = result;
            IsComplete = true;
        }
        catch (Exception ex)
        {
            AnalysisCache.TryRemove(ComputeContentKey(_content), out _);
            ErrorMessage = ex.Message;
            IsFailed = true;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static string ComputeContentKey(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        await topLevel.Clipboard!.SetTextAsync(ResultText);
        topLevel.Notice(CommonLanguageManager.Instance.aiAnalysis_copied.CurrentValue(), NotificationType.Success);
    }

    private void Retry_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = AnalyzeAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
