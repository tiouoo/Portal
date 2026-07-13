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
            "dev" => -1,
            "nightly" => 0,
            "commit" => 1,
            _ => -1
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            -1 => "dev",
            0 => "nightly",
            1 => "commit",
            _ => "dev"
        };
    }
}