using LitematicaViewer.Core.Models;

namespace LitematicaViewer.Core.Services;

public class BlockReplacer
{
    public int Replace(
        LitematicFile file,
        string fromBlockId,
        string toBlockId,
        Dictionary<string, string>? matchProperties = null,
        Dictionary<string, string>? setProperties = null)
    {
        return Replace(file, new[] { (fromBlockId, toBlockId, matchProperties, setProperties) });
    }

    public int Replace(
        LitematicFile file,
        (string From, string To, Dictionary<string, string>? Match, Dictionary<string, string>? Set)[] replacements)
    {
        var replaced = 0;
        var replacementMap = new Dictionary<string, (string To, Dictionary<string, string>? Match, Dictionary<string, string>? Set)>();

        foreach (var (from, to, match, set) in replacements)
        {
            replacementMap[NormalizeId(from)] = (NormalizeId(to), match, set);
        }

        foreach (var region in file.Regions)
        {
            replaced += ReplaceInRegion(region, replacementMap);
        }

        return replaced;
    }

    private static int ReplaceInRegion(Region region,
        Dictionary<string, (string To, Dictionary<string, string>? Match, Dictionary<string, string>? Set)> map)
    {
        var replacedIndices = new Dictionary<int, BlockState>();
        var indexCounts = new Dictionary<int, int>();

        foreach (var (paletteIndex, blockState) in region.Palette)
        {
            if (!map.TryGetValue(blockState.BlockId, out var replacement))
                continue;

            var (to, match, set) = replacement;

            if (match != null && match.Count > 0)
            {
                var allMatch = match.All(kv =>
                    blockState.Properties.TryGetValue(kv.Key, out var val) && val == kv.Value);
                if (!allMatch) continue;
            }

            var newState = new BlockState { BlockId = to };
            foreach (var (key, value) in blockState.Properties)
            {
                newState.Properties[key] = value;
            }

            if (set != null)
            {
                foreach (var (key, value) in set)
                {
                    newState.Properties[key] = value;
                }
            }

            replacedIndices[paletteIndex] = newState;
        }

        if (region.RawLongs.Length > 0)
        {
            var blocksPerLong = 64 / region.BitsPerBlock;
            var mask = (1L << region.BitsPerBlock) - 1;
            var totalBlocks = region.TotalBlocksLong;
            var blockIdx = 0L;
            foreach (var packed in region.RawLongs)
            {
                for (var j = 0; j < blocksPerLong && blockIdx < totalBlocks; j++)
                {
                    var idx = (int)((packed >> (j * region.BitsPerBlock)) & mask);
                    if (replacedIndices.ContainsKey(idx))
                    {
                        indexCounts.TryGetValue(idx, out var cnt);
                        indexCounts[idx] = cnt + 1;
                    }
                    blockIdx++;
                }
            }
        }

        foreach (var (index, newState) in replacedIndices)
        {
            region.Palette[index] = newState;
        }

        return indexCounts.Values.Sum();
    }

    public int ReplaceByCategory(LitematicFile file, string categoryKeyword, string toBlockId)
    {
        var replaced = 0;
        toBlockId = NormalizeId(toBlockId);

        foreach (var region in file.Regions)
        {
            var replacedIndices = new Dictionary<int, BlockState>();

            foreach (var (index, blockState) in region.Palette)
            {
                if (blockState.BlockId.Contains(categoryKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    var newState = new BlockState { BlockId = toBlockId };
                    foreach (var (key, value) in blockState.Properties)
                    {
                        newState.Properties[key] = value;
                    }
                    replacedIndices[index] = newState;
                }
            }

            foreach (var (index, newState) in replacedIndices)
            {
                region.Palette[index] = newState;
            }

            if (region.RawLongs.Length > 0)
            {
                var blocksPerLong = 64 / region.BitsPerBlock;
                var mask = (1L << region.BitsPerBlock) - 1;
                var totalBlocks = region.TotalBlocksLong;
                var blockIdx = 0L;
                foreach (var packed in region.RawLongs)
                {
                    for (var j = 0; j < blocksPerLong && blockIdx < totalBlocks; j++)
                    {
                        var idx = (int)((packed >> (j * region.BitsPerBlock)) & mask);
                        if (replacedIndices.ContainsKey(idx))
                            replaced++;
                        blockIdx++;
                    }
                }
            }
        }

        return replaced;
    }

    private static string NormalizeId(string id)
    {
        return id.Contains(':') ? id : $"minecraft:{id}";
    }
}
