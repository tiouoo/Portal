namespace Portal.Core.Services.SystemResources;

public sealed record ResourceSnapshot
{
    public float? CpuUsage { get; init; }

    public ulong TotalMemoryBytes { get; init; }

    public ulong UsedMemoryBytes { get; init; }

    public float MemoryUsage => TotalMemoryBytes == 0 ? 0 : UsedMemoryBytes * 100f / TotalMemoryBytes;

    public float DiskUsage { get; init; }

    public ulong TotalDiskBytes { get; init; }

    public ulong UsedDiskBytes { get; init; }

    public ulong DiskReadBytesPerSec { get; init; }

    public ulong DiskWriteBytesPerSec { get; init; }

    public ulong NetworkDownloadBytesPerSec { get; init; }

    public ulong NetworkUploadBytesPerSec { get; init; }

    public float? GpuUsage { get; init; }

    public string? GpuName { get; init; }
}