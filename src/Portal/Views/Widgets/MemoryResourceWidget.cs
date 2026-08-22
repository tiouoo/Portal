using Portal.Core.Classes.Entries;
using Portal.Core.Module.Widgets;
using Portal.Core.Services.SystemResources;
using Portal.Localization;

using Portal.Module;
namespace Portal.Views.Widgets;

public sealed class MemoryResourceWidget : ResourceWidgetBase
{
    private MemoryWidgetData? _data;
    private bool _showPercentage = true;

    public MemoryResourceWidget(WidgetCellSize size) : base(size)
    {
        Title = CommonLanguageManager.Instance.widgets_memoryTitle.CurrentValue();
        IconGlyph = "\ue62d";
    }

    public override ResourceKind ResourceKind => ResourceKind.Memory;

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
            SecondaryText = string.Format(CommonLanguageManager.Instance.widgets_usedTotal.CurrentValue(),
                FormatBytes(used), FormatBytes(total));
        }
        else
        {
            PrimaryText = FormatBytes(used);
            SecondaryText = string.Format(CommonLanguageManager.Instance.widgets_usagePercent.CurrentValue(), pct,
                FormatBytes(total));
        }

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