using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public sealed class RecentPlayListService
{
    private readonly RecentPlayService _recentPlayService = new();
    private readonly object _refreshLock = new();
    private Task? _refreshTask;

    private RecentPlayListService()
    {
        InstanceManager.Instance.InstancesChanged += (_, _) => _ = RefreshAsync();
    }

    public static RecentPlayListService Instance { get; } = new();

    public ObservableCollection<RecentPlayTarget> Items { get; } = new RecentPlayCollection();

    public event EventHandler? Refreshed;

    public static void Initialize()
    {
        _ = Instance;
    }

    public Task RefreshAsync()
    {
        lock (_refreshLock)
        {
            return _refreshTask ??= RefreshCoreAsync();
        }
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
                ((RecentPlayCollection)Items).ReplaceWith(targets);
                Refreshed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception exception)
        {
            Logger.Error(LogLanguageManager.Instance.recentPlay_refreshFailed.CurrentValue(), exception);
        }
        finally
        {
            lock (_refreshLock)
            {
                _refreshTask = null;
            }
        }
    }

    private sealed class RecentPlayCollection : ObservableCollection<RecentPlayTarget>
    {
        public void ReplaceWith(IEnumerable<RecentPlayTarget> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnPropertyChanged(new(nameof(Count)));
            OnPropertyChanged(new("Item[]"));
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
        }
    }
}
