using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Module.AggregatedSearch;
using Portal.Localization;
using Portal.Module;
using Portal.Module.DefaultPage;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_home", "pages_homePath", "Home")]
[DefaultPage("pages_home")]
public partial class HomePage : UserControl, ITioTabPage
{
    private readonly HomePageViewModel _viewModel = new();

    public HomePage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.homePage_pageTitle.CurrentValue(),
        IconGlyph = "\ue619",
        IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        _viewModel.Dispose();
        DataContext = null;
    }
}

public partial class HomePageViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _greetingTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private MinecraftAccount? _javaAccount;
    private BedrockAccount? _bedrockAccount;
    private bool _isDisposed;

    public HomePageViewModel()
    {
        Data.ConfigEntry.PropertyChanged += ConfigEntry_OnPropertyChanged;
        _greetingTimer.Tick += GreetingTimer_OnTick;
        _greetingTimer.Start();
        RefreshAccountSubscriptions();
        RefreshGreeting();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGreeting))]
    public partial string? Greeting { get; private set; }

    public bool HasGreeting => !string.IsNullOrWhiteSpace(Greeting);

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _greetingTimer.Stop();
        _greetingTimer.Tick -= GreetingTimer_OnTick;
        Data.ConfigEntry.PropertyChanged -= ConfigEntry_OnPropertyChanged;
        SetAccountSubscriptions(null, null);
    }

    private void ConfigEntry_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Data.ConfigEntry.UsingMinecraftMinecraftAccount) or
            nameof(Data.ConfigEntry.UsingBedrockAccount)))
            return;

        RefreshAccountSubscriptions();
        RefreshGreeting();
    }

    private void Account_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MinecraftAccount.Name) or nameof(BedrockAccount.Gamertag))
            RefreshGreeting();
    }

    private void GreetingTimer_OnTick(object? sender, EventArgs e)
    {
        RefreshGreeting();
    }

    private void RefreshAccountSubscriptions()
    {
        SetAccountSubscriptions(Data.ConfigEntry.UsingMinecraftMinecraftAccount,
            Data.ConfigEntry.UsingBedrockAccount);
    }

    private void SetAccountSubscriptions(MinecraftAccount? javaAccount, BedrockAccount? bedrockAccount)
    {
        if (_javaAccount != null)
            _javaAccount.PropertyChanged -= Account_OnPropertyChanged;
        if (_bedrockAccount != null)
            _bedrockAccount.PropertyChanged -= Account_OnPropertyChanged;

        _javaAccount = javaAccount;
        _bedrockAccount = bedrockAccount;

        if (_javaAccount != null)
            _javaAccount.PropertyChanged += Account_OnPropertyChanged;
        if (_bedrockAccount != null)
            _bedrockAccount.PropertyChanged += Account_OnPropertyChanged;
    }

    private void RefreshGreeting()
    {
        var accountName = !string.IsNullOrWhiteSpace(_javaAccount?.Name)
            ? _javaAccount.Name
            : _bedrockAccount?.Gamertag;
        if (string.IsNullOrWhiteSpace(accountName))
        {
            Greeting = null;
            return;
        }

        var greeting = DateTime.Now.Hour switch
        {
            < 3 => CommonLanguageManager.Instance.homePage_greetingAfterMidnight.CurrentValue(),
            < 6 => CommonLanguageManager.Instance.homePage_greetingBeforeDawn.CurrentValue(),
            < 9 => CommonLanguageManager.Instance.homePage_greetingEarlyMorning.CurrentValue(),
            < 12 => CommonLanguageManager.Instance.homePage_greetingMorning.CurrentValue(),
            < 14 => CommonLanguageManager.Instance.homePage_greetingNoon.CurrentValue(),
            < 17 => CommonLanguageManager.Instance.homePage_greetingAfternoon.CurrentValue(),
            < 20 => CommonLanguageManager.Instance.homePage_greetingEvening.CurrentValue(),
            < 23 => CommonLanguageManager.Instance.homePage_greetingNight.CurrentValue(),
            _ => CommonLanguageManager.Instance.homePage_greetingLateNight.CurrentValue()
        };
        Greeting = string.Format(greeting, accountName);
    }
}
