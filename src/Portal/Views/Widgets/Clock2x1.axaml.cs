using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

public partial class Clock2x1 : ClockWidgetBase
{
    public Clock2x1()
    {
        Size = new WidgetCellSize(2, 2);
        InitializeComponent();
        InitializeClock();
    }
}
