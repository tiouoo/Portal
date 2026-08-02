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

    /// <summary>实例小组件、快速进入世界/服务器组件共用的实例标识（InstanceFolderPath）。</summary>
    public string? InstanceFolderPath { get; set; }
    /// <summary>快速进入世界组件所记录的存档文件夹名。</summary>
    public string? WorldFolderName { get; set; }
    /// <summary>快速进入服务器组件所记录的服务器地址。</summary>
    public string? ServerAddress { get; set; }
    /// <summary>快速进入服务器组件所记录的服务器端口。</summary>
    public int? ServerPort { get; set; }

    /// <summary>内存资源组件的显示模式：true=百分比，false=数值。null 时默认百分比。</summary>
    public bool? MemoryShowPercentage { get; set; }

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
