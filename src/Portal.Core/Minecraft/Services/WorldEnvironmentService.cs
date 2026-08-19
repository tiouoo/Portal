using fNbt;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldEnvironmentService
{
    public Task<WorldWeatherSettings?> LoadWeatherAsync(string worldPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => LoadWeather(worldPath, cancellationToken), cancellationToken);
    }

    public Task<WorldClockSettings?> LoadClocksAsync(string worldPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => LoadClocks(worldPath, cancellationToken), cancellationToken);
    }

    public Task SaveWeatherAsync(string worldPath, WorldWeatherSettings settings,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveWeather(worldPath, settings, cancellationToken), cancellationToken);
    }

    public Task SaveClocksAsync(string worldPath, WorldClockSettings settings,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveClocks(worldPath, settings, cancellationToken), cancellationToken);
    }

    private static WorldWeatherSettings? LoadWeather(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadModernFile(worldPath, "weather.dat");
        var data = file?.RootTag["data"] as NbtCompound ?? LoadLegacyData(worldPath);
        return data == null
            ? null
            : new WorldWeatherSettings(GetBool(data, "raining"), GetBool(data, "thundering"),
                GetInt(data, file == null ? "rainTime" : "rain_time"),
                GetInt(data, file == null ? "thunderTime" : "thunder_time"),
                GetInt(data, file == null ? "clearWeatherTime" : "clear_weather_time"));
    }

    private static WorldClockSettings? LoadClocks(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadModernFile(worldPath, "world_clocks.dat");
        var data = file?.RootTag["data"] as NbtCompound;
        if (data == null) return LoadLegacyClocks(worldPath);

        var clocks = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var dimension in data.Tags.OfType<NbtCompound>())
            if (!string.IsNullOrEmpty(dimension.Name) && dimension["total_ticks"] is NbtLong ticks)
                clocks[dimension.Name] = ticks.Value;
        return new WorldClockSettings(clocks);
    }

    private static void SaveWeather(string worldPath, WorldWeatherSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadModernFile(worldPath, "weather.dat");
        if (file != null)
        {
            var data = file.RootTag["data"] as NbtCompound ?? throw new InvalidDataException(CommonLanguageManager.Instance.world_weatherMissingDataTag.CurrentValue());
            SetBool(data, "raining", settings.Raining);
            SetBool(data, "thundering", settings.Thundering);
            SetInt(data, "rain_time", settings.RainTime);
            SetInt(data, "thunder_time", settings.ThunderTime);
            SetInt(data, "clear_weather_time", settings.ClearWeatherTime);
            SaveModernFile(file, worldPath, "weather.dat");
            return;
        }

        SaveLegacyWeather(worldPath, settings);
    }

    private static void SaveClocks(string worldPath, WorldClockSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadModernFile(worldPath, "world_clocks.dat");
        if (file == null)
        {
            SaveLegacyClocks(worldPath, settings);
            return;
        }

        var data = file.RootTag["data"] as NbtCompound ?? throw new InvalidDataException(CommonLanguageManager.Instance.world_timeMissingDataTag.CurrentValue());
        foreach (var (dimension, totalTicks) in settings.TotalTicks)
            if (data[dimension] is NbtCompound clock && clock["total_ticks"] is NbtLong ticks)
                ticks.Value = totalTicks;
        SaveModernFile(file, worldPath, "world_clocks.dat");
    }

    private static NbtFile? LoadModernFile(string worldPath, string fileName)
    {
        var path = Path.Combine(worldPath, "data", "minecraft", fileName);
        if (!File.Exists(path)) return null;
        var file = new NbtFile();
        file.LoadFromFile(path);
        return file;
    }

    private static void SaveModernFile(NbtFile file, string worldPath, string fileName)
    {
        file.SaveToFile(Path.Combine(worldPath, "data", "minecraft", fileName), NbtCompression.None);
    }

    private static NbtCompound? LoadLegacyData(string worldPath)
    {
        var path = Path.Combine(worldPath, "level.dat");
        if (!File.Exists(path)) return null;
        var file = new NbtFile();
        file.LoadFromFile(path);
        return file.RootTag["Data"] as NbtCompound ?? file.RootTag;
    }

    private static WorldClockSettings? LoadLegacyClocks(string worldPath)
    {
        var data = LoadLegacyData(worldPath);
        return data == null
            ? null
            : new WorldClockSettings(new Dictionary<string, long>(StringComparer.Ordinal)
                { ["minecraft:overworld"] = GetLong(data, "DayTime") });
    }

    private static void SaveLegacyWeather(string worldPath, WorldWeatherSettings settings)
    {
        var path = Path.Combine(worldPath, "level.dat");
        var file = new NbtFile();
        file.LoadFromFile(path);
        var data = file.RootTag["Data"] as NbtCompound ?? file.RootTag;
        SetBool(data, "raining", settings.Raining);
        SetBool(data, "thundering", settings.Thundering);
        SetInt(data, "rainTime", settings.RainTime);
        SetInt(data, "thunderTime", settings.ThunderTime);
        SetInt(data, "clearWeatherTime", settings.ClearWeatherTime);
        file.SaveToFile(path, NbtCompression.GZip);
    }

    private static void SaveLegacyClocks(string worldPath, WorldClockSettings settings)
    {
        if (!settings.TotalTicks.TryGetValue("minecraft:overworld", out var ticks)) return;
        var path = Path.Combine(worldPath, "level.dat");
        var file = new NbtFile();
        file.LoadFromFile(path);
        var data = file.RootTag["Data"] as NbtCompound ?? file.RootTag;
        if (data["DayTime"] is NbtLong dayTime)
        {
            dayTime.Value = ticks;
        }
        else
        {
            if (data["DayTime"] != null) data.Remove("DayTime");
            data.Add(new NbtLong("DayTime", ticks));
        }

        file.SaveToFile(path, NbtCompression.GZip);
    }

    private static bool GetBool(NbtCompound data, string name)
    {
        return data[name] is NbtByte tag && tag.Value != 0;
    }

    private static int GetInt(NbtCompound data, string name)
    {
        return (data[name] as NbtInt)?.Value ?? 0;
    }

    private static long GetLong(NbtCompound data, string name)
    {
        return (data[name] as NbtLong)?.Value ?? 0;
    }

    private static void SetBool(NbtCompound data, string name, bool value)
    {
        if (data[name] is NbtByte tag) tag.Value = value ? (byte)1 : (byte)0;
    }

    private static void SetInt(NbtCompound data, string name, int value)
    {
        if (data[name] is NbtInt tag) tag.Value = value;
    }
}