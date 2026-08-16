using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Core.Module;

public static class DesktopShortcutUi
{
    public static async Task CreateAsync(TopLevel? topLevel, Func<Task<string>> create)
    {
        if (topLevel == null)
            return;

        try
        {
            var shortcutPath = await create();
            topLevel.Notice($"桌面快捷方式已创建：{shortcutPath}", NotificationType.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            topLevel.Notice($"创建桌面快捷方式失败：{ex.Message}", NotificationType.Error);
        }
    }
}