using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Flurl.Http;
using Newtonsoft.Json.Linq;
using Portal.Const;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module.Update;

public class CheckUpdate
{
    public static async Task<string?> Main(TopLevel sender)
    {
        var channel = Data.UiProperty.OverrideUpdateChannel;

        var shanghaiZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var buildTimeInShanghai = TimeZoneInfo.ConvertTime(Data.Instance.Version.BuildTime, shanghaiZone);
        var startEpoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        
        var totalHours = (int)((buildTimeInShanghai - startEpoch).TotalSeconds / 3600);
        var appVersion = $"{Data.Instance.Version.Version}.{totalHours}";

        var current = $"build-{Data.Instance.Version.Type}-{appVersion}-{Data.Instance.Version.Action}-{Data.Instance.Version.Commit}";
        var tagName = $"publish-{channel}";
        var apiUrl = $"https://api.github.com/repos/tiouoo/Portal/releases/tags/{tagName}";

        try
        {
            var release = await apiUrl
                .WithHeader("User-Agent", "Portal-Updater")
                .GetStringAsync();
            
            var json = JObject.Parse(release);
            var remoteTitle = json["name"]?.ToString();

            if (!string.IsNullOrEmpty(remoteTitle))
            {
                return remoteTitle.Trim() == current.Trim() ? "latest" : remoteTitle;
            }
        }
        catch (FlurlHttpException e)
        {
            Dispatcher.UIThread.Post(() => sender.Notice($"网络请求错误: {e.StatusCode}\n{e.Message}", NotificationType.Error));
        }
        catch (Exception e)
        {
            Dispatcher.UIThread.Post(() => sender.Notice($"检查更新失败\n{e.Message}", NotificationType.Error));
        }

        return null;
    }
}