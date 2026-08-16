using Portal.Core.App.Service.SystemResources;
using Portal.Core.Module.Widgets;

namespace Portal.Views.Widgets;

public sealed class NetworkResourceWidget : ResourceWidgetBase
{
    private const double MaxReferenceMBps = 125;

    public NetworkResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = "网络";
        IconGeometry =
            "F1 M640,640z M0,0z M91.6,73.8C79.3,68.8 65.3,74.7 60.4,87 47.2,119.5 40,154.9 40,192 40,229.1 47.2,264.5 60.4,297 65.4,309.3 79.4,315.2 91.7,310.2 104,305.2 109.9,291.2 104.9,278.9 94,252.2 88,222.8 88,192 88,161.2 94,131.8 104.9,105 109.9,92.7 103.9,78.7 91.7,73.7z M548.4,73.8C536.1,78.8 530.2,92.8 535.2,105.1 546.1,131.9 552.1,161.3 552.1,192.1 552.1,222.9 546.1,252.3 535.2,279.1 530.2,291.4 536.2,305.4 548.4,310.4 560.6,315.4 574.7,309.4 579.7,297.2 592.8,264.7 600.1,229.3 600.1,192.2 600.1,155.1 592.9,119.7 579.7,87.2 574.7,74.9 560.7,69 548.4,74z M372.1,229.2C379.6,218.7 384,205.9 384,192 384,156.7 355.3,128 320,128 284.7,128 256,156.7 256,192 256,205.9 260.4,218.7 267.9,229.2L130.9,530.8C123.6,546.9 130.7,565.9 146.8,573.2 162.9,580.5 181.9,573.4 189.2,557.3L209.8,512.1 430.4,512.1 451,557.3C458.3,573.4 477.3,580.5 493.4,573.2 509.5,565.9 516.6,546.9 509.3,530.8L372.1,229.2z M408.5,464L231.5,464 253.3,416 386.6,416 408.4,464z M320,269.3L364.8,368 275.1,368 319.9,269.3z M195.3,137.6C200.6,125.5 195.1,111.3 182.9,106 170.7,100.7 156.6,106.2 151.3,118.4 141.5,141 136,165.9 136,192 136,218.1 141.5,243 151.3,265.6 156.6,277.7 170.8,283.3 182.9,278 195,272.7 200.6,258.5 195.3,246.4 188,229.8 184,211.4 184,192 184,172.6 188,154.2 195.3,137.6z M488.7,118.4C483.4,106.3 469.2,100.7 457.1,106 445,111.3 439.4,125.5 444.7,137.6 452,154.2 456,172.6 456,192 456,211.4 452,229.8 444.7,246.4 439.4,258.5 444.9,272.7 457.1,278 469.3,283.3 483.4,277.8 488.7,265.6 498.5,243 504,218.1 504,192 504,165.9 498.5,141 488.7,118.4z";
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