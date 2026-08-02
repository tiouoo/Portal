using Portal.Classes.Entries;
using Portal.Core.SystemResources;
using Portal.Module.Widgets;

namespace Portal.Views.Widgets;

/// <summary>
/// 内存占用小组件。支持右键切换显示模式：
/// - 百分比模式：主显示百分比，副文本显示已用/总量
/// - 数值模式：主显示已用内存，副文本显示百分比
/// </summary>
public sealed class MemoryResourceWidget : ResourceWidgetBase
{
    private bool _showPercentage = true;
    private MemoryWidgetData? _data;

    public MemoryResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = "内存";
        IconGeometry = "F1 M640,640z M0,0z M128,128C92.7,128,64,156.7,64,192L64,199.4C64,206.2 68.4,212 74.1,215.7 87.3,224.3 96,239.1 96,256 96,272.9 87.3,287.7 74.1,296.3 68.4,300 64,305.8 64,312.6L64,368 576,368 576,312.6C576,305.8 571.6,300 565.9,296.3 552.7,287.7 544,272.9 544,256 544,239.1 552.7,224.3 565.9,215.7 571.6,212 576,206.2 576,199.4L576,192C576,156.7,547.3,128,512,128L128,128z M576,480L576,416 64,416 64,480C64,497.7,78.3,512,96,512L160,512 160,488C160,474.7 170.7,464 184,464 197.3,464 208,474.7 208,488L208,512 296,512 296,488C296,474.7 306.7,464 320,464 333.3,464 344,474.7 344,488L344,512 432,512 432,488C432,474.7 442.7,464 456,464 469.3,464 480,474.7 480,488L480,512 544,512C561.7,512,576,497.7,576,480z M224,224L224,288C224,305.7 209.7,320 192,320 174.3,320 160,305.7 160,288L160,224C160,206.3 174.3,192 192,192 209.7,192 224,206.3 224,224z M352,224L352,288C352,305.7 337.7,320 320,320 302.3,320 288,305.7 288,288L288,224C288,206.3 302.3,192 320,192 337.7,192 352,206.3 352,224z M480,224L480,288C480,305.7 465.7,320 448,320 430.3,320 416,305.7 416,288L416,224C416,206.3 430.3,192 448,192 465.7,192 480,206.3 480,224z";
    }

    public override ResourceKind ResourceKind => ResourceKind.Memory;

    /// <summary>切换显示模式（百分比 ↔ 数值），并持久化到布局数据。</summary>
    public void ToggleDisplayMode()
    {
        _showPercentage = !_showPercentage;
        if (_data != null)
            _data.ShowPercentage = _showPercentage;
        OnUpdate(SystemResourceService.Instance.Latest);
    }

    public override void Initialize(WidgetLayoutData layout)
    {
        _data = layout.Data as MemoryWidgetData;
        // 兼容旧配置：若 Data 缺失则补建一个，避免切换模式时无处持久化。
        if (_data == null)
        {
            _data = new MemoryWidgetData();
            layout.Data = _data;
        }
        _showPercentage = _data.ShowPercentage ?? true;
        base.Initialize(layout);
    }

    protected override void OnUpdate(ResourceSnapshot snapshot)
    {
        var total = snapshot.TotalMemoryBytes;
        var used = snapshot.UsedMemoryBytes;
        var pct = snapshot.MemoryUsage;

        if (_showPercentage)
        {
            PrimaryText = $"{pct:F1}%";
            SecondaryText = $"已用 {FormatBytes(used)}\n总 {FormatBytes(total)}";
        }
        else
        {
            PrimaryText = FormatBytes(used);
            SecondaryText = $"占用 {pct:F1}%\n总 {FormatBytes(total)}";
        }

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
