using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Portal.Module.Converter;

public class UpdateChannelToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "commit" => 1,
            _ => 0
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            1 => "commit",
            _ => "nightly"
        };
    }
}