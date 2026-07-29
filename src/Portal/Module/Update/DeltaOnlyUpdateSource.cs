using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Portal.Module.Update;

internal sealed class DeltaOnlyUpdateSource(IUpdateSource source) : IUpdateSource
{
    public Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null) =>
        source.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);

    public Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken = default)
    {
        if (releaseEntry.Type != VelopackAssetType.Delta)
            throw new InvalidOperationException("静默更新只允许下载增量更新包。");

        return source.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
