namespace LitematicaViewer.Core.Models;

public class BlockState
{
    public string BlockId { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new();

    public override string ToString()
    {
        if (Properties.Count == 0)
            return BlockId;
        var props = string.Join(",", Properties.Select(p => $"{p.Key}={p.Value}"));
        return $"{BlockId}[{props}]";
    }

    public static BlockState FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return new BlockState { BlockId = "minecraft:air" };

        value = value.Trim();
        if (!value.Contains('['))
            return new BlockState { BlockId = value };

        var bracketIndex = value.IndexOf('[');
        var id = value[..bracketIndex];
        var propsStr = value[(bracketIndex + 1)..].TrimEnd(']');

        var props = new Dictionary<string, string>();
        foreach (var pair in propsStr.Split(','))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
                props[pair[..eqIndex]] = pair[(eqIndex + 1)..];
        }

        return new BlockState { BlockId = id, Properties = props };
    }
}
