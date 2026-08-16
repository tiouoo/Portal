using Portal.Core.App.Service.SystemResources;
using Portal.Core.Module.Widgets;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

public sealed class GpuResourceWidget : ResourceWidgetBase
{
    public GpuResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = "GPU";
        IconGeometry = "F1 M640,640z M0,0z M512,160L512,416 128,416 128,160 512,160z M128,96C92.7,96,64,124.7,64,160L64,416C64,451.3,92.7,480,128,480L272,480 256,528 184,528C170.7,528 160,538.7 160,552 160,565.3 170.7,576 184,576L456,576C469.3,576 480,565.3 480,552 480,538.7 469.3,528 456,528L384,528 368,480 512,480C547.3,480,576,451.3,576,416L576,160C576,124.7,547.3,96,512,96L128,96z";
    }

    public override ResourceKind ResourceKind => ResourceKind.Gpu;

    protected override void OnUpdate(ResourceSnapshot snapshot)
    {
        if (snapshot.GpuUsage is { } usage)
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

        SecondaryText = snapshot.GpuName ?? "不可用";
    }
}
