using Avalonia;

namespace Portal.Core.Module.Widgets;

public static class WidgetGeometry
{
    public const double CellSize = 130;
    public const double Spacing = 12;
    public const double Pitch = CellSize + Spacing;

    public static Size GetSize(int columns, int rows)
    {
        return new Size(
            columns * CellSize + (columns - 1) * Spacing,
            rows * CellSize + (rows - 1) * Spacing);
    }

    public static Size GetSize(WidgetCellSize size)
    {
        return GetSize(size.Columns, size.Rows);
    }

    public static int GetCols(double width)
    {
        return Math.Max(1, (int)Math.Round((width + Spacing) / Pitch));
    }

    public static int GetRows(double height)
    {
        return Math.Max(1, (int)Math.Round((height + Spacing) / Pitch));
    }
}