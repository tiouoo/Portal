using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Widgets;
using Portal.Views.Pages;
using Portal.Views.Pages.InstancePages;
using TioUi.Common.Extensions;

namespace Portal.Views.Widgets;

public partial class ContinueInstanceWidget : IWidgetContent, INotifyPropertyChanged
{
    private MinecraftInstance? _recentInstance;

    public ContinueInstanceWidget()
    {
        Size = new WidgetCellSize(2, 1);
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public MinecraftInstance? RecentInstance
    {
        get => _recentInstance;
        private set
        {
            if (ReferenceEquals(_recentInstance, value)) return;
            _recentInstance = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecentInstance)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecentInstance)));
        }
    }

    public bool HasRecentInstance => RecentInstance != null;
    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnLoaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged += OnSourceChanged;
        InstanceManager.Instance.StatisticsChanged += OnSourceChanged;
        InstanceManager.Instance.InstanceIconChanged += OnIconChanged;
        Refresh();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged -= OnSourceChanged;
        InstanceManager.Instance.StatisticsChanged -= OnSourceChanged;
        InstanceManager.Instance.InstanceIconChanged -= OnIconChanged;
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Refresh();
    private void OnIconChanged(object? sender, MinecraftInstance e) => PropertyChanged?.Invoke(this,
        new PropertyChangedEventArgs(nameof(RecentInstance)));

    private void Refresh()
    {
        RecentInstance = InstanceManager.Instance.Instances
            .Where(instance => instance.LastPlayTime != DateTime.MinValue)
            .OrderByDescending(instance => instance.LastPlayTime)
            .FirstOrDefault();
    }

    public override void PerformClick()
    {
        if (RecentInstance != null && TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(RecentInstance, topLevel);
    }

    private void ContinueGame_Click(object? sender, RoutedEventArgs e)
    {
        if (RecentInstance == null) return;
        _ = MinecraftLaunchService.LaunchAsync(RecentInstance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(RecentInstance,
                logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }
}
