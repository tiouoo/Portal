using Portal.Core.Module.Widgets;
using Portal.Core.Services.SystemResources;

using Portal.Module;
namespace Portal.Views.Widgets;

public sealed class CpuResourceWidget : ResourceWidgetBase
{
    public CpuResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = "CPU";
        IconGeometry = GeometryResources.Get("CpuGeometry");;
        HasSecondaryText = false;
    }

    public override ResourceKind ResourceKind => ResourceKind.Cpu;

    protected override void OnUpdate(ResourceSnapshot snapshot)
    {
        if (snapshot.CpuUsage is { } usage)
        {
            PrimaryText = $"{usage:F1}%";
            Percentage = usage;
            ProgressValue = usage;
        }
        else
        {
            PrimaryText = "N/A";
            Percentage = 0;
            ProgressValue = 0;
        }
    }
}