using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Portal.Core.Minecraft.Classes;

namespace Portal.Module.Converter;

public class AccountTypeToBrushConverter : IValueConverter
{
    private static readonly LinearGradientBrush MicrosoftBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.Parse("#FF9A9E"), 0.0),
            new GradientStop(Color.Parse("#FAD0C4"), 0.5),
            new GradientStop(Color.Parse("#FFD1FF"), 1.0),
        ]
    };

    private static readonly LinearGradientBrush OfflineBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.Parse("#83a1fd"), 0.0),
            new GradientStop(Color.Parse("#79fbd1"), 1.0),
        ]
    };

    private static readonly LinearGradientBrush YggdrasilBrush = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.Parse("#fda184"), 0.0),
            new GradientStop(Color.Parse("#f6d166"), 1.0),
        ]
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AccountType type)
            return MicrosoftBrush;

        return type switch
        {
            AccountType.Microsoft => MicrosoftBrush,
            AccountType.Yggdrasil => YggdrasilBrush,
            AccountType.Offline => OfflineBrush,
            _ => MicrosoftBrush,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
