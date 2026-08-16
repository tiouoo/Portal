using System.Globalization;
using Avalonia.Data.Converters;

namespace Portal.Module.Converter;

public class StringIsNotWriteSpaceOrNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s)
            return false;

        return !string.IsNullOrWhiteSpace(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}