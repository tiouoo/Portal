using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Portal.Module.Converter;

public class IntCompareConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        
        if (value is not int source || parameter is not string param)
            return false;

        var arr = param.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (arr.Length != 2 || !int.TryParse(arr[1], out int compareNum))
            return false;

        return arr[0] switch
        {
            ">" => source > compareNum,
            ">=" => source >= compareNum,
            "<" => source < compareNum,
            "<=" => source <= compareNum,
            "==" => source == compareNum,
            "!=" => source != compareNum,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}