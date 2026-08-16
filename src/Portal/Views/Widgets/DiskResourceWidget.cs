using Portal.Core.App.Service.SystemResources;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

/// <summary>磁盘占用小组件。主显示百分比，副文本显示已用/总量。</summary>
public sealed class DiskResourceWidget : ResourceWidgetBase
{
    public DiskResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = "磁盘";
        IconGeometry = "F1 M640,640z M0,0z M160,96C124.7,96,96,124.7,96,160L96,324.1C114.1,311.4,136.2,304,160,304L480,304C503.8,304,525.9,311.4,544,324.1L544,160C544,124.7,515.3,96,480,96L160,96z M544,416C544,380.7,515.3,352,480,352L160,352C124.7,352,96,380.7,96,416L96,480C96,515.3,124.7,544,160,544L480,544C515.3,544,544,515.3,544,480L544,416z M320,448C320,430.3 334.3,416 352,416 369.7,416 384,430.3 384,448 384,465.7 369.7,480 352,480 334.3,480 320,465.7 320,448z M448,416C465.7,416 480,430.3 480,448 480,465.7 465.7,480 448,480 430.3,480 416,465.7 416,448 416,430.3 430.3,416 448,416z";
    }

    public override ResourceKind ResourceKind => ResourceKind.Disk;

    protected override void OnUpdate(ResourceSnapshot snapshot)
    {
        var pct = snapshot.DiskUsage;
        PrimaryText = $"{pct:F1}%";
        SecondaryText = $"已用 {FormatBytes(snapshot.UsedDiskBytes)}\n总 {FormatBytes(snapshot.TotalDiskBytes)}";
        Percentage = pct;
        ProgressValue = pct;
    }

    private static string FormatBytes(ulong bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024UL * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
