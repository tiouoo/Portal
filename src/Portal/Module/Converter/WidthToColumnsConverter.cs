using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Portal.Module.Converter;

public class WidthToColumnsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width) return 4;

        var minColumnWidth = parameter is string s && int.TryParse(s, out var paramWidth) ? paramWidth : 220;
        var spacing = 12;

        var availableWidth = width - 24;

        if (availableWidth <= minColumnWidth) return 1;

        var columns = (int)Math.Floor((availableWidth + spacing) / (minColumnWidth + spacing));

        return Math.Max(1, Math.Min(columns, 6));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}