namespace Portal.Core.Minecraft.Graphics;

public readonly struct GameVersion : IComparable<GameVersion>, IEquatable<GameVersion>
{
    public const int MinimumYearMajorVersion = 26;

    private enum VersionKind
    {
        Unknown,
        Release,
        LegacySnapshot,
        Old
    }

    private enum SuffixType
    {
        Snapshot,
        Pre,
        Rc,
        Ga
    }

    private readonly VersionKind _kind;
    private readonly int _major;
    private readonly int _minor;
    private readonly int _patch;
    private readonly SuffixType _suffix;
    private readonly int _suffixNumber;
    private readonly int _snapshotValue;
    private readonly int _unknownOrder;

    private GameVersion(VersionKind kind, string raw, int unknownOrder = 0)
    {
        _kind = kind;
        Value = raw;
        _unknownOrder = unknownOrder;
    }

    private GameVersion(string raw, int major, int minor, int patch, SuffixType suffix, int suffixNumber)
    {
        _kind = VersionKind.Release;
        Value = raw;
        _major = major;
        _minor = minor;
        _patch = patch;
        _suffix = suffix;
        _suffixNumber = suffixNumber;
    }

    private GameVersion(int snapshotValue, string raw)
    {
        _kind = VersionKind.LegacySnapshot;
        Value = raw;
        _snapshotValue = snapshotValue;
    }

    private static GameVersion Unknown(string raw)
    {
        return new GameVersion(VersionKind.Unknown, raw, 1);
    }

    public string Value { get; }

    public static GameVersion Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return Unknown(version ?? string.Empty);

        if (!version.Any(char.IsDigit))
            return Unknown(version);

        var first = version[0];

        if (first is 'r' or 'a' or 'b' or 'c' or 'i')
            return new GameVersion(VersionKind.Old, version);

        if (version == "0.0")
            return new GameVersion(VersionKind.Old, version);

        if (version.Length >= 5 && version[2] == 'w'
                                && int.TryParse(version.AsSpan(0, 2), out var snapshotYear)
                                && int.TryParse(version.AsSpan(3, 2), out var snapshotWeek))
        {
            var snapshotSuffix = version.Length > 5 ? version[5] : ' ';
            return new GameVersion(
                (snapshotYear << 24) | (snapshotWeek << 16) | ((snapshotSuffix & 0xff) << 8),
                version);
        }

        var dash = version.IndexOf('-');
        var releaseCore = dash >= 0 ? version[..dash] : version;
        var suffixText = dash >= 0 ? version[(dash + 1)..] : string.Empty;

        var parts = releaseCore.Split('.');
        if (parts.Length < 2)
            return Unknown(version);

        if (!int.TryParse(parts[0], out var major))
            return Unknown(version);
        if (!int.TryParse(parts[1], out var minor))
            return Unknown(version);

        var patch = 0;
        if (parts.Length >= 3)
            if (!int.TryParse(parts[2], out patch))
                return Unknown(version);

        var suffix = SuffixType.Ga;
        var suffixNumber = 0;
        if (dash >= 0)
        {
            (SuffixType suffixType, int number)? parsed = ParseSuffix(suffixText);
            if (parsed is not { } value)
                return Unknown(version);

            suffix = value.suffixType;
            suffixNumber = value.number;
        }

        return new GameVersion(version, major, minor, patch, suffix, suffixNumber);
    }

    private static (SuffixType type, int number)? ParseSuffix(string text)
    {
        if (text.Length == 0)
            return null;

        var lower = text.Trim().ToLowerInvariant();

        if (lower.StartsWith("snapshot-", StringComparison.Ordinal) &&
            int.TryParse(lower["snapshot-".Length..], out var snap2))
            return (SuffixType.Snapshot, snap2);

        if (lower.StartsWith("snapshot ", StringComparison.Ordinal) &&
            int.TryParse(lower["snapshot ".Length..], out var snap3))
            return (SuffixType.Snapshot, snap3);

        if (lower.StartsWith("pre-", StringComparison.Ordinal) &&
            int.TryParse(lower["pre-".Length..], out var preNum2))
            return (SuffixType.Pre, preNum2);

        if (lower.StartsWith("pre", StringComparison.Ordinal) &&
            int.TryParse(lower["pre".Length..], out var preNum))
            return (SuffixType.Pre, preNum);

        if (lower.StartsWith("rc-", StringComparison.Ordinal) &&
            int.TryParse(lower["rc-".Length..], out var rcNum2))
            return (SuffixType.Rc, rcNum2);

        if (lower.StartsWith("rc", StringComparison.Ordinal) &&
            int.TryParse(lower["rc".Length..], out var rcNum))
            return (SuffixType.Rc, rcNum);

        return null;
    }

    public int CompareTo(GameVersion other)
    {
        if (_kind != other._kind)
            return KindRank(_kind).CompareTo(KindRank(other._kind));

        switch (_kind)
        {
            case VersionKind.Unknown:
                return _unknownOrder.CompareTo(other._unknownOrder);

            case VersionKind.LegacySnapshot:
                return _snapshotValue.CompareTo(other._snapshotValue);

            case VersionKind.Old:
                return string.CompareOrdinal(Value, other.Value);

            case VersionKind.Release:
            {
                var c = _major.CompareTo(other._major);
                if (c != 0) return c;

                c = _minor.CompareTo(other._minor);
                if (c != 0) return c;

                c = _patch.CompareTo(other._patch);
                if (c != 0) return c;

                c = SuffixRank(_suffix).CompareTo(SuffixRank(other._suffix));
                if (c != 0) return c;

                return _suffixNumber.CompareTo(other._suffixNumber);
            }

            default:
                return 0;
        }
    }

    private static int SuffixRank(SuffixType type)
    {
        return type switch
        {
            SuffixType.Snapshot => 0,
            SuffixType.Pre => 1,
            SuffixType.Rc => 2,
            SuffixType.Ga => 3,
            _ => -1
        };
    }

    private static int KindRank(VersionKind kind)
    {
        return kind switch
        {
            VersionKind.Old => 0,
            VersionKind.LegacySnapshot => 1,
            VersionKind.Release => 2,
            VersionKind.Unknown => 3,
            _ => 3
        };
    }

    public bool Equals(GameVersion other)
    {
        return CompareTo(other) == 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is GameVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (_kind.GetHashCode() << 24) ^ (_major << 16) ^ (_minor << 8) ^ (_patch);
    }

    public override string ToString()
    {
        return Value;
    }

    public static bool operator <(GameVersion a, GameVersion b)
    {
        return a.CompareTo(b) < 0;
    }

    public static bool operator >(GameVersion a, GameVersion b)
    {
        return a.CompareTo(b) > 0;
    }

    public static bool operator >=(GameVersion a, GameVersion b)
    {
        return a.CompareTo(b) >= 0;
    }

    public static bool operator <=(GameVersion a, GameVersion b)
    {
        return a.CompareTo(b) <= 0;
    }

    public static bool operator ==(GameVersion a, GameVersion b)
    {
        return a.CompareTo(b) == 0;
    }

    public static bool operator !=(GameVersion a, GameVersion b)
    {
        return a.CompareTo(b) != 0;
    }
}