using Portal.Core.Module.Widgets;
using Portal.Core.Services.SystemResources;
using Portal.Localization;

using Portal.Module;
namespace Portal.Views.Widgets;

public sealed class DiskResourceWidget : ResourceWidgetBase
{
    public DiskResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = CommonLanguageManager.Instance.widgets_diskTitle.CurrentValue();
        IconGlyph = "hard-drive";;
    }

    public override ResourceKind ResourceKind => ResourceKind.Disk;

    protected override void OnUpdate(ResourceSnapshot snapshot)
    {
        var pct = snapshot.DiskUsage;
        PrimaryText = $"{pct:F1}%";
        SecondaryText = string.Format(CommonLanguageManager.Instance.widgets_usedTotal.CurrentValue(),
            FormatBytes(snapshot.UsedDiskBytes), FormatBytes(snapshot.TotalDiskBytes));
        Percentage = pct;
        ProgressValue = pct;
    }

    private static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024UL * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }
}