using PeNet.Header.Pe;

namespace PeNet.Header.Resource;

public record ResourceLocation(ImageResourceDataEntry Resource, uint Offset, uint Size)
{
    public ImageResourceDataEntry Resource { get; } = Resource;
    public uint Offset { get; } = Offset;
    public uint Size { get; } = Size;
}