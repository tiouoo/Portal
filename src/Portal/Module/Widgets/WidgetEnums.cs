namespace Portal.Module.Widgets;

public enum WidgetKind
{
    Clock,
    Instance,
    QuickWorld,
    QuickServer
}

public readonly record struct WidgetCellSize(int Columns, int Rows)
{
    public static WidgetCellSize Parse(string value)
    {
        var s = value.Trim().Replace('x', '×').Replace('X', '×').Replace('*', '×');
        var parts = s.Split('×');
        if (parts.Length == 2 &&
            int.TryParse(parts[0].Trim(), out var columns) &&
            int.TryParse(parts[1].Trim(), out var rows))
            return new WidgetCellSize(Math.Max(1, columns), Math.Max(1, rows));
        return new WidgetCellSize(1, 1);
    }

    public override string ToString() => $"{Columns}×{Rows}";
}
