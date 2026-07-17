using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Module.Converter;

public sealed class ManagedTaskStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ManagedTaskStatus.Faulted => new SolidColorBrush(Color.Parse("#C77C7C")),
        ManagedTaskStatus.Running or ManagedTaskStatus.Cancelling => new SolidColorBrush(Color.Parse("#C6A06A")),
        ManagedTaskStatus.Cancelled => new SolidColorBrush(Color.Parse("#8D91A7")),
        ManagedTaskStatus.Pending or ManagedTaskStatus.Waiting => new SolidColorBrush(Color.Parse("#9CA3AF")),
        _ => new SolidColorBrush(Color.Parse("#79A88A"))
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
