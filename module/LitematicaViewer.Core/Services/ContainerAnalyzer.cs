using fNbt;
using LitematicaViewer.Core.Models;

namespace LitematicaViewer.Core.Services;

public class ContainerAnalyzer
{
    private static readonly HashSet<string> ContainerBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft:chest", "minecraft:trapped_chest", "minecraft:barrel",
        "minecraft:dispenser", "minecraft:dropper", "minecraft:hopper",
        "minecraft:furnace", "minecraft:blast_furnace", "minecraft:smoker",
        "minecraft:brewing_stand", "minecraft:lectern",
        "minecraft:shulker_box", "minecraft:white_shulker_box",
        "minecraft:orange_shulker_box", "minecraft:magenta_shulker_box",
        "minecraft:light_blue_shulker_box", "minecraft:yellow_shulker_box",
        "minecraft:lime_shulker_box", "minecraft:pink_shulker_box",
        "minecraft:gray_shulker_box", "minecraft:light_gray_shulker_box",
        "minecraft:cyan_shulker_box", "minecraft:purple_shulker_box",
        "minecraft:blue_shulker_box", "minecraft:brown_shulker_box",
        "minecraft:green_shulker_box", "minecraft:red_shulker_box",
        "minecraft:black_shulker_box", "minecraft:chiseled_bookshelf"
    };

    public List<ContainerData> Analyze(LitematicFile file, string litematicFilePath)
    {
        var containers = FindContainers(file);
        var nbtFile = new NbtFile();
        nbtFile.LoadFromFile(litematicFilePath);
        FillContainerItems(nbtFile.RootTag, file, containers);
        return containers;
    }

    public List<ContainerData> FindContainers(LitematicFile file)
    {
        var containers = new List<ContainerData>();

        foreach (var region in file.Regions)
        {
            for (var x = region.MinX; x <= region.MaxX; x++)
            {
                for (var y = region.MinY; y <= region.MaxY; y++)
                {
                    for (var z = region.MinZ; z <= region.MaxZ; z++)
                    {
                        var blockState = region.GetBlock(x, y, z);
                        if (blockState == null) continue;
                        if (!ContainerBlocks.Contains(blockState.BlockId)) continue;

                        containers.Add(new ContainerData
                        {
                            X = x, Y = y, Z = z,
                            ContainerBlockId = blockState.BlockId,
                            ContainerProperties = blockState.Properties
                        });
                    }
                }
            }
        }

        return containers;
    }

    private static void FillContainerItems(NbtCompound root, LitematicFile file, List<ContainerData> containers)
    {
        if (root["Regions"] is not NbtCompound regions)
            return;

        var tileEntityMap = new Dictionary<(int, int, int), List<ContainerItem>>();
        var regionPositions = file.Regions.ToDictionary(r => r.Name, r => (r.MinX, r.MinY, r.MinZ));

        foreach (var regionTag in regions)
        {
            if (regionTag is not NbtCompound regionCompound)
                continue;

            if (regionCompound["TileEntities"] is not NbtList tileEntities)
                continue;

            var regionName = regionTag.Name ?? "";
            if (!regionPositions.TryGetValue(regionName, out var regionPos))
                continue;

            foreach (var te in tileEntities)
            {
                if (te is not NbtCompound teCompound) continue;

                var localX = GetInt(teCompound, "x");
                var localY = GetInt(teCompound, "y");
                var localZ = GetInt(teCompound, "z");

                var worldX = regionPos.Item1 + localX;
                var worldY = regionPos.Item2 + localY;
                var worldZ = regionPos.Item3 + localZ;

                if (teCompound["Items"] is not NbtList items)
                    continue;

                var itemList = new List<ContainerItem>();
                foreach (var item in items)
                {
                    if (item is not NbtCompound itemCompound) continue;

                    var itemId = GetString(itemCompound, "id");
                    var slot = GetByte(itemCompound, "Slot");
                    var count = GetItemCount(itemCompound);

                    if (itemId != "minecraft:air" && itemId != "" && count > 0)
                    {
                        itemList.Add(new ContainerItem
                        {
                            ItemId = itemId,
                            Slot = slot,
                            Count = count
                        });
                    }
                }

                tileEntityMap[(worldX, worldY, worldZ)] = itemList;
            }
        }

        foreach (var container in containers)
        {
            if (tileEntityMap.TryGetValue((container.X, container.Y, container.Z), out var items))
            {
                container.Items = items;
            }
        }
    }

    private static int GetItemCount(NbtCompound itemCompound)
    {
        if (!itemCompound.TryGet("Count", out var tag)) return 0;
        if (tag is NbtByte bTag) return bTag.Value;
        if (tag is NbtShort sTag) return sTag.Value;
        if (tag is NbtInt iTag) return iTag.Value;
        return 0;
    }

    private static int GetInt(NbtCompound compound, string key)
    {
        if (!compound.TryGet(key, out var tag)) return 0;
        if (tag is NbtInt iTag) return iTag.Value;
        return 0;
    }

    private static byte GetByte(NbtCompound compound, string key)
    {
        if (!compound.TryGet(key, out var tag)) return 0;
        if (tag is NbtByte bTag) return bTag.Value;
        return 0;
    }

    private static string GetString(NbtCompound compound, string key)
    {
        if (!compound.TryGet(key, out var tag)) return "";
        if (tag is NbtString sTag) return sTag.Value;
        return "";
    }
}
