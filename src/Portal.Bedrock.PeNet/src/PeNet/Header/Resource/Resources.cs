using System.Linq;
using PeNet.FileParser;

namespace PeNet.Header.Resource;

public class Resources : AbstractStructure
{
    private readonly ResourceLocation[] _groupIconDirectoryLocations;
    private readonly ResourceLocation[] _iconDirectoryLocations;
    private readonly ResourceLocation? _vsVersionLocation;
    private GroupIconDirectory[]? _groupIconDirectories;

    private bool _groupIconDirectoriesParsed;
    private Icon[]? _icons;

    private bool _iconsParsed;
    private VsVersionInfo? _vsVersionInfo;
    private bool _vsVersionInfoParsed;

        public Resources(IRawFile peFile, long offset, ResourceLocation? vsVersionLocation,
        ResourceLocation[] iconDirectoryLocations, ResourceLocation[] groupIconDirectoryLocations)
        : base(peFile, offset)
    {
        _vsVersionLocation = vsVersionLocation;
        _iconDirectoryLocations = iconDirectoryLocations;
        _groupIconDirectoryLocations = groupIconDirectoryLocations;
    }

        public VsVersionInfo? VsVersionInfo
    {
        get
        {
            if (_vsVersionInfoParsed) return _vsVersionInfo;
            _vsVersionInfoParsed = true;
            if (_vsVersionLocation != null)
                return _vsVersionInfo ??= new VsVersionInfo(PeFile, _vsVersionLocation.Offset);
            return null;
        }
    }

        public Icon[]? Icons
    {
        get
        {
            if (_iconsParsed) return _icons;
            _iconsParsed = true;
            return _icons ??= _iconDirectoryLocations
                .Select(location => new Icon(PeFile, location.Offset, location.Size,
                    location.Resource.Parent.Parent.Parent?.ID ?? uint.MaxValue, this))
                .ToArray();
        }
    }

        public GroupIconDirectory[]? GroupIconDirectories
    {
        get
        {
            if (_groupIconDirectoriesParsed) return _groupIconDirectories;
            _groupIconDirectoriesParsed = true;
            return _groupIconDirectories ??= _groupIconDirectoryLocations
                .Select(location => new GroupIconDirectory(PeFile, location))
                .ToArray();
        }
    }
}