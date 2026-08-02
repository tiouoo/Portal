using Avalonia.Controls;
using Portal.Classes.Entries;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

public abstract class IWidgetContent : UserControl
{
    public WidgetCellSize Size { get; protected set; } = new(1, 1);
    public WidgetKind Kind { get; internal set; }
}
