using Newtonsoft.Json;
using Portal.Core.Module.Widgets;

namespace Portal.Core.Classes.Entries;

public sealed class WidgetLayoutData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WidgetKind Kind { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public bool? ShowBackground { get; set; }

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

public class InstanceBoundWidgetData
{
    public string? InstanceFolderPath { get; set; }
}

public sealed class InstanceWidgetData : InstanceBoundWidgetData { }

public sealed class QuickWorldWidgetData : InstanceBoundWidgetData
{
        public string? WorldFolderName { get; set; }
}

public sealed class QuickServerWidgetData : InstanceBoundWidgetData
{
        public string? ServerAddress { get; set; }
        public int? ServerPort { get; set; }
}

public sealed class MemoryWidgetData
{
        public bool? ShowPercentage { get; set; }
}

public sealed class ImageWidgetData
{
        public string? ImagePath { get; set; }
        public bool? StretchFill { get; set; }
}

public sealed class NewsWidgetData
{
        public string? Filter { get; set; }
}
