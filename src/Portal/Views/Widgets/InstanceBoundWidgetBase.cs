using Avalonia.Controls;
using Avalonia.Threading;
using Portal.Classes.Entries;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

/// <summary>
/// 依赖实例的小组件基类。负责根据 WidgetLayoutData 解析当前实例、
/// 监听实例列表与图标变更，并在实例失效时提供回退显示。
/// </summary>
public abstract class InstanceBoundWidgetBase : IWidgetContent
{
    protected WidgetLayoutData? LayoutData;
    protected MinecraftInstance? Instance;

    public override void Initialize(WidgetLayoutData layout)
    {
        LayoutData = layout;
        ResolveInstance();
        OnInstanceResolved();

        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        InstanceManager.Instance.InstanceIconChanged += OnInstanceIconChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        InstanceManager.Instance.InstancesChanged -= OnInstancesChanged;
        InstanceManager.Instance.InstanceIconChanged -= OnInstanceIconChanged;
        Unloaded -= OnUnloaded;
    }

    private void ResolveInstance()
    {
        var path = LayoutData?.InstanceFolderPath;
        Instance = path != null
            ? InstanceManager.Instance.Instances.FirstOrDefault(i => i.InstanceFolderPath == path)
            : null;
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        var previous = Instance;
        ResolveInstance();
        if (previous != Instance)
            Dispatcher.UIThread.Post(OnInstanceResolved);
    }

    private void OnInstanceIconChanged(object? sender, MinecraftInstance instance)
    {
        if (Instance != null && instance.InstanceFolderPath == Instance.InstanceFolderPath)
            Dispatcher.UIThread.Post(OnInstanceIconRefreshed);
    }

    /// <summary>实例解析完成后调用（首次或实例列表变更后），子类可重写以刷新显示。</summary>
    protected virtual void OnInstanceResolved() { }

    /// <summary>当前实例的图标被更换后调用，子类可重写以刷新图标。</summary>
    protected virtual void OnInstanceIconRefreshed() => OnInstanceResolved();
}
