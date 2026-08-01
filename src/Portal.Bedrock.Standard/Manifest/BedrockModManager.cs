using System.Text.Json;
using System.Collections.Concurrent;

namespace Portal.Bedrock.Standard.Manifest;

public static class BedrockModManager
{
    public const int DefaultDelayMs = 5;
    public const int MaximumDelayMs = 200000;
    public static readonly string PortalConfigFolder = Path.Combine("config", "Portal");
    public static readonly string ModsFolderName = "mods";
    public static readonly string ConfigFileName = "mods.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, object> ConfigLocks = new(StringComparer.OrdinalIgnoreCase);

    public static string GetModsFolder(BedrockInstanceConfig config) =>
        Path.Combine(config.InstancePath, PortalConfigFolder, ModsFolderName);

    public static string GetConfigPath(BedrockInstanceConfig config) =>
        Path.Combine(config.InstancePath, PortalConfigFolder, ConfigFileName);

    public static IReadOnlyList<BedrockModInfo> Scan(BedrockInstanceConfig config)
    {
        EnsureBedrock(config);
        lock (GetLock(config))
        {
            var folder = GetModsFolder(config);
            Directory.CreateDirectory(folder);
            var manifest = LoadCore(config);
            var configured = manifest.Mods
                .Where(entry => entry != null && IsSafeFileName(entry.File))
                .GroupBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var result = new List<BedrockModInfo>();
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsX64Dll(path))
                    continue;

                var fileName = Path.GetFileName(path);
                if (!configured.TryGetValue(fileName, out var entry))
                {
                    entry = new BedrockModEntry { File = fileName, DelayMs = DefaultDelayMs };
                    configured.Add(fileName, entry);
                    manifest.Mods.Add(entry);
                }

                entry.DelayMs = Math.Clamp(entry.DelayMs, 0, MaximumDelayMs);
                result.Add(new BedrockModInfo(path, new FileInfo(path).Length, entry));
            }

            SaveCore(config, manifest);
            return result;
        }
    }

    public static BedrockModConfig Load(BedrockInstanceConfig config)
    {
        EnsureBedrock(config);
        lock (GetLock(config))
            return LoadCore(config);
    }

    private static BedrockModConfig LoadCore(BedrockInstanceConfig config)
    {
        var path = GetConfigPath(config);
        if (!File.Exists(path))
            return new BedrockModConfig();

        try
        {
            var result = JsonSerializer.Deserialize<BedrockModConfig>(File.ReadAllText(path), JsonOptions)
                         ?? new BedrockModConfig();
            result.Mods ??= [];
            result.Mods.RemoveAll(entry => entry == null);
            return result;
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", true);
            return new BedrockModConfig();
        }
    }

    public static void Save(BedrockInstanceConfig config, BedrockModConfig manifest)
    {
        EnsureBedrock(config);
        lock (GetLock(config))
            SaveCore(config, manifest);
    }

    private static void SaveCore(BedrockInstanceConfig config, BedrockModConfig manifest)
    {
        var path = GetConfigPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public static void Update(BedrockInstanceConfig config, string fileName, Action<BedrockModEntry> update)
    {
        EnsureBedrock(config);
        lock (GetLock(config))
        {
            var manifest = LoadCore(config);
            var entry = manifest.Mods.FirstOrDefault(item =>
                string.Equals(item.File, fileName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new BedrockModEntry { File = fileName, DelayMs = DefaultDelayMs };
                manifest.Mods.Add(entry);
            }

            update(entry);
            entry.DelayMs = Math.Clamp(entry.DelayMs, 0, MaximumDelayMs);
            SaveCore(config, manifest);
        }
    }

    public static bool IsX64Dll(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                return false;
            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset > stream.Length - 26)
                return false;
            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664)
                return false;
            var sectionCount = reader.ReadUInt16();
            stream.Position += 12;
            var optionalHeaderSize = reader.ReadUInt16();
            var characteristics = reader.ReadUInt16();
            if (sectionCount == 0 || sectionCount > 96 || optionalHeaderSize < 0xF0 ||
                peOffset + 24L + optionalHeaderSize + sectionCount * 40L > stream.Length ||
                (characteristics & 0x2000) == 0)
                return false;
            return reader.ReadUInt16() == 0x20B;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSafeFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) &&
        string.Equals(Path.GetExtension(fileName), ".dll", StringComparison.OrdinalIgnoreCase);

    private static object GetLock(BedrockInstanceConfig config) =>
        ConfigLocks.GetOrAdd(Path.GetFullPath(GetConfigPath(config)), static _ => new object());

    private static void EnsureBedrock(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
    }
}
