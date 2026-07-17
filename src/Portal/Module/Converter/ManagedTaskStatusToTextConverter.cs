using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Module.Converter;

public sealed class ManagedTaskStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ManagedTaskStatus.Faulted => "失败",
        ManagedTaskStatus.Running => "执行中",
        ManagedTaskStatus.Cancelling => "正在取消",
        ManagedTaskStatus.Pending => "等待中",
        ManagedTaskStatus.Waiting => "等待中",
        ManagedTaskStatus.Cancelled => "已取消",
        ManagedTaskStatus.Completed => "已完成",
        _ => "暂无任务"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
