namespace Portal.Module.Widgets;

public enum WidgetKind
{
    Clock,
    Instance,
    QuickWorld,
    QuickServer,
    CpuResource,
    MemoryResource,
    DiskResource,
    NetworkResource,
    GpuResource,
    Image
}

/// <summary>小组件分类，用于添加组件对话框的左侧导航。</summary>
public enum WidgetCategory
{
    /// <summary>游戏相关：实例、快速进入世界/服务器。</summary>
    Game,
    /// <summary>系统资源：CPU、内存、磁盘、网络、GPU。</summary>
    Resource,
    /// <summary>实用工具：时钟、图片等。</summary>
    Utility
}

public readonly record struct WidgetCellSize(int Columns, int Rows)
{
    public static WidgetCellSize Parse(string value)
    {
        var s = value.Trim().Replace('x', '×').Replace('X', '×').Replace('*', '×');
        var parts = s.Split('×');
        if (parts.Length == 2 &&
            int.TryParse(parts[0].Trim(), out var columns) &&
            int.TryParse(parts[1].Trim(), out var rows))
            return new WidgetCellSize(Math.Max(1, columns), Math.Max(1, rows));
        return new WidgetCellSize(1, 1);
    }

    public override string ToString() => $"{Columns}×{Rows}";
}
