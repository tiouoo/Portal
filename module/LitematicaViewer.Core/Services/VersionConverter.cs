using LitematicaViewer.Core.Enums;
using LitematicaViewer.Core.Models;

namespace LitematicaViewer.Core.Services;

public class VersionConverter
{
    public LitematicFile Convert(LitematicFile file, LitematicVersion targetVersion)
    {
        file.Version = (int)targetVersion;

        switch (targetVersion)
        {
            case LitematicVersion.V3:
                file.MinecraftDataVersion = 1631;
                break;
            case LitematicVersion.V6:
                file.MinecraftDataVersion = 2860;
                break;
            case LitematicVersion.V7:
                file.MinecraftDataVersion = 3955;
                break;
        }

        foreach (var region in file.Regions)
        {
            ConvertPalette(region, targetVersion);
            ConvertBlockStates(region, targetVersion);
        }

        return file;
    }

    private static void ConvertPalette(Region region, LitematicVersion targetVersion)
    {
        var newPalette = new Dictionary<int, BlockState>();
        var remap = new Dictionary<int, int>();

        foreach (var (index, blockState) in region.Palette)
        {
            blockState.BlockId = ConvertBlockId(blockState.BlockId, targetVersion);
            newPalette[index] = blockState;
            remap[index] = index;
        }

        region.Palette = newPalette;
    }

    private static void ConvertBlockStates(Region region, LitematicVersion targetVersion)
    {
    }

    private static string ConvertBlockId(string blockId, LitematicVersion targetVersion)
    {
        if (targetVersion == LitematicVersion.V3)
        {
            return blockId switch
            {
                "minecraft:grass_block" => "minecraft:grass",
                "minecraft:netherite_block" => "minecraft:stone",
                "minecraft:ancient_debris" => "minecraft:stone",
                "minecraft:crying_obsidian" => "minecraft:obsidian",
                "minecraft:lodestone" => "minecraft:stone",
                "minecraft:respawn_anchor" => "minecraft:obsidian",
                "minecraft:shroomlight" => "minecraft:glowstone",
                "minecraft:soul_lantern" => "minecraft:lantern",
                "minecraft:soul_torch" => "minecraft:torch",
                "minecraft:soul_campfire" => "minecraft:campfire",
                "minecraft:chain" => "minecraft:iron_bars",
                _ => blockId
            };
        }

        return blockId;
    }
}
