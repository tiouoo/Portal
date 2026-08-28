using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Portal.Core.Classes.Entries;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Widgets;
using Portal.Localization;
using Portal.Views.Pages;

namespace Portal.Views.Widgets;

public partial class LaunchButtonWidget : IWidgetContent, IWidgetPersistenceAware
{
    private readonly ObservableCollection<LaunchInstanceChoice> _instanceChoices = [];
    private WidgetLayoutData? _layout;
    private MinecraftInstance? _instance;
    private Action? _saveLayout;

    public LaunchButtonWidget()
    {
        Size = new WidgetCellSize(2, 1);
        InitializeComponent();
        InstanceItems.ItemsSource = _instanceChoices;
        if (LaunchButton.Parent is InputElement buttonArea)
            buttonArea.AddHandler(InputElement.PointerWheelChangedEvent, OnButtonAreaPointerWheelChanged,
                RoutingStrategies.Bubble, true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public override void Initialize(WidgetLayoutData layout)
    {
        _layout = layout;
        if (layout.Data is not LaunchButtonWidgetData)
            layout.Data = new LaunchButtonWidgetData();
        ResolveInstance();
        RefreshDisplay();
    }

    public void SetSaveLayoutAction(Action saveLayout)
    {
        _saveLayout = saveLayout;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        ResolveInstance();
        RefreshDisplay();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged -= OnInstancesChanged;
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        ResolveInstance();
        RefreshDisplay();
        RebuildInstanceChoices();
    }

    private void ResolveInstance()
    {
        var path = (_layout?.Data as LaunchButtonWidgetData)?.InstanceFolderPath;
        _instance = string.IsNullOrWhiteSpace(path)
            ? null
            : InstanceManager.Instance.Instances.FirstOrDefault(instance =>
                SamePath(instance.InstanceFolderPath, path));
    }

    private void RefreshDisplay()
    {
        LaunchText.Text = _instance == null
            ? WidgetsLanguageManager.Instance.launchbuttonwidget_selectInstance.CurrentValue()
            : WidgetsLanguageManager.Instance.launchbuttonwidget_launch.CurrentValue();
        InstanceText.Text = _instance?.InstanceName ??
                            WidgetsLanguageManager.Instance.launchbuttonwidget_notPinned.CurrentValue();
        ToolTip.SetTip(LaunchButton, _instance == null
            ? WidgetsLanguageManager.Instance.launchbuttonwidget_selectInstance.CurrentValue()
            : WidgetsLanguageManager.Instance.launchbuttonwidget_launch.CurrentValue());
        ToolTip.SetTip(MenuButton,
            WidgetsLanguageManager.Instance.launchbuttonwidget_selectInstance.CurrentValue());
    }

    private void LaunchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_instance == null)
        {
            MenuButton.Flyout?.ShowAt(MenuButton);
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        _ = MinecraftLaunchService.LaunchAsync(_instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(_instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)));
    }

    private void InstanceFlyout_OnOpening(object? sender, EventArgs e)
    {
        RebuildInstanceChoices();
    }

    private void RebuildInstanceChoices()
    {
        _instanceChoices.Clear();
        foreach (var instance in GetOrderedInstances())
        {
            _instanceChoices.Add(new LaunchInstanceChoice(instance,
                _instance != null && SamePath(_instance.InstanceFolderPath, instance.InstanceFolderPath)));
        }

        var hasInstances = _instanceChoices.Count > 0;
        InstanceScrollViewer.IsVisible = hasInstances;
        EmptyState.IsVisible = !hasInstances;
    }

    private void OnButtonAreaPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;

        var instances = GetOrderedInstances();
        if (instances.Count == 0)
            return;

        var currentIndex = _instance == null
            ? -1
            : instances.FindIndex(instance => SamePath(instance.InstanceFolderPath, _instance.InstanceFolderPath));
        var step = e.Delta.Y > 0 ? -1 : 1;
        var nextIndex = currentIndex < 0
            ? step > 0 ? 0 : instances.Count - 1
            : (currentIndex + step + instances.Count) % instances.Count;

        if (currentIndex != nextIndex)
        {
            PinInstance(instances[nextIndex], false);
            RebuildInstanceChoices();
        }

        e.Handled = true;
    }

    private void InstanceChoice_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LaunchInstanceChoice choice })
            PinInstance(choice.Instance);
    }

    private void PinInstance(MinecraftInstance? instance, bool hideFlyout = true)
    {
        if (_layout?.Data is not LaunchButtonWidgetData data)
            return;

        data.InstanceFolderPath = instance?.InstanceFolderPath;
        _instance = instance;
        RefreshDisplay();
        _saveLayout?.Invoke();
        if (hideFlyout)
            MenuButton.Flyout?.Hide();
    }

    private static List<MinecraftInstance> GetOrderedInstances() =>
        InstanceManager.Instance.Instances
            .OrderBy(instance => instance.InstanceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public sealed class LaunchInstanceChoice(MinecraftInstance instance, bool isSelected)
{
    public MinecraftInstance Instance { get; } = instance;
    public bool IsSelected { get; } = isSelected;
    public string FolderText => string.Format(
        WidgetsLanguageManager.Instance.launchbuttonwidget_fromFolder.CurrentValue(), Instance.FolderName);
}
