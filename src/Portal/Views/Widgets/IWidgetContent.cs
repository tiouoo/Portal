using Avalonia.Controls;
using Portal.Classes.Entries;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

public abstract class IWidgetContent : UserControl
{
    public WidgetCellSize Size { get; protected set; } = new(1, 1);
    public WidgetKind Kind { get; internal set; }

    /// <summary>
    /// 在组件内容创建或尺寸切换后调用，传入当前的布局数据。
    /// 需要依赖布局数据（如实例、世界、服务器信息）的组件可重写此方法以刷新显示。
    /// </summary>
    public virtual void Initialize(WidgetLayoutData layout) { }

    /// <summary>
    /// 用户在组件卡片上点击（按下并松开且未触发拖动）时调用。
    /// 需要响应点击的组件可重写此方法（例如打开详情页）。
    /// </summary>
    public virtual void PerformClick() { }
}
