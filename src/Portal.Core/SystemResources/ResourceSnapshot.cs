namespace Portal.Core.SystemResources;

/// <summary>
/// 系统资源数据快照，由 <see cref="SystemResourceService"/> 定期采集后发布。
/// 所有数值在采集时已做好单位换算与容错处理。
/// </summary>
public sealed record ResourceSnapshot
{
    /// <summary>CPU 整体占用百分比（0-100），取不到时为 null。</summary>
    public float? CpuUsage { get; init; }

    /// <summary>系统物理内存总量（字节）。</summary>
    public ulong TotalMemoryBytes { get; init; }

    /// <summary>系统已用物理内存（字节）。</summary>
    public ulong UsedMemoryBytes { get; init; }

    /// <summary>内存占用百分比（0-100）。</summary>
    public float MemoryUsage => TotalMemoryBytes == 0 ? 0 : UsedMemoryBytes * 100f / TotalMemoryBytes;

    /// <summary>系统盘（C:/根）占用百分比（0-100）。</summary>
    public float DiskUsage { get; init; }

    /// <summary>系统盘总容量（字节）。</summary>
    public ulong TotalDiskBytes { get; init; }

    /// <summary>系统盘已用容量（字节）。</summary>
    public ulong UsedDiskBytes { get; init; }

    /// <summary>磁盘读取速率（字节/秒），取不到时为 0。</summary>
    public ulong DiskReadBytesPerSec { get; init; }

    /// <summary>磁盘写入速率（字节/秒），取不到时为 0。</summary>
    public ulong DiskWriteBytesPerSec { get; init; }

    /// <summary>网络下载速率（字节/秒）。</summary>
    public ulong NetworkDownloadBytesPerSec { get; init; }

    /// <summary>网络上传速率（字节/秒）。</summary>
    public ulong NetworkUploadBytesPerSec { get; init; }

    /// <summary>GPU 占用百分比（0-100），取不到时为 null。</summary>
    public float? GpuUsage { get; init; }

    /// <summary>GPU 名称，取不到时为 null。</summary>
    public string? GpuName { get; init; }
}
