using LitematicaViewer.Core.Models;

namespace LitematicaViewer.Core.Services;

public class StructureGenerator
{
    public LitematicFile CreateCube(
        string blockId,
        int width,
        int height,
        int length,
        bool hollow = false,
        int wallThickness = 1,
        int offsetX = 0,
        int offsetY = 0,
        int offsetZ = 0)
    {
        blockId = NormalizeBlockId(blockId);

        var region = new Region
        {
            Name = "Generated",
            MinX = offsetX,
            MinY = offsetY,
            MinZ = offsetZ,
            MaxX = offsetX + width - 1,
            MaxY = offsetY + height - 1,
            MaxZ = offsetZ + length - 1
        };

        region.Palette[0] = new BlockState { BlockId = "minecraft:air" };
        region.Palette[1] = new BlockState { BlockId = blockId };

        var totalBlocks = (long)width * height * length;
        var bitsPerBlock = 2;
        region.BitsPerBlock = bitsPerBlock;
        var blocksPerLong = 64 / bitsPerBlock;
        var longCount = (int)((totalBlocks + blocksPerLong - 1) / blocksPerLong);
        var rawLongs = new long[longCount];

        var blockIdx = 0L;
        for (var y = 0; y < height; y++)
        {
            for (var z = 0; z < length; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var shouldPlace = !hollow || IsOnFace(x, y, z, width, height, length, wallThickness);
                    var paletteIndex = shouldPlace ? 1L : 0L;
                    var longIdx = (int)(blockIdx / blocksPerLong);
                    var bitOff = (int)(blockIdx % blocksPerLong) * bitsPerBlock;
                    rawLongs[longIdx] |= paletteIndex << bitOff;
                    blockIdx++;
                }
            }
        }

        region.RawLongs = rawLongs;

        var nonAirCount = hollow
            ? (long)width * height * length - ((long)(width - 2 * wallThickness) * (height - 2 * wallThickness) * (length - 2 * wallThickness))
            : (long)width * height * length;
        if (!hollow) nonAirCount = totalBlocks;

        return new LitematicFile
        {
            Version = 7,
            MinecraftDataVersion = 3955,
            Name = "Generated_Cube",
            Author = "LitematicaViewer",
            Description = $"Generated {width}x{height}x{length} cube",
            TotalBlocks = nonAirCount,
            TotalVolume = totalBlocks,
            EnclosingSizeX = width,
            EnclosingSizeY = height,
            EnclosingSizeZ = length,
            Regions = new List<Region> { region }
        };
    }

    public LitematicFile CreateSolid(int width, int height, int length, string blockId = "minecraft:stone",
        int ox = 0, int oy = 0, int oz = 0)
    {
        return CreateCube(blockId, width, height, length, false, 0, ox, oy, oz);
    }

    public LitematicFile CreateHollow(int width, int height, int length, string blockId = "minecraft:stone",
        int wallThickness = 1, int ox = 0, int oy = 0, int oz = 0)
    {
        return CreateCube(blockId, width, height, length, true, wallThickness, ox, oy, oz);
    }

    private static bool IsOnFace(int x, int y, int z, int w, int h, int l, int thickness)
    {
        return x < thickness || x >= w - thickness
            || y < thickness || y >= h - thickness
            || z < thickness || z >= l - thickness;
    }

    private static string NormalizeBlockId(string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            return "minecraft:stone";
        return blockId.Contains(':') ? blockId : $"minecraft:{blockId}";
    }
}
