using Avalonia.Controls;
using Avalonia.Threading;
using Portal.Classes.Entries;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

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
        var path = (LayoutData?.Data as InstanceBoundWidgetData)?.InstanceFolderPath;
        Instance = path != null
            ? InstanceManager.Instance.Instances.FirstOrDefault(i => i.InstanceFolderPath == path)
            : null;
    }

        protected T? GetData<T>() where T : class => LayoutData?.Data as T;

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

        protected virtual void OnInstanceResolved() { }

        protected virtual void OnInstanceIconRefreshed() => OnInstanceResolved();
}
