using Portal.Core.Module.Widgets;
using Portal.Core.Services.SystemResources;
using Portal.Localization;

using Portal.Module;
namespace Portal.Views.Widgets;

public sealed class GpuResourceWidget : ResourceWidgetBase
{
    public GpuResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = "GPU";
        IconGeometry = GeometryResources.Get("GpuGeometry");;
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

        SecondaryText = snapshot.GpuName ?? CommonLanguageManager.Instance.widgets_unavailable.CurrentValue();
    }
}