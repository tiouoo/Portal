using System.Text.Json.Serialization;
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
    public bool AlignToGrid { get; set; } = true;
    public double FreeX { get; set; }
    public double FreeY { get; set; }
    public double? FreeWidth { get; set; }
    public double? FreeHeight { get; set; }

    public WidgetData? Data { get; set; }

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

public class WidgetData
{
}

public class InstanceBoundWidgetData : WidgetData
{
    public string? InstanceFolderPath { get; set; }
    public WidgetClickAction? ClickAction { get; set; }
}

public enum WidgetClickAction
{
    None,
    ShowDetails,
    LaunchInstance,
    QuickEnterWorld,
    QuickEnterServer
}

public sealed class InstanceWidgetData : InstanceBoundWidgetData
{
}

public sealed class QuickWorldWidgetData : InstanceBoundWidgetData
{
    public string? WorldFolderName { get; set; }
}

public sealed class QuickServerWidgetData : InstanceBoundWidgetData
{
    public string? ServerAddress { get; set; }
    public int? ServerPort { get; set; }
}

public sealed class MemoryWidgetData : WidgetData
{
    public bool? ShowPercentage { get; set; }
}

public sealed class ImageWidgetData : WidgetData
{
    public string? ImagePath { get; set; }
    public bool? StretchFill { get; set; }
}

public sealed class NewsWidgetData : WidgetData
{
    public string? Filter { get; set; }
}

public sealed class LaunchButtonWidgetData : WidgetData
{
    public string? InstanceFolderPath { get; set; }
}

public sealed class GameListWidgetData : WidgetData
{
    public string? Title { get; set; }
    public List<GameListWidgetEntry> Items { get; set; } = [];
    public int Limit { get; set; } = 4;
}

public sealed class GameListWidgetEntry
{
    public HomePageItemKind? ItemKind { get; set; }
    public string InstanceFolderPath { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? DisplayName { get; set; }
    public string? ServerAddress { get; set; }
    public int? ServerPort { get; set; }
}

public enum HomePageItemKind
{
    Instance,
    World,
    Server
}
