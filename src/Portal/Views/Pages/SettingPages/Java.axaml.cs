using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Portal.Const;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Operations.Java;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("Java 虚拟机与内存", "设置/Java 虚拟机与内存", "Java")]
public partial class Java : DataUserControl
{
    public Java()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void AddJava_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        await AddJavaAsync(topLevel);
    }

    private async void AutoScan_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            var result = await JavaRuntimeOperations.ScanAndAddAsync(Data.ConfigEntry.JavaRuntimes);
            if (Data.ConfigEntry.DefaultJavaRuntime == null)
                Data.ConfigEntry.DefaultJavaRuntime = Data.ConfigEntry.JavaRuntimes.FirstOrDefault();

            topLevel.Notice(
                $"扫描完成：新增 {result.AddedCount} 个 Java，重复 {result.DuplicateCount} 个",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice($"Java 扫描失败：{ex.Message}", NotificationType.Error);
        }
    }

    private async Task AddJavaAsync(TopLevel topLevel)
    {
        try
        {
            var result = await JavaRuntimeOperations.AddFromPickerAsync(topLevel, Data.ConfigEntry.JavaRuntimes);
            if (result == null) return;

            if (!result.IsValid)
            {
                topLevel.Notice("无法识别该 Java 可执行文件", NotificationType.Error);
                return;
            }

            if (result.IsDuplicate)
            {
                topLevel.Notice("该 Java 已在列表中", NotificationType.Warning);
                return;
            }

            Data.ConfigEntry.DefaultJavaRuntime ??= result.JavaRuntime;
            topLevel.Notice("Java 已添加", NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice($"添加 Java 失败：{ex.Message}", NotificationType.Error);
        }
    }

    private void RemoveJava_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: JavaRuntimeEntry java }) return;

        Data.ConfigEntry.DefaultJavaRuntime = JavaRuntimeOperations.Remove(
            Data.ConfigEntry.JavaRuntimes,
            java,
            Data.ConfigEntry.DefaultJavaRuntime);

        this.AsTopLevel().Notice(new NotificationOptions
        {
            Content = $"已移除 Java：{java.DisplayName}",
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons =
            [
                new OperateButtonEntry("撤销", _ =>
                {
                    JavaRuntimeOperations.Restore(Data.ConfigEntry.JavaRuntimes, java);
                    Data.ConfigEntry.DefaultJavaRuntime = java;
                }, true)
            ]
        });
    }
}
