using fNbt;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Services;

public sealed class WorldEnvironmentService
{
    public Task<WorldWeatherSettings?> LoadWeatherAsync(string worldPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadWeather(worldPath, cancellationToken), cancellationToken);

    public Task<WorldClockSettings?> LoadClocksAsync(string worldPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadClocks(worldPath, cancellationToken), cancellationToken);

    public Task SaveWeatherAsync(string worldPath, WorldWeatherSettings settings, CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveWeather(worldPath, settings, cancellationToken), cancellationToken);

    public Task SaveClocksAsync(string worldPath, WorldClockSettings settings, CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveClocks(worldPath, settings, cancellationToken), cancellationToken);

    private static WorldWeatherSettings? LoadWeather(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadFile(worldPath, "weather.dat");
        var data = file?.RootTag["data"] as NbtCompound;
        return data == null ? null : new WorldWeatherSettings(GetBool(data, "raining"), GetBool(data, "thundering"),
            GetInt(data, "rain_time"), GetInt(data, "thunder_time"), GetInt(data, "clear_weather_time"));
    }

    private static WorldClockSettings? LoadClocks(string worldPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadFile(worldPath, "world_clocks.dat");
        var data = file?.RootTag["data"] as NbtCompound;
        if (data == null)
            return null;

        var clocks = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var dimension in data.Tags.OfType<NbtCompound>())
        {
            if (!string.IsNullOrEmpty(dimension.Name) && dimension["total_ticks"] is NbtLong ticks)
                clocks[dimension.Name] = ticks.Value;
        }
        return new WorldClockSettings(clocks);
    }

    private static void SaveWeather(string worldPath, WorldWeatherSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadFile(worldPath, "weather.dat") ?? throw new FileNotFoundException("未找到天气数据文件。");
        var data = file.RootTag["data"] as NbtCompound ?? throw new InvalidDataException("天气数据文件不包含 data 标签。");
        SetBool(data, "raining", settings.Raining);
        SetBool(data, "thundering", settings.Thundering);
        SetInt(data, "rain_time", settings.RainTime);
        SetInt(data, "thunder_time", settings.ThunderTime);
        SetInt(data, "clear_weather_time", settings.ClearWeatherTime);
        SaveFile(file, worldPath, "weather.dat");
    }

    private static void SaveClocks(string worldPath, WorldClockSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = LoadFile(worldPath, "world_clocks.dat") ?? throw new FileNotFoundException("未找到世界时钟数据文件。");
        var data = file.RootTag["data"] as NbtCompound ?? throw new InvalidDataException("世界时钟数据文件不包含 data 标签。");
        foreach (var (dimension, totalTicks) in settings.TotalTicks)
            if (data[dimension] is NbtCompound clock && clock["total_ticks"] is NbtLong ticks)
                ticks.Value = totalTicks;
        SaveFile(file, worldPath, "world_clocks.dat");
    }

    private static NbtFile? LoadFile(string worldPath, string fileName)
    {
        var path = Path.Combine(worldPath, "data", "minecraft", fileName);
        if (!File.Exists(path)) return null;
        var file = new NbtFile();
        file.LoadFromFile(path);
        return file;
    }

    private static void SaveFile(NbtFile file, string worldPath, string fileName) =>
        file.SaveToFile(Path.Combine(worldPath, "data", "minecraft", fileName), NbtCompression.None);
    private static bool GetBool(NbtCompound data, string name) => data[name] is NbtByte tag && tag.Value != 0;
    private static int GetInt(NbtCompound data, string name) => (data[name] as NbtInt)?.Value ?? 0;
    private static void SetBool(NbtCompound data, string name, bool value)
    {
        if (data[name] is NbtByte tag) tag.Value = value ? (byte)1 : (byte)0;
    }
    private static void SetInt(NbtCompound data, string name, int value)
    {
        if (data[name] is NbtInt tag) tag.Value = value;
    }
}
