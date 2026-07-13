using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Portal.Classes.Entries;

namespace Portal.Module.Converter;

public class BackgroundModeCompareConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BackgroundMode currentMode || parameter is not string targetModeName)
            return false;

        var re = targetModeName.StartsWith('!');

        var res = targetModeName.Trim('!') switch
        {
            "Default" => currentMode == BackgroundMode.Default,
            "Image" => currentMode == BackgroundMode.Image,
            "SolidColor" => currentMode == BackgroundMode.Color,
            "Acrylic" => currentMode == BackgroundMode.Acrylic,
            "Blur" => currentMode == BackgroundMode.Blur,
            "Mica" => currentMode == BackgroundMode.Mica,
            _ => false
        };
        return re ? !res : res;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}