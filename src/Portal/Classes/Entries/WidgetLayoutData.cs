using Newtonsoft.Json;
using Portal.Module.Widgets;

namespace Portal.Classes.Entries;

public sealed class WidgetLayoutData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WidgetKind Kind { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public bool? ShowBackground { get; set; }

    /// <summary>
    /// 组件自定义数据。具体类型由组件决定，使用时按需转换为对应类型
    /// （如 <see cref="InstanceWidgetData"/>、<see cref="QuickWorldWidgetData"/>、
    /// <see cref="QuickServerWidgetData"/>、<see cref="MemoryWidgetData"/>）。
    /// 序列化时通过 TypeNameHandling.Auto 保留运行时类型信息。
    /// </summary>
    public object? Data { get; set; }

    [JsonIgnore]
    public WidgetCellSize Size
    {
        get => new(Columns, Rows);
        set
        {
            Columns = value.Columns;
            Rows = value.Rows;
        }
    }
}

/// <summary>依赖实例的小组件共用数据基类，记录所属实例路径。</summary>
public class InstanceBoundWidgetData
{
    public string? InstanceFolderPath { get; set; }
}

/// <summary>实例小组件数据。</summary>
public sealed class InstanceWidgetData : InstanceBoundWidgetData { }

/// <summary>快速进入世界小组件数据。</summary>
public sealed class QuickWorldWidgetData : InstanceBoundWidgetData
{
    /// <summary>存档文件夹名。</summary>
    public string? WorldFolderName { get; set; }
}

/// <summary>快速进入服务器小组件数据。</summary>
public sealed class QuickServerWidgetData : InstanceBoundWidgetData
{
    /// <summary>服务器地址。</summary>
    public string? ServerAddress { get; set; }
    /// <summary>服务器端口。</summary>
    public int? ServerPort { get; set; }
}

/// <summary>内存资源小组件数据。</summary>
public sealed class MemoryWidgetData
{
    /// <summary>显示模式：true=百分比，false=数值。null 时默认百分比。</summary>
    public bool? ShowPercentage { get; set; }
}

/// <summary>图片小组件数据。</summary>
public sealed class ImageWidgetData
{
    /// <summary>本地图片绝对路径。</summary>
    public string? ImagePath { get; set; }
    /// <summary>图片填充方式：true=裁剪填充（UniformToFill），false=完整显示（Uniform）。null 时默认裁剪填充。</summary>
    public bool? StretchFill { get; set; }
}
