using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LiteSkinViewer2D;
using LiteSkinViewer2D.Extensions;
using Newtonsoft.Json;
using SkiaSharp;

namespace Portal.Core.Minecraft.Classes;

public partial class MinecraftAccount(AccountType accountType) : ObservableObject
{
    [JsonIgnore] public static readonly string SteveSkin =
        @"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";

    [ObservableProperty] public partial DateTime CreateAt { get; set; } = DateTime.Now;

    [NotifyPropertyChangedFor(nameof(DisplayLastLoginTime))]
    [ObservableProperty]
    public partial DateTime LastLoginTime { get; set; } = DateTime.MinValue;

    public AccountType AccountType { get; } = accountType;

    [NotifyPropertyChangedFor(nameof(DisplayAccountNote))]
    [ObservableProperty]
    public partial string? AccountNote { get; set; }

    [NotifyPropertyChangedFor(nameof(ShortDisplay))]
    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty] public partial Guid? Uuid { get; set; }
    [ObservableProperty] public partial string? AccessToken { get; set; }
    [ObservableProperty] public partial string? RefreshToken { get; set; }
    [ObservableProperty] public partial DateTime? LastRefreshTime { get; set; } = DateTime.MinValue;
    public string? YggdrasilServerUrl { get; init; }
    public string? ServerNote { get; init; }
    [ObservableProperty] public partial string? ClientToken { get; set; }
    [ObservableProperty] public partial string? Password { get; set; }
    [ObservableProperty] public partial string? Email { get; set; }
    [ObservableProperty] public partial Dictionary<string, string> MetaData { get; set; } = [];

    [JsonIgnore]
    public bool IsOffline => AccountType == AccountType.Offline;

    [JsonIgnore]
    public string DisplayAccountNote
    {
        get
        {
            if (!string.IsNullOrEmpty(AccountNote))
                return AccountNote;
            return AccountType switch
            {
                AccountType.Offline => "离线模式",
                AccountType.Yggdrasil =>
                    ServerNote ?? (Uri.TryCreate(YggdrasilServerUrl, UriKind.Absolute, out var uri)
                        ? uri.Host
                        : "外置登录"),
                AccountType.Microsoft => "微软账户",
                _ => "未知"
            };
        }
    }

    public string Skin { get; set; } = SteveSkin;

    [JsonIgnore]
    public string ShortDisplay
    {
        get
        {
            var t = AccountType switch
            {
                AccountType.Offline => "离线",
                AccountType.Yggdrasil => "外置",
                AccountType.Microsoft => "微软",
                _ => "未知"
            };
            return $"{t} · {Name}";
        }
    }

    [JsonIgnore]
    public string ShortType
    {
        get
        {
            var t = AccountType switch
            {
                AccountType.Offline => "离线",
                AccountType.Yggdrasil => "外置",
                AccountType.Microsoft => "微软",
                _ => "未知"
            };
            return $"{t}";
        }
    }

    [JsonIgnore]
    [field: JsonIgnore]
    public Bitmap Head
    {
        get { return field ??= HandleHeadSkin(); }
    }

    [JsonIgnore]
    [field: JsonIgnore]
    public Bitmap Body
    {
        get { return field ??= HandleBodySkin(); }
    }

    [JsonIgnore]
    [field: JsonIgnore]
    public Bitmap Cover
    {
        get { return field ??= HandleCoverSkin(); }
    }

    private Bitmap HandleCoverSkin()
    {
        var imageBytes = Convert.FromBase64String(Skin);
        return CoverCapturer.Default.Capture(SKBitmap.Decode(imageBytes)).ToBitmap(130);
    }

    private Bitmap HandleBodySkin()
    {
        var imageBytes = Convert.FromBase64String(Skin);
        return FullBodyCapturer.Default.Capture(SKBitmap.Decode(imageBytes)).ToBitmap();
    }

    private Bitmap HandleHeadSkin()
    {
        var imageBytes = Convert.FromBase64String(Skin);
        return HeadCapturer.Default.Capture(SKBitmap.Decode(imageBytes)).ToBitmap(36);
    }

    public static Guid GetMinecraftOfflineUuid(string name)
    {
        if (string.IsNullOrEmpty(name)) return Guid.Empty;
        var bytes = Encoding.UTF8.GetBytes($"OfflinePlayer:{name}");
        var hash = MD5.HashData(bytes);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        Array.Reverse(guidBytes, 0, 4);
        Array.Reverse(guidBytes, 4, 2);
        Array.Reverse(guidBytes, 6, 2);
        return new Guid(guidBytes);
    }

    [JsonIgnore]
    public string DisplayLastLoginTime
    {
        get
        {
            if (LastLoginTime == DateTime.MinValue)
                return "从未登录";

            var timeSpan = DateTime.Now - LastLoginTime;

            if (timeSpan.TotalMinutes < 1)
                return "刚刚";

            if (!(timeSpan.TotalDays <= 30)) return LastLoginTime.ToString("yyyy-MM-dd HH:mm");
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} 天前";

            return timeSpan.TotalHours >= 1 ? $"{(int)timeSpan.TotalHours} 小时前" : $"{(int)timeSpan.TotalMinutes} 分钟前";
        }
    }

    public bool Equals(MinecraftAccount? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (AccountType != other.AccountType) return false;

        if (AccountType == AccountType.Yggdrasil &&
            !string.Equals(YggdrasilServerUrl, other.YggdrasilServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uuid.HasValue && other.Uuid.HasValue)
        {
            return Uuid.Value == other.Uuid.Value;
        }

        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MinecraftAccount);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AccountType);

        if (AccountType == AccountType.Yggdrasil && YggdrasilServerUrl != null)
        {
            hash.Add(YggdrasilServerUrl, StringComparer.OrdinalIgnoreCase);
        }

        if (Uuid.HasValue)
        {
            hash.Add(Uuid.Value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(MinecraftAccount? left, MinecraftAccount? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(MinecraftAccount? left, MinecraftAccount? right)
    {
        return !(left == right);
    }
}