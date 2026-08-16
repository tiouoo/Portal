using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Core.Minecraft.Classes;

public partial class BedrockAccount : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] public partial string Gamertag { get; set; } = string.Empty;
    [ObservableProperty] public partial string Xuid { get; set; } = string.Empty;
    [ObservableProperty] public partial string? AvatarUrl { get; set; }
    [ObservableProperty] public partial string? AccountNote { get; set; }
    [ObservableProperty] public partial string AccessToken { get; set; } = string.Empty;
    [ObservableProperty] public partial string RefreshToken { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset ExpiresAt { get; set; }
    [ObservableProperty] public partial DateTime LastLoginTime { get; set; } = DateTime.MinValue;

    public string DisplayAccountNote => string.IsNullOrWhiteSpace(AccountNote) ? "基岩版 · Xbox" : AccountNote;
    public string ShortDisplay => $"基岩·{Gamertag}";

    public string DisplayLastLoginTime => LastLoginTime == DateTime.MinValue
        ? "从未登录"
        : LastLoginTime.ToString("yyyy-MM-dd HH:mm");

    partial void OnAccountNoteChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayAccountNote));
    }

    partial void OnGamertagChanged(string value)
    {
        OnPropertyChanged(nameof(ShortDisplay));
    }
}