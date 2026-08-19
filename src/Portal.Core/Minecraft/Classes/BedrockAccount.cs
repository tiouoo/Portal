using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Localization;

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

    public string DisplayAccountNote => string.IsNullOrWhiteSpace(AccountNote) ? CommonLanguageManager.Instance.account_bedrockXbox.CurrentValue() : AccountNote;
    public string ShortDisplay => string.Format(CommonLanguageManager.Instance.account_bedrockShort.CurrentValue(), Gamertag);

    public string DisplayLastLoginTime => LastLoginTime == DateTime.MinValue
        ? CommonLanguageManager.Instance.account_neverLoggedIn.CurrentValue()
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