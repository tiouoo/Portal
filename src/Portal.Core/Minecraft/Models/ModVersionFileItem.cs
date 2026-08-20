using System.Text.RegularExpressions;
using Iridium.Enums.Resources;
using Iridium.Models.Resources;
using Portal.Core.App.Helpers;
using Portal.Localization;

namespace Portal.Core.Minecraft.Models;

public sealed record ModVersionGroupKey(string Loader, string MinecraftVersion);

public sealed record ModVersionFileItem(
    string Id,
    string DisplayName,
    string Details,
    string ReleaseTypeText,
    string FileName,
    string DownloadUrl,
    long FileSize,
    DateTime Published,
    IReadOnlyList<string> MinecraftVersions,
    IReadOnlyList<ModVersionGroupKey> GroupKeys,
    ModDetailsSource Source,
    string ProjectId,
    IReadOnlyList<ModFileDependency> Dependencies)
{
    public static ModVersionFileItem From(ResourceFile file)
    {
        var fileName = file.PrimaryFile?.FileName ?? string.Empty;
        var versions = file.GameVersions.Where(IsMinecraftVersion).ToList();
        var loaders = file.Loaders.Count > 0
            ? file.Loaders.Select(LoaderName).OfType<string>().ToArray()
            : [LinguaSentinels.UniversalLoader];
        return new ModVersionFileItem(file.Id,
            string.IsNullOrWhiteSpace(file.Name) ? fileName : file.Name,
            FormatDetails(string.Join(",", loaders.Distinct()), fileName, file.Published, file.ReleaseType),
            ReleaseType(file.ReleaseType), fileName,
            file.PrimaryFile?.Url ?? string.Empty, file.PrimaryFile?.Size ?? 0,
            file.Published ?? default, versions,
            loaders.SelectMany(loader => versions.Select(version => new ModVersionGroupKey(loader, version)))
                .ToList(),
            file.Source == ResourceSource.Modrinth ? ModDetailsSource.Modrinth : ModDetailsSource.CurseForge,
            file.ProjectId,
            file.Dependencies.Where(dependency => dependency.Type == DependencyType.Required &&
                                                  (!string.IsNullOrWhiteSpace(dependency.ProjectId) ||
                                                   !string.IsNullOrWhiteSpace(dependency.VersionId)))
                .Select(dependency => new ModFileDependency(dependency.ProjectId ?? string.Empty,
                    dependency.FileName ?? string.Empty, dependency.VersionId)).Distinct().ToArray());
    }

    public ModVersionFileItem ForCompatibility(ModVersionGroupKey compatibility)
    {
        return this with
        {
            Details = $"{compatibility.Loader}·{Details[(Details.IndexOf('·') + 1)..]}",
            MinecraftVersions = [compatibility.MinecraftVersion],
            GroupKeys = [compatibility]
        };
    }

    private static string? LoaderName(ResourceLoaderType loader)
    {
        return loader switch
        {
            ResourceLoaderType.NeoForge => "NeoForge", ResourceLoaderType.Forge => "Forge",
            ResourceLoaderType.Fabric => "Fabric", ResourceLoaderType.Quilt => "Quilt",
            ResourceLoaderType.LiteLoader => "LiteLoader", ResourceLoaderType.OptiFine => "OptiFine",
            ResourceLoaderType.Canvas => "Canvas", ResourceLoaderType.Iris => "Iris",
            _ => LinguaSentinels.UniversalLoader
        };
    }

    private static bool IsMinecraftVersion(string version)
    {
        return Regex.IsMatch(version,
            @"^\d+\.\d+(?:\.\d+)?(?:-(?:snapshot|pre-release|pre\d+|rc\d+))?$", RegexOptions.IgnoreCase);
    }

    private static string FormatDetails(string loader, string fileName, DateTime? published, ReleaseType releaseType)
    {
        return $"{loader}·{fileName}·{RelativeTime.Format(published ?? default)}·{ReleaseType(releaseType)}";
    }

    private static string ReleaseType(Iridium.Enums.Resources.ReleaseType type)
    {
        return type switch
        {
            Iridium.Enums.Resources.ReleaseType.Beta =>
                CommonLanguageManager.Instance.mod_releaseTypeBeta.CurrentValue(),
            Iridium.Enums.Resources.ReleaseType.Alpha =>
                CommonLanguageManager.Instance.mod_releaseTypeAlpha.CurrentValue(),
            _ => CommonLanguageManager.Instance.mod_releaseTypeRelease.CurrentValue()
        };
    }
}

public sealed record ModFileDependency(string ProjectId, string Name, string? VersionId = null);

public readonly record struct MinecraftVersionKey(int Major, int Minor, int Patch) : IComparable<MinecraftVersionKey>
{
    public int CompareTo(MinecraftVersionKey other)
    {
        return Major != other.Major ? Major.CompareTo(other.Major) :
            Minor != other.Minor ? Minor.CompareTo(other.Minor) :
            Patch.CompareTo(other.Patch);
    }

    public static MinecraftVersionKey Parse(string version)
    {
        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        return match.Success
            ? new MinecraftVersionKey(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0)
            : new MinecraftVersionKey(-1, -1, -1);
    }
}
