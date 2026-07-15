using fNbt;
using LitematicaViewer.Core.Models;

namespace LitematicaViewer.Core.Parsers;

public class LitematicParser
{
    public LitematicFile Load(string filePath)
    {
        try
        {
            var nbt = new NbtFile();
            nbt.LoadFromFile(filePath);
            return Parse(nbt.RootTag, filePath);
        }
        catch (NbtFormatException nfe)
        {
            throw new LitematicParseException(
                $"NBT format error while parsing '{filePath}': {nfe.Message}", filePath, 0, 0, nfe);
        }
        catch (System.IO.InvalidDataException ide)
        {
            throw new LitematicParseException(
                $"Invalid data while reading '{filePath}': {ide.Message}", filePath, 0, 0, ide);
        }
        catch (Exception ex) when (ex is not LitematicParseException)
        {
            throw new LitematicParseException(
                $"Failed to load litematic file '{filePath}': {ex.GetType().Name}: {ex.Message}",
                filePath, 0, 0, ex);
        }
    }

    public LitematicFile LoadFromBytes(byte[] data)
    {
        try
        {
            var nbt = new NbtFile();
            nbt.LoadFromBuffer(data, 0, data.Length, NbtCompression.GZip, null);
            return Parse(nbt.RootTag, "<memory buffer>");
        }
        catch (NbtFormatException nfe)
        {
            throw new LitematicParseException(
                $"NBT format error parsing byte buffer: {nfe.Message}", "<memory buffer>", 0, 0, nfe);
        }
    }

    public void Save(LitematicFile file, string path)
    {
        var root = ToNbt(file);
        var nbt = new NbtFile(root);
        nbt.SaveToFile(path, NbtCompression.GZip);
    }

    private static LitematicFile Parse(NbtCompound root, string sourcePath)
    {
        try
        {
            var file = new LitematicFile
            {
                Version = GetInt(root, "Version", 7),
                MinecraftDataVersion = GetInt(root, "MinecraftDataVersion", 0)
            };

            if (root["Metadata"] is NbtCompound meta)
            {
                file.Name = GetString(meta, "Name", "Unknown");
                file.Author = GetString(meta, "Author", "Unknown");
                file.Description = GetString(meta, "Description", "");
                file.TotalBlocks = GetLong(meta, "TotalBlocks", 0);
                file.TotalVolume = GetLong(meta, "TotalVolume", 0);

                if (meta["EnclosingSize"] is NbtCompound enc)
                {
                    file.EnclosingSizeX = GetInt(enc, "x", 0);
                    file.EnclosingSizeY = GetInt(enc, "y", 0);
                    file.EnclosingSizeZ = GetInt(enc, "z", 0);
                }
            }

            if (root["Regions"] is NbtCompound regions)
            {
                var regionIdx = 0;
                foreach (var regionTag in regions)
                {
                    if (regionTag is not NbtCompound regionCompound)
                        continue;

                    try
                    {
                        var region = ParseRegion(regionCompound, regionTag.Name ?? "Region");
                        file.Regions.Add(region);
                        regionIdx++;
                    }
                    catch (Exception ex)
                    {
                        throw new LitematicParseException(
                            $"Failed to parse region '{regionTag.Name}' (#{regionIdx}) in '{sourcePath}': {ex.Message}",
                            sourcePath, regionIdx, 0, ex);
                    }
                }
            }

            return file;
        }
        catch (Exception ex) when (ex is not LitematicParseException)
        {
            throw new LitematicParseException(
                $"Failed to parse litematic structure from '{sourcePath}': {ex.Message}",
                sourcePath, -1, 0, ex);
        }
    }

    private static Region ParseRegion(NbtCompound regionCompound, string name)
    {
        var region = new Region { Name = name };

        if (regionCompound["Position"] is NbtCompound pos)
        {
            region.MinX = GetInt(pos, "x", 0);
            region.MinY = GetInt(pos, "y", 0);
            region.MinZ = GetInt(pos, "z", 0);
        }

        if (regionCompound["Size"] is NbtCompound size)
        {
            var sx = GetInt(size, "x", 0);
            var sy = GetInt(size, "y", 0);
            var sz = GetInt(size, "z", 0);
            region.MaxX = region.MinX + sx - 1;
            region.MaxY = region.MinY + sy - 1;
            region.MaxZ = region.MinZ + sz - 1;
        }

        ParsePalette(regionCompound, region);
        ParseBlocks(regionCompound, region);

        return region;
    }

    private static void ParsePalette(NbtCompound regionCompound, Region region)
    {
        NbtList? palette = null;

        palette = regionCompound["BlockStatePalette"] as NbtList
               ?? regionCompound["blockstatepalette"] as NbtList
               ?? regionCompound["palette"] as NbtList;

        if (palette == null)
        {
            foreach (var child in regionCompound)
            {
                if (child is NbtList list && list.ListType == NbtTagType.Compound && list.Count > 0)
                {
                    if (list[0] is NbtCompound first && IsPaletteEntry(first))
                    {
                        palette = list;
                        region.Name += $" [palette via '{child.Name}']";
                        break;
                    }
                }
            }
        }

        if (palette == null)
            return;

        for (int i = 0; i < palette.Count; i++)
        {
            if (palette[i] is not NbtCompound paletteEntry)
                continue;

            var blockState = new BlockState
            {
                BlockId = GetString(paletteEntry, "Name", "minecraft:air")
            };

            if (paletteEntry["Properties"] is NbtCompound props)
            {
                foreach (var prop in props)
                {
                    if (prop is NbtString propStr)
                        blockState.Properties[prop.Name ?? ""] = propStr.Value;
                }
            }

            region.Palette[i] = blockState;
        }
    }

