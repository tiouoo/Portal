using System.Text.RegularExpressions;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
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
    public static ModVersionFileItem From(ModrinthResourceFile file)
    {
        return new ModVersionFileItem(file.VersionId,
            string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            FormatDetails(string.Join(",", file.ModLoaders.Select(LoaderName).Distinct()), file.FileName,
                file.Published,
                file.ReleaseType),
            ReleaseType(file.ReleaseType), file.FileName, file.DownloadUrl, file.FileSize, file.Published,
            file.MinecraftVersions.ToList(),
            file.ModLoaders.SelectMany(loader =>
                file.MinecraftVersions.Select(version => new ModVersionGroupKey(LoaderName(loader), version))).ToList(),
            ModDetailsSource.Modrinth,
            file.ProjectId,
            file.Dependencies.Where(dependency => dependency.Type == DependencyType.Required &&
                                                  (!string.IsNullOrWhiteSpace(dependency.ProjectId) ||
                                                   !string.IsNullOrWhiteSpace(dependency.VersionId)))
                .Select(dependency => new ModFileDependency(dependency.ProjectId, dependency.FileName ?? string.Empty,
                    dependency.VersionId)).Distinct().ToArray());
    }

    public static ModVersionFileItem From(CurseforgeResourceFile file)
    {
        var versions = file.GameVersions.Where(IsMinecraftVersion).ToList();
        var loaders = file.GameVersions.Select(LoaderName).OfType<string>().DefaultIfEmpty(LinguaSentinels.UniversalLoader);
        var enumerable = loaders as string[] ?? loaders.ToArray();
        return new ModVersionFileItem(file.Id.ToString(),
            string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName,
            FormatDetails(string.Join(",", enumerable), file.FileName, file.Published, file.ReleaseType),
            ReleaseType(file.ReleaseType), file.FileName,
            file.DownloadUrl, file.FileLength, file.Published, versions,
            enumerable.SelectMany(loader => versions.Select(version => new ModVersionGroupKey(loader, version)))
                .ToList(),
            ModDetailsSource.CurseForge,
            file.ModId.ToString(),
            file.Dependencies.Where(dependency => dependency.Value == DependencyType.Required)
                .Select(dependency => new ModFileDependency(dependency.Key.ToString(), string.Empty)).Distinct()
                .ToArray());
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

    private static string LoaderName(ModLoaderType loader)
    {
        return loader switch
        {
            ModLoaderType.NeoForge => "NeoForge", ModLoaderType.Forge => "Forge", ModLoaderType.Fabric => "Fabric",
            ModLoaderType.Quilt => "Quilt", _ => LinguaSentinels.UniversalLoader
        };
    }

    private static string? LoaderName(string loader)
    {
        return loader.Trim().ToLowerInvariant() switch
        {
            "neoforge" => "NeoForge", "forge" => "Forge", "fabric" => "Fabric", "quilt" => "Quilt", _ => null
        };
    }

    private static bool IsMinecraftVersion(string version)
    {
        return Regex.IsMatch(version,
            @"^\d+\.\d+(?:\.\d+)?(?:-(?:snapshot|pre-release|pre\d+|rc\d+))?$", RegexOptions.IgnoreCase);
    }

    private static string FormatDetails(string loader, string fileName, DateTime published, FileReleaseType releaseType)
    {
        return $"{loader}·{fileName}·{RelativeTime.Format(published)}·{ReleaseType(releaseType)}";
    }

    private static string ReleaseType(FileReleaseType type)
    {
        return type switch
        {
            FileReleaseType.Release => CommonLanguageManager.Instance.mod_releaseTypeRelease.CurrentValue(),
            FileReleaseType.Beta => CommonLanguageManager.Instance.mod_releaseTypeBeta.CurrentValue(),
            FileReleaseType.Alpha => CommonLanguageManager.Instance.mod_releaseTypeAlpha.CurrentValue(),
            _ => CommonLanguageManager.Instance.mod_releaseTypeOther.CurrentValue()
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