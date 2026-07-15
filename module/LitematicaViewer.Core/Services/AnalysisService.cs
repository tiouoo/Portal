using LitematicaViewer.Core.Enums;
using LitematicaViewer.Core.Helpers;
using LitematicaViewer.Core.Models;

namespace LitematicaViewer.Core.Services;

public class AnalysisResult
{
    public Dictionary<string, long> BlockCounts { get; set; } = new();
    public Dictionary<BlockCategory, List<(long Count, string BlockId)>> Categories { get; set; } = new();
    public long TotalBlocks { get; set; }
}

public class AnalysisProgress
{
    public long Processed { get; set; }
    public long Total { get; set; }
    public double Percent { get; set; }
}

public class AnalysisService
{
    public AnalysisResult Analyze(LitematicFile file, IProgress<AnalysisProgress>? progress = null)
    {
        return AnalyzeInternal(file, progress);
    }

    private static AnalysisResult AnalyzeInternal(LitematicFile file, IProgress<AnalysisProgress>? progress)
    {
        var result = new AnalysisResult();
        long totalBlocks = 0;

        foreach (var region in file.Regions)
        {
            totalBlocks += AnalyzeRegion(region, result, progress);
        }

        result.TotalBlocks = totalBlocks;

        foreach (var (blockId, count) in result.BlockCounts)
        {
            var category = BlockCategoryHelper.Classify(blockId);
            if (!result.Categories.ContainsKey(category))
                result.Categories[category] = new List<(long, string)>();
            result.Categories[category].Add((count, blockId));
        }

        foreach (var cat in result.Categories.Keys.ToList())
        {
            result.Categories[cat] = result.Categories[cat]
                .OrderByDescending(x => x.Count)
                .ToList();
        }

        return result;
    }

    private static long AnalyzeRegion(Region region, AnalysisResult result, IProgress<AnalysisProgress>? progress)
    {
        if (region.RawLongs.Length == 0) return 0;

        var mask = (1L << region.BitsPerBlock) - 1;
        var blocksPerLong = 64 / region.BitsPerBlock;
        var totalBlocks = region.TotalBlocksLong;

        var paletteSize = Math.Max(region.Palette.Count, 1);
        var isAir = new bool[paletteSize];
        var isExcluded = new bool[paletteSize];
        var effectiveIds = new string[paletteSize];
        var multipliers = new long[paletteSize];
        var hasWaterlogged = new bool[paletteSize];

        for (var i = 0; i < paletteSize; i++)
        {
            if (region.Palette.TryGetValue(i, out var bs))
            {
                var bid = bs.BlockId;
                isAir[i] = bid is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air";
                isExcluded[i] = !isAir[i] && (bid is "minecraft:piston_head"
                    or "minecraft:nether_portal" or "minecraft:moving_piston" or "minecraft:bedrock");
                effectiveIds[i] = isAir[i] ? "" : GetEffectiveBlockId(bid, bs.Properties);
                multipliers[i] = GetEffectiveCount(bid, bs.Properties);
                hasWaterlogged[i] = bs.Properties.TryGetValue("waterlogged", out var wl) && wl == "true";
            }
            else
            {
                isAir[i] = true;
                effectiveIds[i] = "";
            }
        }

        var frequencies = new long[paletteSize];
        var longCount = region.RawLongs.Length;
        var blockIdx = 0L;
        var reportInterval = Math.Max(longCount / 20, 1);

        for (var li = 0; li < longCount; li++)
        {
            var packed = region.RawLongs[li];
            for (var j = 0; j < blocksPerLong && blockIdx < totalBlocks; j++)
            {
                var idx = (int)((packed >> (j * region.BitsPerBlock)) & mask);
                if (idx < paletteSize)
                    frequencies[idx]++;
                blockIdx++;
            }

            if (progress != null && li % reportInterval == 0)
            {
                var pct = (double)li / longCount * 100;
                progress.Report(new AnalysisProgress { Processed = li, Total = longCount, Percent = pct });
            }
        }

        long nonAir = 0;
        for (var i = 0; i < paletteSize; i++)
        {
            var freq = frequencies[i];
            if (freq == 0 || isAir[i]) continue;

            nonAir += freq * multipliers[i];

            if (!isExcluded[i])
            {
                var effectiveId = effectiveIds[i];
                result.BlockCounts.TryGetValue(effectiveId, out var ex);
                result.BlockCounts[effectiveId] = ex + freq * multipliers[i];

                if (hasWaterlogged[i])
                {
                    result.BlockCounts.TryGetValue("minecraft:water", out var wc);
                    result.BlockCounts["minecraft:water"] = wc + freq;
                }
            }
        }

        progress?.Report(new AnalysisProgress { Processed = longCount, Total = longCount, Percent = 100 });

        return nonAir;
    }

    private static string GetEffectiveBlockId(string blockId, Dictionary<string, string> properties)
    {
        var simplified = blockId switch
        {
            "minecraft:farmland" => "minecraft:dirt",
            "minecraft:dirt_path" => "minecraft:dirt",
            "minecraft:bubble_column" => "minecraft:water",
            "minecraft:soul_fire" => "minecraft:fire",
            _ => blockId
        };

        if (simplified.Contains("potted_"))
            simplified = simplified.Replace("potted_", "");
        if (simplified.Contains("_cake"))
            simplified = simplified.Replace("_cake", "");
        if (simplified.Contains("wall_"))
            simplified = simplified.Replace("wall_", "");
        if (simplified.Contains("_cauldron"))
            simplified = simplified.Replace("_cauldron", "");

        if (properties.TryGetValue("type", out var type) && type == "double")
            return simplified;
        if (properties.TryGetValue("half", out var half) && half == "upper")
            return simplified;
        if (properties.TryGetValue("part", out var part) && part == "head")
            return simplified;

        return simplified;
    }

    private static long GetEffectiveCount(string blockId, Dictionary<string, string> properties)
    {
        if (properties.TryGetValue("eggs", out var eggs) && int.TryParse(eggs, out var eggCount) && eggCount > 0)
            return eggCount;
        if (properties.TryGetValue("pickles", out var pickles) && int.TryParse(pickles, out var pickleCount) && pickleCount > 0)
            return pickleCount;
        if (properties.TryGetValue("charges", out var charges) && int.TryParse(charges, out var chargeCount) && chargeCount > 0)
            return chargeCount;
        if (properties.TryGetValue("flower_amount", out var flowers) && int.TryParse(flowers, out var flowerCount) && flowerCount > 0)
            return flowerCount;
        return 1;
    }

    public Dictionary<string, long> GetSortedBlockCounts(AnalysisResult result)
    {
        return result.BlockCounts
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
