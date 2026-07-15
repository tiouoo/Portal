using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Portal.Classes.Entries;
using Portal.Classes.Enums;
using Portal.Const;
using Portal.Core;
using Portal.Module.Update;
using Portal.Views;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Tab.Common;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;

namespace Portal.Module.Initialize;

public static partial class Initializer
{
    public static void Ui()
    {
        File.WriteAllText(ConfigPath.AppPathDataPath,
            Process.GetCurrentProcess().MainModule.FileName);

        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        if (Data.ConfigEntry.EnableCustomForegroundColor)
        {
            ConfigEntry.SetForegroundColor(Data.ConfigEntry.ForegroundColor);
        }

        LoopGc.BeginLoop();

        Functions.CreateNewTabWindowFunc = _ => new TabWindow(false);
        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;

        Events.CoreSaveSettings += Portal.App.Method.SaveConfig;

        if (Data.ConfigEntry.BackgroundMode == BackgroundMode.Default)
        {
            Application.Current.Resources.Remove("BackGroundOpacity");
            Application.Current.Resources.Remove("TranslucentBackGroundOpacity");
        }
        else
        {
            Application.Current.Resources["BackGroundOpacity"] = Data.ConfigEntry.ControlOpacity;
            Application.Current.Resources["TranslucentBackGroundOpacity"] = Data.ConfigEntry.TranslucentControlOpacity;
        }

        if (Data.ConfigEntry.EnableCheckAutoUpdate && Data.Instance.Version.Type != "dev")
            _ = CheckUpdate();

        InitializationEvents.RaiseAfterUiLoaded();
    }

    private static async Task CheckUpdate()
    {
        var result = await UpdateChecker.Check(null, true);
        switch (result)
        {
            case null:
                Data.UiProperty.FoundNewVersion = false;
                Data.UiProperty.IsLatestVersion = false;
                return;
            case "latest":
                Data.UiProperty.IsLatestVersion = true;
                return;
            default:
                Data.UiProperty.NewVersion = result;
                Data.UiProperty.FoundNewVersion = true;
                break;
        }
    }
}