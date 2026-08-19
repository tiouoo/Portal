using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Models;

public enum ResourceKind
{
    Mod,
    ResourcePack,
    ShaderPack
}

public sealed record ResourceUpdateCandidate(
    string FilePath,
    ResourceKind Kind,
    string? Sha1 = null,
    uint? Fingerprint = null,
    string? Source = null,
    string? ProjectId = null,
    string? VersionId = null);

public sealed record ResourceUpdateResult(
    string FilePath,
    ModDetailsSource? Source,
    string? ProjectId,
    string? CurrentVersionId,
    string? TargetVersionId,
    ModVersionFileItem? TargetFile)
{
    public bool HasUpdate => TargetFile != null &&
                             !string.IsNullOrEmpty(TargetVersionId) &&
                             !string.Equals(CurrentVersionId, TargetVersionId, StringComparison.Ordinal);

    public bool HasIdentity => Source != null && !string.IsNullOrEmpty(ProjectId);
}
