using System.Globalization;
using Avalonia.Data.Converters;
using Portal.Core.Classes;

namespace Portal.Module.Converter;

public class UpdateSourceToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UpdateSource.Cnb => 1,
            _ => 0
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            1 => UpdateSource.Cnb,
            _ => UpdateSource.Github
        };
    }
}