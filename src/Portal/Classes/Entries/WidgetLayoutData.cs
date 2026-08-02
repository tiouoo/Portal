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
