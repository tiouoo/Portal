using System.Globalization;
using Avalonia.Data.Converters;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Module.Converter;

public sealed class ManagedTaskStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ManagedTaskStatus.Faulted => CommonLanguageManager.Instance.taskStatus_failed.CurrentValue(),
            ManagedTaskStatus.Running => CommonLanguageManager.Instance.taskStatus_running.CurrentValue(),
            ManagedTaskStatus.Cancelling => CommonLanguageManager.Instance.taskStatus_cancelling.CurrentValue(),
            ManagedTaskStatus.Pending => CommonLanguageManager.Instance.taskStatus_waiting.CurrentValue(),
            ManagedTaskStatus.Waiting => CommonLanguageManager.Instance.taskStatus_waiting.CurrentValue(),
            ManagedTaskStatus.Cancelled => CommonLanguageManager.Instance.taskStatus_cancelled.CurrentValue(),
            ManagedTaskStatus.Completed => CommonLanguageManager.Instance.taskStatus_completed.CurrentValue(),
            _ => CommonLanguageManager.Instance.taskStatus_none.CurrentValue()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}