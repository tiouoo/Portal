namespace PeNet.Header.Pe;

public class ExportFunction
{
        public ExportFunction(string? name, uint address, ushort ordinal)
    {
        Name = name;
        Address = address;
        Ordinal = ordinal;
    }

        public ExportFunction(string name, uint address, ushort ordinal, string forwardName)
    {
        Name = name;
        Address = address;
        Ordinal = ordinal;
        ForwardName = forwardName;
    }

        public string? Name { get; }

        public uint Address { get; }

        public ushort Ordinal { get; }

        public string? ForwardName { get; }


        public bool HasName => !string.IsNullOrEmpty(Name);

        public bool HasOrdinal => Ordinal != 0;

        public bool HasForward => !string.IsNullOrEmpty(ForwardName);
}