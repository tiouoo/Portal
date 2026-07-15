using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Flurl.Http;
using Newtonsoft.Json.Linq;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module.Update;

public static class UpdateChecker
{
    public static async Task<string?> Check(TopLevel sender, bool noreply = false)
    {
        var channel = Data.UiProperty.OverrideUpdateChannel;

        var current = Data.Instance.Version.VersionTitle;
        var tagName = $"publish-{channel}";
        var apiUrl = $"https://api.github.com/repos/tiouoo/Portal/releases/tags/{tagName}";
        Logger.Info($"Checking update for {current} from {apiUrl}");

        try
        {
            var release = await apiUrl
                .WithHeader("User-Agent", "Portal-Updater")
                .GetStringAsync();
            
            var json = JObject.Parse(release);
            var remoteTitle = json["name"]?.ToString();
            Logger.Info($"Version {current} Remote title: {remoteTitle}");

            if (!string.IsNullOrEmpty(remoteTitle))
            {
                return remoteTitle.Trim() == current.Trim() ? "latest" : remoteTitle;
            }
        }
        catch (FlurlHttpException e)
        {
            if (noreply) return null;
            Dispatcher.UIThread.Post(() => sender.Notice($"网络请求错误: {e.StatusCode}\n{e.Message}", NotificationType.Error));
        }
        catch (Exception e)
        {
            if (noreply) return null;
            Dispatcher.UIThread.Post(() => sender.Notice($"检查更新失败\n{e.Message}", NotificationType.Error));
        }

        return null;
    }
}