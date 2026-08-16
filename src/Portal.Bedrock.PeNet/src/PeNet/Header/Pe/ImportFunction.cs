namespace PeNet.Header.Pe;

public class ImportFunction
{
        public ImportFunction(string? name, string dll, ushort hint, uint iatOffset)
    {
        Name = name;
        DLL = dll;
        Hint = hint;
        IATOffset = iatOffset;
    }

        public string? Name { get; }

        public string DLL { get; }

        public ushort Hint { get; }

        public uint IATOffset { get; }
}