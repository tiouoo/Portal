using Hardware.Info;

namespace Portal.Core.SystemResources;

/// <summary>
/// 跨平台系统资源采集服务（单例）。
/// 在后台线程定期采集 CPU、内存、磁盘、网络、GPU 数据，并通过 <see cref="Updated"/> 事件发布快照。
/// </summary>
public sealed class SystemResourceService
{
    private readonly IHardwareInfo _hardware = new HardwareInfo();
    private readonly Timer _fastTimer;
    private readonly Timer _slowTimer;

    private ulong _lastNetBytesReceived;
    private ulong _lastNetBytesSent;
    private DateTime _lastNetSampleTime;

    public static SystemResourceService Instance { get; } = new();

    public event EventHandler<ResourceSnapshot>? Updated;

    public ResourceSnapshot Latest { get; private set; } = new();

    private SystemResourceService()
    {
        // 预热：先刷新显卡列表，避免首次采样拿不到 GPU 名称
        try { _hardware.RefreshVideoControllerList(refreshMonitorList: false); } catch { /* 忽略 */ }
        // CPU 使用率由 CpuUsageProvider 负责采集（需两次采样取差值），这里先触发一次预热
        _ = CpuUsageProvider.GetUsage();
        // GPU 使用率同样需要两次采样取差值，预热一次
        _ = GpuUsageProvider.GetUsage();

        _fastTimer = new Timer(_ => SampleFast(), null, 1000, 1000);
        _slowTimer = new Timer(_ => SampleSlow(), null, 2000, 3000);
    }

    public static void Initialize() => _ = Instance;

    /// <summary>快速采样：CPU、内存、磁盘容量、网络速率（开销极小）。</summary>
    private void SampleFast()
    {
        try
        {
            _hardware.RefreshMemoryStatus();
        }
        catch
        {
            // 忽略采集异常，保留上次结果
        }

        var memStatus = _hardware.MemoryStatus;

        // CPU 使用率：原生 API 计算差值，与任务管理器一致（1 秒粒度）
        var cpuUsage = CpuUsageProvider.GetUsage();
        ulong totalMem = memStatus.TotalPhysical;
        ulong usedMem = totalMem - memStatus.AvailablePhysical;

        // 系统盘容量（跨平台用 DriveInfo）
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
        catch
        {
            // 忽略
        }

        // 网络速率：用 NetworkInterface 统计两次采样差值（跨平台）
        ulong downBytes = 0, upBytes = 0;
        try
        {
            var now = DateTime.UtcNow;
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                             ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
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
        catch
        {
            // 忽略
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

    /// <summary>慢速采样：GPU 信息与使用率（型号不常变，低频刷新即可）。</summary>
    private void SampleSlow()
    {
        float? gpuUsage = null;
        string? gpuName = null;

        // GPU 使用率：Windows 用 PDH API 读取 GPU Engine 性能计数器
        gpuUsage = GpuUsageProvider.GetUsage();

        // GPU 型号：Hardware.Info 通过 WMI 获取
        try
        {
            _hardware.RefreshVideoControllerList(refreshMonitorList: false);
            var gpu = PickRealGpu(_hardware.VideoControllerList);
            if (gpu != null)
                gpuName = gpu.Name;
        }
        catch
        {
            // 忽略
        }

        var snapshot = Latest with
        {
            GpuUsage = gpuUsage ?? Latest.GpuUsage,
            GpuName = gpuName
        };
        Latest = snapshot;
        Updated?.Invoke(this, snapshot);
    }

    /// <summary>
    /// 从显卡列表中挑选真实的物理 GPU，跳过远程控制软件创建的虚拟显卡。
    /// 策略：先过滤掉名称含虚拟关键词的，再优先选择显存最大的；若全部被过滤则回退到第一个。
    /// </summary>
    private static Hardware.Info.VideoController? PickRealGpu(IEnumerable<Hardware.Info.VideoController> list)
    {
        var all = list as IList<Hardware.Info.VideoController> ?? list.ToList();
        if (all.Count == 0)
            return null;

        // 名称含真实 GPU 厂商关键词的优先选择
        var real = all.Where(v => !IsVirtualGpu(v.Name)).ToList();
        if (real.Count == 0)
            return all.FirstOrDefault();

        // 优先选择显存最大的（AdapterRAM 可能不准确，但作为排序依据足够）
        return real
            .OrderByDescending(v => v.AdapterRAM)
            .FirstOrDefault();
    }

    /// <summary>判断显卡名称是否属于虚拟/远程显示适配器。</summary>
    private static bool IsVirtualGpu(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        var lower = name.ToLowerInvariant();
        return VirtualGpuKeywords.Any(k => lower.Contains(k));
    }

    private static readonly string[] VirtualGpuKeywords =
    [
        "microsoft basic render",     // Microsoft Basic Render Driver
        "remote display",             // Microsoft Remote Display Adapter
        "mirror",                     // Mirror Driver
        "virtual",                    // Virtual Display
        "indirect",                   // Indirect Display
        "software",                   // Software Adapter
        "rdp",                        // RDP
        "parsec",                     // Parsec Virtual Display
        "anydesk",                    // AnyDesk
        "todesk",                     // ToDesk
        "teamviewer",                 // TeamViewer
        "splashtop",                  // Splashtop
        "spacedesk",                  // SpaceDesk
        "duet",                       // Duet Display
        "displaylink",                // DisplayLink
        "iddcx",                      // Indirect Display Driver Class
        "ammyy",                      // Ammyy Admin
        "supremo",                    // SupRemo
        "no machine",                 // NoMachine NX
        "meshconsole",                // MeshConsole
        "lite manager",               // LiteManager
        "usb video",                  // USB Video Device
        "microsoft hyper-v"           // Hyper-V Video
    ];
}
