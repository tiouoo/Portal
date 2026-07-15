using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Portal.Core.Minecraft.Classes;

namespace Portal.Module.Converter;

public class AccountTypeToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AccountType type || parameter is not string param)
            return false;

        return param switch
        {
            "IsOffline" => type == AccountType.Offline,
            "IsOnline" => type is AccountType.Microsoft or AccountType.Yggdrasil,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
