namespace PeNet.Header.Net;

public class HeapSizes
{
        public HeapSizes(byte heapSizes)
    {
        String = (heapSizes & 0x1) == 0 ? 2U : 4U;
        Guid = (heapSizes & 0x2) == 0 ? 2U : 4U;
        Blob = (heapSizes & 0x4) == 0 ? 2U : 4U;
        HasExtraData = (heapSizes & 0x40) == 0;
    }

        public uint String { get; }

        public uint Guid { get; }

        public uint Blob { get; }

        public bool HasExtraData { get; }
}