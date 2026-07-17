using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Module.Converter;

public sealed class ManagedTaskStatusToCompletedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ManagedTaskStatus.Completed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
