using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Portal.Bedrock.Preload;

/// <summary>读取 &lt;exe&gt;/config/Portal/config.json 并暴露各分节配置项。</summary>
internal sealed class ConfigManager
{
    private readonly Dictionary<string, Dictionary<string, string>> _objects = new(StringComparer.Ordinal);

    public ConfigManager()
    {
        string path = Path.Combine(ExeDirectory, "config", "Portal", "config.json");
        if (!File.Exists(path))
        {
            Logger.Error($"Config file missing: {path}");
            LoadDefaults();
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonProperty obj in document.RootElement.EnumerateObject())
            {
                if (obj.Value.ValueKind is not JsonValueKind.Object)
                    continue;

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty property in obj.Value.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind is JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
                _objects[obj.Name] = values;
            }

            Logger.Info($"Config loaded, objects: {_objects.Count}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to parse config JSON, using defaults: {ex.Message}");
            LoadDefaults();
        }
    }

    private static string ExeDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? ".";

    private void LoadDefaults()
    {
        _objects.Clear();
        _objects["config"] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["isConsole"] = "true",
            ["isVersionIsolated"] = "true",
            ["isDetailedLog"] = "false",
        };
    }

    public string Get(string section, string key) =>
        _objects.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
            ? value
            : string.Empty;

    public string GetConfig(string key) => Get("config", key);
    public string GetInfo(string key) => Get("info", key);

    public bool GetConfigBool(string key) => GetConfig(key).ToLowerInvariant() is "true" or "1";
    public int GetConfigInt(string key) => int.TryParse(GetConfig(key), out int value) ? value : 0;
    public int GetInfoInt(string key) => int.TryParse(GetInfo(key), out int value) ? value : 0;
}
