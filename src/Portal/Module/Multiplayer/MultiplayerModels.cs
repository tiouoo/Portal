using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Module.Multiplayer;

public enum MinecraftEdition
{
    Java,
    Bedrock
}

public sealed partial class LanServerEntry : ObservableObject
{
    public required string Motd { get; init; }
    public required string Ip { get; init; }
    public required int Port { get; init; }
    public string Display => $"{Motd}·{Ip}:{Port}";
}

public sealed partial class OnlineMember : ObservableObject
{
    public required string Name { get; init; }
    public string Vendor { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Role => Kind.Equals("HOST", StringComparison.OrdinalIgnoreCase) ||
                          Kind.Equals("host", StringComparison.OrdinalIgnoreCase)
        ? "房主"
        : "成员";
    public string Detail => string.IsNullOrWhiteSpace(Vendor) ? Role : $"{Role}·{Vendor}";
}
