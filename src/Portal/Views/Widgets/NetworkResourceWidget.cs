using Portal.Core.Module.Widgets;
using Portal.Core.Services.SystemResources;
using Portal.Localization;

using Portal.Module;
namespace Portal.Views.Widgets;

public sealed class NetworkResourceWidget : ResourceWidgetBase
{
    private const double MaxReferenceMBps = 125;

    public NetworkResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = CommonLanguageManager.Instance.widgets_networkTitle.CurrentValue();
        IconGlyph = "\ue602";
        ProgressMaximum = MaxReferenceMBps;
    }

    public override ResourceKind ResourceKind => ResourceKind.Network;

    protected override void OnUpdate(ResourceSnapshot snapshot)
    {
        var down = snapshot.NetworkDownloadBytesPerSec;
        var up = snapshot.NetworkUploadBytesPerSec;
        var total = down + up;

        var downMB = down / (1024.0 * 1024);
        var upMB = up / (1024.0 * 1024);
        var totalMB = total / (1024.0 * 1024);

        PrimaryText = $"{totalMB:F1} MB/s";
        SecondaryText = $"↓ {downMB:F1} MB/s\n↑ {upMB:F1} MB/s";

        Percentage = Math.Min(100, totalMB / MaxReferenceMBps * 100);
        ProgressValue = Math.Min(MaxReferenceMBps, totalMB);
    }
}