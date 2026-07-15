namespace LitematicaViewer.Core.Models;

public class Region
{
    public string Name { get; set; } = string.Empty;
    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }
    public int SizeX => MaxX - MinX + 1;
    public int SizeY => MaxY - MinY + 1;
    public int SizeZ => MaxZ - MinZ + 1;

    public Dictionary<int, BlockState> Palette { get; set; } = new();
    public long[] RawLongs { get; set; } = Array.Empty<long>();
    public int BitsPerBlock { get; set; } = 1;

    public long TotalBlocksLong => (long)SizeX * SizeY * SizeZ;

    public long GetBlockIndex(int x, int y, int z)
    {
        x -= MinX;
        y -= MinY;
        z -= MinZ;
        return x + (long)z * SizeX + (long)y * SizeX * SizeZ;
    }

    public BlockState? GetBlock(int x, int y, int z)
    {
        var index = GetBlockIndex(x, y, z);
        if (index < 0 || index >= TotalBlocksLong) return null;

        var paletteIndex = DecodePaletteIndex(index);
        if (paletteIndex < 0) return null;

        return Palette.TryGetValue(paletteIndex, out var state) ? state : null;
    }

    public int GetPaletteIndexAt(int x, int y, int z)
    {
        var index = GetBlockIndex(x, y, z);
        return DecodePaletteIndex(index);
    }

    private int DecodePaletteIndex(long blockIndex)
    {
        if (RawLongs.Length == 0) return -1;
        var blocksPerLong = 64 / BitsPerBlock;
        var longIdx = (int)(blockIndex / blocksPerLong);
        if (longIdx >= RawLongs.Length) return -1;
        var bitOffset = (int)(blockIndex % blocksPerLong) * BitsPerBlock;
        var mask = (1L << BitsPerBlock) - 1;
        return (int)((RawLongs[longIdx] >> bitOffset) & mask);
    }
}
