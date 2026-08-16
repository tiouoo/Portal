namespace Portal.Core.Minecraft.Classes;

public sealed record WorldWeatherSettings(
    bool Raining,
    bool Thundering,
    int RainTime,
    int ThunderTime,
    int ClearWeatherTime);

public sealed record WorldClockSettings(IReadOnlyDictionary<string, long> TotalTicks);