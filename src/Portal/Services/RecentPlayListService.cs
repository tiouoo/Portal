using System.Collections.ObjectModel;
using Avalonia.Threading;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Services;

public sealed class RecentPlayListService
{
    private readonly object _refreshLock = new();
    private readonly RecentPlayService _recentPlayService = new();
    private Task? _refreshTask;

    private RecentPlayListService()
    {
        InstanceManager.Instance.InstancesChanged += (_, _) => _ = RefreshAsync();
    }

    public static RecentPlayListService Instance { get; } = new();

    public ObservableCollection<RecentPlayTarget> Items { get; } = [];

    public event EventHandler? Refreshed;

    public static void Initialize()
    {
        _ = Instance;
    }

    public Task RefreshAsync()
    {
        lock (_refreshLock)
            return _refreshTask ??= RefreshCoreAsync();
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            var targets = Data.ConfigEntry.ShowRecentPlays
                ? await _recentPlayService.ScanAsync(InstanceManager.Instance.Instances).ConfigureAwait(false)
                : [];

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Clear();
                foreach (var target in targets)
                    Items.Add(target);
                Refreshed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception exception)
        {
            Logger.Error("刷新最近游玩失败。", exception);
        }
        finally
        {
            lock (_refreshLock)
                _refreshTask = null;
        }
    }
}
