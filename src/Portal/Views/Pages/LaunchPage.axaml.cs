using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.AggregatedSearch;
using Portal.Localization;
using Portal.Module;
using Portal.Module.DefaultPage;
using Portal.Views.Components.Operations.Account;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[AggregatedSearchPage("pages_launch", "pages_launchPath", "Launch")]
[DefaultPage("pages_launch")]
public partial class LaunchPage : UserControl, ITioTabPage
{
    private readonly ContextMenu _accountContextMenu = new()
    {
        Cursor = new Cursor(StandardCursorType.Arrow)
    };
    private readonly LaunchPageViewModel _viewModel;

    public LaunchPage()
    {
        InitializeComponent();
        _viewModel = new LaunchPageViewModel();
        DataContext = _viewModel;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = CommonLanguageManager.Instance.launchPage_pageTitle.CurrentValue(),
        IconGlyph = "\ue613",
        IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        _viewModel.Dispose();
        DataContext = null;
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var instance = _viewModel.SelectedInstance;
        if (instance == null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)));
    }

    private void InstanceSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedInstance != null && TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(_viewModel.SelectedInstance, topLevel);
    }

    private void SelectInstance_OnClick(object? sender, RoutedEventArgs e) =>
        SelectInstanceButton.Flyout?.ShowAt(SelectInstanceButton);

    private void InstanceFlyout_OnOpening(object? sender, EventArgs e) => _viewModel.RebuildInstances();

    private void InstanceChoice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: LaunchPageInstanceChoice choice })
            return;

        _viewModel.SelectInstance(choice.Instance);
        SelectInstanceButton.Flyout?.Hide();
    }

    private void AccountArea_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        _accountContextMenu.Close();
        _accountContextMenu.Items.Clear();

        var switchAccount = new MenuItem
        {
            Header = CommonLanguageManager.Instance.launchPage_switchAccount.CurrentValue(),
            Icon = IconResources.CreateIcon("\ue621", 16),
            Cursor = new Cursor(StandardCursorType.Arrow)
        };
        foreach (var account in _viewModel.AvailableAccounts)
        {
            var candidate = account;
            var item = new MenuItem
            {
                Header = $"{candidate.DisplayAccountNote}·{candidate.Name}",
                IsChecked = candidate.Equals(_viewModel.CurrentAccount),
                Cursor = new Cursor(StandardCursorType.Arrow),
                Classes = { "hide-icon" }
            };
            item.Click += (_, _) => _viewModel.SelectAccount(candidate);
            switchAccount.Items.Add(item);
        }

        switchAccount.IsEnabled = switchAccount.Items.Count > 0;
        _accountContextMenu.Items.Add(switchAccount);

        if (_viewModel.CurrentAccount is { } currentAccount)
        {
            _accountContextMenu.Items.Add(new Separator { Cursor = new Cursor(StandardCursorType.Arrow) });
            foreach (var item in MinecraftAccountMenu.CreateOperationItems(this, currentAccount,
                         _viewModel.RefreshAccount))
                _accountContextMenu.Items.Add(item);
        }

        if (sender is Control control)
            _accountContextMenu.Open(control);
    }
}

public partial class LaunchPageViewModel : ObservableObject, IDisposable
{
    private bool _isDisposed;

    public LaunchPageViewModel()
    {
        RefreshAccount();
        RebuildInstances();
        InstanceManager.Instance.InstancesChanged += InstancesChanged;
        Data.ConfigEntry.MinecraftAccounts.CollectionChanged += AccountsChanged;
        Data.ConfigEntry.PropertyChanged += ConfigChanged;
    }

    public ObservableCollection<LaunchPageInstanceChoice> InstanceChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentAccount))]
    [NotifyPropertyChangedFor(nameof(CanLaunch))]
    public partial MinecraftAccount? CurrentAccount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedInstance))]
    [NotifyPropertyChangedFor(nameof(SelectedInstanceName))]
    [NotifyPropertyChangedFor(nameof(CanLaunch))]
    public partial MinecraftInstance? SelectedInstance { get; private set; }

    [ObservableProperty] public partial bool HasInstances { get; private set; }

    public bool HasCurrentAccount => CurrentAccount != null;
    public bool HasSelectedInstance => SelectedInstance != null;
    public bool CanLaunch => SelectedInstance != null && CurrentAccount != null;
    public string SelectedInstanceName => SelectedInstance?.InstanceName ??
                                          CommonLanguageManager.Instance.launchPage_noInstanceSelected.CurrentValue();
    public IEnumerable<MinecraftAccount> AvailableAccounts => Data.ConfigEntry.MinecraftAccounts
        .OrderBy(account => account.Name, StringComparer.CurrentCultureIgnoreCase);

    public void SelectAccount(MinecraftAccount account)
    {
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
        RefreshAccount();
    }

    public void SelectInstance(MinecraftInstance instance)
    {
        Data.ConfigEntry.LaunchPageInstanceFolderPath = instance.InstanceFolderPath;
        SelectedInstance = instance;
        RebuildInstances();
    }

    public void RebuildInstances()
    {
        var instances = InstanceManager.Instance.Instances
            .Where(instance => instance.IsJava)
            .OrderBy(instance => instance.InstanceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var selectedPath = Data.ConfigEntry.LaunchPageInstanceFolderPath;
        var resolved = instances.FirstOrDefault(instance => SamePath(instance.InstanceFolderPath, selectedPath));
        if (resolved == null && SelectedInstance != null)
            resolved = instances.FirstOrDefault(instance => SamePath(instance.InstanceFolderPath,
                SelectedInstance.InstanceFolderPath));

        SelectedInstance = resolved;
        InstanceChoices.Clear();
        foreach (var instance in instances)
            InstanceChoices.Add(new LaunchPageInstanceChoice(instance,
                SelectedInstance != null && SamePath(instance.InstanceFolderPath,
                    SelectedInstance.InstanceFolderPath)));
        HasInstances = InstanceChoices.Count > 0;
    }

    public void RefreshAccount()
    {
        CurrentAccount = Data.ConfigEntry.UsingMinecraftMinecraftAccount;
        OnPropertyChanged(nameof(AvailableAccounts));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        InstanceManager.Instance.InstancesChanged -= InstancesChanged;
        Data.ConfigEntry.MinecraftAccounts.CollectionChanged -= AccountsChanged;
        Data.ConfigEntry.PropertyChanged -= ConfigChanged;
    }

    private void InstancesChanged(object? sender, EventArgs e) => RebuildInstances();

    private void AccountsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshAccount();

    private void ConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Data.ConfigEntry.UsingMinecraftMinecraftAccount))
            RefreshAccount();
    }

    private static bool SamePath(string left, string? right) =>
        !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public sealed class LaunchPageInstanceChoice(MinecraftInstance instance, bool isSelected)
{
    public MinecraftInstance Instance { get; } = instance;
    public bool IsSelected { get; } = isSelected;
}
