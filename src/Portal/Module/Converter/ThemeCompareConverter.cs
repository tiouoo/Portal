using System.Globalization;
using Avalonia.Data.Converters;
using TioUi.Shared;

namespace Portal.Module.Converter;

public class ThemeCompareConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Theme currentTheme || parameter is not string targetThemeName)
            return false;

        return targetThemeName switch
        {
            "Light" => currentTheme == Theme.Light,
            "Dark" => currentTheme == Theme.Dark,
            "Mirage" => currentTheme == Theme.Mirage,
            "System" => currentTheme == Theme.System,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}