using Portal.Core.Module.Widgets;

namespace Portal.Views.Widgets;

public partial class Clock1x1 : ClockWidgetBase
{
    public Clock1x1()
    {
        Size = new WidgetCellSize(1, 1);
        InitializeComponent();
        InitializeClock();
    }
}