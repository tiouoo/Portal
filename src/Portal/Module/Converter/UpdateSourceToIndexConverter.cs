using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Portal.Classes.Enums;

namespace Portal.Module.Converter;

public class UpdateSourceToIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        UpdateSource.Cnb => 1,
        _ => 0
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        1 => UpdateSource.Cnb,
        _ => UpdateSource.Github
    };
}
