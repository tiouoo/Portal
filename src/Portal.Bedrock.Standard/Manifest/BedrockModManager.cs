using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Portal.Localization;

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

    public static string GetInstanceModsFolder(BedrockInstanceConfig config) =>
        Path.Combine(config.InstancePath, ModsFolderName);

    public static string GetConfigPath(BedrockInstanceConfig config) =>
        Path.Combine(config.InstancePath, PortalConfigFolder, ConfigFileName);

    public static IReadOnlyList<BedrockModInfo> Scan(BedrockInstanceConfig config)
    {
        EnsureBedrock(config);
        lock (GetLock(config))
        {
            var folder = GetModsFolder(config);
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_scanningDllMods.CurrentValue(), folder));
            Directory.CreateDirectory(folder);
            var manifest = LoadCore(config);
            var configured = manifest.Mods
                .Where(entry => entry != null && IsSafeConfigKey(entry.File))
                .GroupBy(entry => NormalizeConfigKey(entry.File), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var result = new List<BedrockModInfo>();
            var candidates = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(IsDll)
                .Select(path => (Path: path, Key: NormalizeConfigKey(Path.GetRelativePath(folder, path))))
                .ToList();

            var instanceModsFolder = GetInstanceModsFolder(config);
            if (Directory.Exists(instanceModsFolder))
            {
                var packageRoots = ScanPackageManifests(instanceModsFolder).PackageRoots;
                candidates.AddRange(Directory.EnumerateFiles(instanceModsFolder, "*", SearchOption.AllDirectories)
                    .Where(IsDll)
                    .Where(path => packageRoots.All(root => !IsPathWithin(path, root)))
                    .Select(path => (Path: path,
                        Key: NormalizeConfigKey(Path.GetRelativePath(config.InstancePath, path)))));
            }

            foreach (var candidate in candidates.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var path = candidate.Path;
                if (!IsX64Dll(path))
                    continue;

                if (!configured.TryGetValue(candidate.Key, out var entry))
                {
                    entry = new BedrockModEntry { File = candidate.Key, DelayMs = DefaultDelayMs };
                    configured.Add(candidate.Key, entry);
                    manifest.Mods.Add(entry);
                }
                else
                {
                    entry.File = candidate.Key;
                }

                entry.DelayMs = Math.Clamp(entry.DelayMs, 0, MaximumDelayMs);
                result.Add(new BedrockModInfo(path, new FileInfo(path).Length, entry));
            }

            SaveCore(config, manifest);
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_dllModsScanComplete.CurrentValue(), folder, result.Count));
            return result;
        }
    }

    /// <summary>Scans LeviLamina package mods stored in the instance mods directory.</summary>
    public static IReadOnlyList<BedrockModInfo> ScanPackages(BedrockInstanceConfig config)
    {
        EnsureBedrock(config);
        var root = GetInstanceModsFolder(config);
        if (!Directory.Exists(root)) return [];
        return ScanPackageManifests(root).Packages;
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
            Trace.TraceWarning(string.Format(LogLanguageManager.Instance.bedrock_dllModsConfigInvalidBackingUp.CurrentValue(), path));
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
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_writingDllModsConfig.CurrentValue(), path));
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public static void Update(BedrockInstanceConfig config, string fileName, Action<BedrockModEntry> update)
    {
        EnsureBedrock(config);
        var key = NormalizeConfigKey(fileName);
        if (!IsSafeConfigKey(key))
            throw new ArgumentException("Invalid Bedrock mod configuration key.", nameof(fileName));
        lock (GetLock(config))
        {
            var manifest = LoadCore(config);
            var entry = manifest.Mods.FirstOrDefault(item =>
                IsSafeConfigKey(item.File) &&
                string.Equals(NormalizeConfigKey(item.File), key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new BedrockModEntry { File = key, DelayMs = DefaultDelayMs };
                manifest.Mods.Add(entry);
            }

            entry.File = key;
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
        catch (IOException exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_readDllModsFileFailed.CurrentValue(), path, Environment.NewLine, exception));
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_readDllModsDenied.CurrentValue(), path, Environment.NewLine, exception));
            return false;
        }
    }

    private static PackageScanResult ScanPackageManifests(string root)
    {
        var packageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<BedrockModInfo>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            var packageRoot = Path.GetFullPath(Path.GetDirectoryName(manifestPath)!);
            packageRoots.Add(packageRoot);
            try
            {
                var manifest = JsonSerializer.Deserialize<BedrockPackageManifest>(
                    File.ReadAllText(manifestPath), JsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Entry)) continue;
                var relativeEntry = manifest.Entry.Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var entryPath = Path.GetFullPath(Path.Combine(packageRoot, relativeEntry));
                if (!IsPathWithin(entryPath, packageRoot) || !File.Exists(entryPath) || !IsX64Dll(entryPath))
                    continue;
                packages.Add(new BedrockModInfo(entryPath, new FileInfo(entryPath).Length,
                    new BedrockModEntry { File = Path.GetFileName(entryPath) }, true,
                    manifest.Name, manifest.Version, manifest.Description,
                    PathsEqual(packageRoot, root) ? null : packageRoot));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                                  or ArgumentException or NotSupportedException)
            {
                Trace.TraceWarning($"Unable to read Bedrock mod manifest {manifestPath}: {exception.Message}");
            }
        }

        return new PackageScanResult(
            packages.OrderBy(item => item.PackageName ?? item.FileName, StringComparer.OrdinalIgnoreCase).ToArray(),
            packageRoots.ToArray());
    }

    private static bool IsDll(string path) =>
        string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeConfigKey(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName) || !IsDll(fileName)) return false;
        var parts = NormalizeConfigKey(fileName).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part => part is not "." and not "..");
    }

    private static string NormalizeConfigKey(string fileName) =>
        fileName.Replace('\\', '/').TrimStart('/');

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class BedrockPackageManifest
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("entry")] public string? Entry { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed record PackageScanResult(
        IReadOnlyList<BedrockModInfo> Packages,
        IReadOnlyList<string> PackageRoots);

    private static object GetLock(BedrockInstanceConfig config) =>
        ConfigLocks.GetOrAdd(Path.GetFullPath(GetConfigPath(config)), static _ => new object());

    private static void EnsureBedrock(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
    }
}
