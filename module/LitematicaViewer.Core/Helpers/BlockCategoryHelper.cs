using System.Reflection;
using System.Text.Json;
using LitematicaViewer.Core.Enums;

namespace LitematicaViewer.Core.Helpers;

public static class BlockCategoryHelper
{
    private static Dictionary<string, List<string>>? _categoryRules;
    private static readonly Dictionary<string, BlockCategory> _cache = new();

    public static BlockCategory Classify(string blockId)
    {
        blockId = CleanBlockId(blockId);

        if (_cache.TryGetValue(blockId, out var cached))
            return cached;

        EnsureLoaded();

        foreach (var (category, keywords) in _categoryRules!)
        {
            foreach (var part in blockId.Split('_'))
            {
                foreach (var keyword in keywords)
                {
                    if (part.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse<BlockCategory>(category, out var parsed))
                        {
                            _cache[blockId] = parsed;
                            return _cache[blockId];
                        }
                    }
                }
            }
        }

        _cache[blockId] = BlockCategory.Other;
        return BlockCategory.Other;
    }

    private static void EnsureLoaded()
    {
        if (_categoryRules != null) return;

        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "LitematicaViewer.Core.Resources.categories.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _categoryRules = new Dictionary<string, List<string>>();
            return;
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        _categoryRules = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                         ?? new Dictionary<string, List<string>>();
    }

    private static string CleanBlockId(string blockId)
    {
        if (blockId.StartsWith("minecraft:"))
            blockId = blockId[10..];
        var bracketIndex = blockId.IndexOf('[');
        if (bracketIndex > 0)
            blockId = blockId[..bracketIndex];
        return blockId;
    }
}
