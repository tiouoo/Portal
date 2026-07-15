namespace LitematicaViewer.Core.Models;

public class ContainerData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string ContainerBlockId { get; set; } = string.Empty;
    public List<ContainerItem> Items { get; set; } = new();
    public Dictionary<string, string> ContainerProperties { get; set; } = new();
}

public class ContainerItem
{
    public string ItemId { get; set; } = string.Empty;
    public int Slot { get; set; }
    public int Count { get; set; }
}
