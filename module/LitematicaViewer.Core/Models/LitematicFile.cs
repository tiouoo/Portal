namespace LitematicaViewer.Core.Models;

public class LitematicFile
{
    public int Version { get; set; }
    public int MinecraftDataVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long TotalBlocks { get; set; }
    public long TotalVolume { get; set; }
    public long EnclosingSizeX { get; set; }
    public long EnclosingSizeY { get; set; }
    public long EnclosingSizeZ { get; set; }
    public List<Region> Regions { get; set; } = new();

    public string VersionDisplay => Version switch
    {
        <= 3 => "1.12/1.13",
        <= 6 => "1.14~1.20.5",
        _ => "1.20.6+"
    };
}