    private static bool IsPaletteEntry(NbtCompound entry)
    {
        if (!entry.TryGet("Name", out var nameTag)) return false;
        return nameTag is NbtString;
    }

    private static void ParseBlocks(NbtCompound regionCompound, Region region)
    {
        var blockStatesTag = regionCompound["BlockStates"];
        if (blockStatesTag == null)
            return;

        var paletteSize = Math.Max(1, region.Palette.Count);
        var bitsPerBlock = paletteSize > 1 ? Math.Max(2, (int)Math.Ceiling(Math.Log2(paletteSize))) : 1;
        region.BitsPerBlock = bitsPerBlock;

        var rawLongs = new List<long>();

        if (blockStatesTag is NbtLongArray longArray)
        {
            rawLongs.AddRange(longArray.Value);
        }
        else if (blockStatesTag is NbtList bsList)
        {
            foreach (var item in bsList)
            {
                if (item is NbtLong longTag)
                    rawLongs.Add(longTag.Value);
            }
        }

        region.RawLongs = rawLongs.ToArray();
    }

    private static NbtCompound ToNbt(LitematicFile file)
    {
        var root = new NbtCompound("");

        root.Add(new NbtInt("Version", file.Version));
        root.Add(new NbtInt("MinecraftDataVersion", file.MinecraftDataVersion));

        var metadata = new NbtCompound("Metadata");
        metadata.Add(new NbtString("Name", file.Name));
        metadata.Add(new NbtString("Author", file.Author));
        metadata.Add(new NbtString("Description", file.Description));
        metadata.Add(new NbtLong("TotalBlocks", file.TotalBlocks));
        metadata.Add(new NbtLong("TotalVolume", file.TotalVolume));
        metadata.Add(new NbtInt("RegionCount", file.Regions.Count));

        var enclosingSize = new NbtCompound("EnclosingSize");
        enclosingSize.Add(new NbtInt("x", (int)file.EnclosingSizeX));
        enclosingSize.Add(new NbtInt("y", (int)file.EnclosingSizeY));
        enclosingSize.Add(new NbtInt("z", (int)file.EnclosingSizeZ));
        metadata.Add(enclosingSize);

        root.Add(metadata);

        var regions = new NbtCompound("Regions");
        foreach (var region in file.Regions)
        {
            regions.Add(ToRegionNbt(region));
        }
        root.Add(regions);

        return root;
    }

    private static NbtCompound ToRegionNbt(Region region)
    {
        var compound = new NbtCompound(region.Name);

        var position = new NbtCompound("Position");
        position.Add(new NbtInt("x", region.MinX));
        position.Add(new NbtInt("y", region.MinY));
        position.Add(new NbtInt("z", region.MinZ));
        compound.Add(position);

        var size = new NbtCompound("Size");
        size.Add(new NbtInt("x", region.SizeX));
        size.Add(new NbtInt("y", region.SizeY));
        size.Add(new NbtInt("z", region.SizeZ));
        compound.Add(size);

        var palette = new NbtList("BlockStatePalette", NbtTagType.Compound);
        var paletteCount = region.Palette.Count;
        for (var i = 0; i < paletteCount; i++)
        {
            if (!region.Palette.TryGetValue(i, out var bs))
                continue;

            var entry = new NbtCompound();
            entry.Add(new NbtString("Name", bs.BlockId));

            if (bs.Properties.Count > 0)
            {
                var props = new NbtCompound("Properties");
                foreach (var (key, value) in bs.Properties)
                {
                    props.Add(new NbtString(key, value));
                }
                entry.Add(props);
            }

            palette.Add(entry);
        }
        compound.Add(palette);
        compound.Add(new NbtLongArray("BlockStates", region.RawLongs));
        compound.Add(new NbtList("Entities", NbtTagType.Compound));
        compound.Add(new NbtList("TileEntities", NbtTagType.Compound));

        return compound;
    }

    private static int GetInt(NbtCompound compound, string key, int defaultValue)
    {
        if (!compound.TryGet(key, out var tag)) return defaultValue;
        if (tag is NbtInt iTag) return iTag.Value;
        if (tag is NbtLong lTag) return (int)lTag.Value;
        return defaultValue;
    }

    private static long GetLong(NbtCompound compound, string key, long defaultValue)
    {
        if (!compound.TryGet(key, out var tag)) return defaultValue;
        if (tag is NbtLong lTag) return lTag.Value;
        if (tag is NbtInt iTag) return iTag.Value;
        return defaultValue;
    }

    private static string GetString(NbtCompound compound, string key, string defaultValue)
    {
        if (!compound.TryGet(key, out var tag)) return defaultValue;
        if (tag is NbtString sTag) return sTag.Value;
        return defaultValue;
    }
}

public class LitematicParseException : Exception
{
    public string FilePath { get; }
    public int RegionIndex { get; }
    public int BlockOffset { get; }

    public LitematicParseException(string message, string filePath, int regionIndex, int blockOffset,
        Exception? inner = null)
        : base(message, inner)
    {
        FilePath = filePath;
        RegionIndex = regionIndex;
        BlockOffset = blockOffset;
    }

    public string GetDetailString()
    {
        var parts = new List<string> { $"Error: {Message}" };
        parts.Add($"File: {FilePath}");
        if (RegionIndex >= 0) parts.Add($"Region: #{RegionIndex}");
        if (InnerException != null)
            parts.Add($"Inner: {InnerException.GetType().Name}: {InnerException.Message}");
        return string.Join("\n", parts);
    }
}
