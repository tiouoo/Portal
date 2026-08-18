using System.Globalization;
using Avalonia.Data.Converters;
using Portal.Core.Classes;

namespace Portal.Module.Converter;

public class UpdateSourceCompareConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not UpdateSource updateSource || parameter is not string target)
            return false;

        return target switch
        {
            "Github" => updateSource == UpdateSource.Github,
            "Cnb" => updateSource == UpdateSource.Cnb,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
