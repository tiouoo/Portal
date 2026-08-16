using Avalonia;

namespace Portal.Module.Widgets;

public static class WidgetGeometry
{
    public const double CellSize = 130;
    public const double Spacing = 12;
    public const double Pitch = CellSize + Spacing;

    public static Size GetSize(int columns, int rows) => new(
        columns * CellSize + (columns - 1) * Spacing,
        rows * CellSize + (rows - 1) * Spacing);

    public static Size GetSize(WidgetCellSize size) => GetSize(size.Columns, size.Rows);

    public static int GetCols(double width) => Math.Max(1, (int)Math.Round((width + Spacing) / Pitch));

    public static int GetRows(double height) => Math.Max(1, (int)Math.Round((height + Spacing) / Pitch));
}
