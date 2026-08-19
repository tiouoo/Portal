using System.Net.NetworkInformation;
using Hardware.Info;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Services.SystemResources;

public sealed class SystemResourceService : IDisposable
{
    private static readonly string[] VirtualGpuKeywords =
    [
        "microsoft basic render",
        "remote display",
        "mirror",
        "virtual",
        "indirect",
        "software",
        "rdp",
        "parsec",
        "anydesk",
        "todesk",
        "teamviewer",
        "splashtop",
        "spacedesk",
        "duet",
        "displaylink",
        "iddcx",
        "ammyy",
        "supremo",
        "no machine",
        "meshconsole",
        "lite manager",
        "usb video",
        "microsoft hyper-v"
    ];

    private readonly Timer _fastTimer;
    private readonly IHardwareInfo _hardware = new HardwareInfo();
    private readonly Timer _slowTimer;
    private bool _disposed;

    private ulong _lastNetBytesReceived;
    private ulong _lastNetBytesSent;
    private DateTime _lastNetSampleTime;

    private SystemResourceService()
    {
        try
        {
            _hardware.RefreshVideoControllerList(false);
        }
        catch (Exception exception)
        {
            Logger.Error(LogLanguageManager.Instance.systemResources_gpuWarmupFailed.CurrentValue(), exception);
        }

        _ = CpuUsageProvider.GetUsage();
        _ = GpuUsageProvider.GetUsage();

        _fastTimer = new Timer(_ => SampleFast(), null, 1000, 1000);
        _slowTimer = new Timer(_ => SampleSlow(), null, 2000, 3000);
    }

    public static SystemResourceService Instance { get; } = new();

    public ResourceSnapshot Latest { get; private set; } = new();

    public void Dispose()
    {
        if (_disposed) return;
        _fastTimer.Dispose();
        _slowTimer.Dispose();
        _disposed = true;
    }

    public event EventHandler<ResourceSnapshot>? Updated;

    public static void Initialize()
    {
        _ = Instance;
    }

    private void SampleFast()
    {
        try
        {
            _hardware.RefreshMemoryStatus();
        }
        catch (Exception exception)
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.systemResources_memoryCollectionFailed.CurrentValue(), Environment.NewLine, exception));
        }

        var memStatus = _hardware.MemoryStatus;

        var cpuUsage = CpuUsageProvider.GetUsage();
        var totalMem = memStatus.TotalPhysical;
        var usedMem = totalMem - memStatus.AvailablePhysical;

        ulong totalDisk = 0, usedDisk = 0;
        float diskUsage = 0;
        try
        {
            var sysDrive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && d.RootDirectory.FullName ==
                    Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'));
            if (sysDrive != null)
            {
                totalDisk = (ulong)sysDrive.TotalSize;
                usedDisk = totalDisk - (ulong)sysDrive.AvailableFreeSpace;
                diskUsage = totalDisk == 0 ? 0 : usedDisk * 100f / totalDisk;
            }
        }
        catch (Exception exception)
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.systemResources_diskCollectionFailed.CurrentValue(), Environment.NewLine, exception));
        }

        ulong downBytes = 0, upBytes = 0;
        try
        {
            var now = DateTime.UtcNow;
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            ulong totalReceived = 0, totalSent = 0;
            foreach (var ni in interfaces)
            {
                var stats = ni.GetIPv4Statistics();
                totalReceived += (ulong)stats.BytesReceived;
                totalSent += (ulong)stats.BytesSent;
            }

            if (_lastNetSampleTime != default && _lastNetBytesReceived > 0)
            {
                var elapsed = (now - _lastNetSampleTime).TotalSeconds;
                if (elapsed > 0)
                {
                    downBytes = (ulong)Math.Max(0, (totalReceived - _lastNetBytesReceived) / elapsed);
                    upBytes = (ulong)Math.Max(0, (totalSent - _lastNetBytesSent) / elapsed);
                }
            }

            _lastNetBytesReceived = totalReceived;
            _lastNetBytesSent = totalSent;
            _lastNetSampleTime = now;
        }
        catch (Exception exception)
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.systemResources_networkCollectionFailed.CurrentValue(), Environment.NewLine, exception));
        }

        var snapshot = Latest with
        {
            CpuUsage = cpuUsage ?? Latest.CpuUsage,
            TotalMemoryBytes = totalMem,
            UsedMemoryBytes = usedMem,
            DiskUsage = diskUsage,
            TotalDiskBytes = totalDisk,
            UsedDiskBytes = usedDisk,
            NetworkDownloadBytesPerSec = downBytes,
            NetworkUploadBytesPerSec = upBytes
        };
        Latest = snapshot;
        Updated?.Invoke(this, snapshot);
    }

    private void SampleSlow()
    {
        string? gpuName = null;

        var gpuUsage = GpuUsageProvider.GetUsage();

        try
        {
            _hardware.RefreshVideoControllerList(false);
            var gpu = PickRealGpu(_hardware.VideoControllerList);
            if (gpu != null)
                gpuName = gpu.Name;
        }
        catch (Exception exception)
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.systemResources_gpuCollectionFailed.CurrentValue(), Environment.NewLine, exception));
        }

        var snapshot = Latest with
        {
            GpuUsage = gpuUsage ?? Latest.GpuUsage,
            GpuName = gpuName
        };
        Latest = snapshot;
        Updated?.Invoke(this, snapshot);
    }

    private static VideoController? PickRealGpu(IEnumerable<VideoController> list)
    {
        var all = list as IList<VideoController> ?? list.ToList();
        if (all.Count == 0)
            return null;

        var real = all.Where(v => !IsVirtualGpu(v.Name)).ToList();
        if (real.Count == 0)
            return all.FirstOrDefault();

        return real
            .OrderByDescending(v => v.AdapterRAM)
            .FirstOrDefault();
    }

    private static bool IsVirtualGpu(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        var lower = name.ToLowerInvariant();
        return VirtualGpuKeywords.Any(lower.Contains);
    }
}