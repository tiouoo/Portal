using System.Reflection;
using System.Text.Json;

namespace LitematicaViewer.Core.Helpers;

public static class CnTranslateHelper
{
    private static readonly Dictionary<string, string> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _cache = new();
    private static bool _loaded;

    public static string ToChinese(string id)
    {
        id = CleanId(id);
        if (_cache.TryGetValue(id, out var cached))
            return cached;

        EnsureLoaded();

        if (_blocks.TryGetValue(id, out var cn))
        {
            _cache[id] = cn;
            return cn;
        }

        _cache[id] = id;
        return id;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "LitematicaViewer.Core.Resources.setting.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return;

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        void LoadSection(string sectionName)
        {
            if (root.TryGetProperty(sectionName, out var section))
            {
                foreach (var prop in section.EnumerateObject())
                {
                    var val = prop.Value.GetString();
                    if (val != null && !_blocks.ContainsKey(prop.Name))
                        _blocks[prop.Name] = val;
                }
            }
        }

        LoadSection("Blocks");
        LoadSection("Items");
    }

    private static string CleanId(string id)
    {
        if (id.StartsWith("minecraft:"))
            id = id[10..];
        var bracketIndex = id.IndexOf('[');
        if (bracketIndex > 0)
            id = id[..bracketIndex];
        return id;
    }
}
