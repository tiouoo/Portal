using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Portal.Views.Widgets;

namespace Portal.Views.Pages;

public sealed class RecentPlayItem : INotifyPropertyChanged, IDisposable
{
    private readonly ServerPing _ping = new();
    private bool _iconLoaded;
    private Bitmap? _ownedIcon;

    public RecentPlayItem(RecentPlayTarget target)
    {
        Target = target;
        if (target.Type == RecentPlayTargetType.Server)
        {
            _ping.Changed += OnPingChanged;
            var address = target.ServerAddress ?? string.Empty;
            _ping.Start(string.IsNullOrWhiteSpace(address)
                ? string.Empty
                : ServerPing.BuildAddress(address, target.ServerPort ?? 25565));
        }
    }

    public RecentPlayTarget Target { get; }

    public string Name => Target.Name;
    public string InstanceName => Target.Instance.InstanceName;
    public string Details => Target.Details;
    public DateTime LastPlayedTime => Target.LastPlayedTime;
    public string RelativeTime => GetRelativeTime(Target.LastPlayedTime);
    public bool CanQuickPlay => Target.CanQuickPlay;

    public string? FolderName => Target.Type == RecentPlayTargetType.World
        ? Target.Id
        : null;

    public bool HasFolderName => FolderName is not null;

    public bool IsServer => Target.Type == RecentPlayTargetType.Server;

    public string? SubtitleText => Target.Type == RecentPlayTargetType.World ? FolderName : ServerDisplayAddress;

    public bool HasSubtitle => SubtitleText is not null;

    public string StatusText => _ping.StatusText;

    public IBrush StatusBrush => _ping.StatusBrush;

    public string PingText => _ping.PingText;

    public bool HasPing => _ping.HasPing;

    public IBrush PingBrush => _ping.PingBrush;

    public string PlayersText => _ping.PlayersText;

    public bool HasPlayers => _ping.HasPlayers;

    public bool IsFavorite =>
        Target.Instance.Config.RecentPlayFavorites?.TryGetValue(Target.Id, out var favorite) == true && favorite;

    public bool IsBlocked => BlockListService.Instance.IsRecentPlayBlocked(Target);
    public string BlockHeaderText => IsBlocked
        ? CommonLanguageManager.Instance.minecraft_unblock.CurrentValue()
        : CommonLanguageManager.Instance.minecraft_block.CurrentValue();
    public string FavoriteHeaderText => IsFavorite
        ? CommonLanguageManager.Instance.minecraft_unfavorite.CurrentValue()
        : CommonLanguageManager.Instance.minecraft_favorite.CurrentValue();

    public Bitmap Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _ownedIcon = Target.Type == RecentPlayTargetType.Server && Target.ServerIconData is { Length: > 0 }
                    ? LoadIcon(Target.ServerIconData)
                    : Target.WorldIconPath is { } path && File.Exists(path)
                        ? LoadIcon(path)
                        : null;
            }

            return _ownedIcon ?? Target.Instance.Icon;
        }
    }

    private string? ServerDisplayAddress
    {
        get
        {
            var address = Target.ServerAddress;
            if (string.IsNullOrWhiteSpace(address))
                return null;

            return ServerPing.BuildDisplayAddress(address, Target.ServerPort ?? 25565);
        }
    }

    public void Dispose()
    {
        _ping.Changed -= OnPingChanged;
        _ping.Cancel();
        var icon = _ownedIcon;
        _ownedIcon = null;
        if (icon != null)
            Dispatcher.UIThread.Post(icon.Dispose, DispatcherPriority.Background);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleFavorite()
    {
        var favorites = Target.Instance.Config.RecentPlayFavorites ??= [];
        if (IsFavorite)
            favorites.Remove(Target.Id);
        else
            favorites[Target.Id] = true;
        Target.Instance.SaveConfig();
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteHeaderText));
    }

    public void RefreshBlockState()
    {
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(BlockHeaderText));
    }

    private void OnPingChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(PingText));
        OnPropertyChanged(nameof(HasPing));
        OnPropertyChanged(nameof(PingBrush));
        OnPropertyChanged(nameof(PlayersText));
        OnPropertyChanged(nameof(HasPlayers));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static Bitmap? LoadIcon(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            return Bitmap.DecodeToWidth(stream, 48);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? LoadIcon(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 48);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetRelativeTime(DateTime time)
    {
        var elapsed = DateTime.Now - time;
        if (elapsed.TotalMinutes < 1) return CommonLanguageManager.Instance.relativeTime_justNow.CurrentValue();
        if (elapsed.TotalDays >= 30) return time.ToString("yyyy-MM-dd HH:mm");
        if (elapsed.TotalDays >= 1)
            return string.Format(CommonLanguageManager.Instance.relativeTime_daysAgo.CurrentValue(),
                (int)elapsed.TotalDays);
        return elapsed.TotalHours >= 1
            ? string.Format(CommonLanguageManager.Instance.relativeTime_hoursAgo.CurrentValue(),
                (int)elapsed.TotalHours)
            : string.Format(CommonLanguageManager.Instance.relativeTime_minutesAgo.CurrentValue(),
                (int)elapsed.TotalMinutes);
    }
}
