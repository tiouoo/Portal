using System.Runtime.InteropServices;

namespace Portal.Core.SystemResources;

/// <summary>
/// 跨平台 CPU 使用率采集。使用操作系统原生 API 计算两次采样间的 CPU 占用，
/// 结果与任务管理器一致（Windows）。
/// </summary>
internal static class CpuUsageProvider
{
    private static ulong _lastIdle;
    private static ulong _lastTotal;
    private static bool _initialized;

    /// <summary>返回 0-100 的 CPU 占用百分比。首次调用返回 null（需两次采样才能算差值）。</summary>
    public static float? GetUsage()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsUsage();
            if (OperatingSystem.IsLinux())
                return GetLinuxUsage();
        }
        catch
        {
            // 忽略采集异常
        }
        return null;
    }

    // ---------- Windows: GetSystemTimes ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.High << 32) | ft.Low;

    private static float? GetWindowsUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return null;

        var idleNow = ToUInt64(idle);
        // kernel 已包含 idle，总时间 = kernel + user
        var totalNow = ToUInt64(kernel) + ToUInt64(user);

        if (!_initialized)
        {
            _lastIdle = idleNow;
            _lastTotal = totalNow;
            _initialized = true;
            return null;
        }

        var totalDelta = totalNow - _lastTotal;
        var idleDelta = idleNow - _lastIdle;
        _lastIdle = idleNow;
        _lastTotal = totalNow;

        if (totalDelta == 0)
            return null;

        var usage = (1.0 - (double)idleDelta / (double)totalDelta) * 100.0;
        return (float)Math.Clamp(usage, 0, 100);
    }

    // ---------- Linux: /proc/stat ----------

    private static float? GetLinuxUsage()
    {
        var firstLine = File.ReadAllLines("/proc/stat").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // cpu  user nice system idle iowait irq softirq steal [guest guest_nice]
        if (parts.Length < 5)
            return null;

        ulong user = ulong.Parse(parts[1]);
        ulong nice = ulong.Parse(parts[2]);
        ulong system = ulong.Parse(parts[3]);
        ulong idle = ulong.Parse(parts[4]);
        ulong iowait = parts.Length > 5 ? ulong.Parse(parts[5]) : 0;
        ulong irq = parts.Length > 6 ? ulong.Parse(parts[6]) : 0;
        ulong softirq = parts.Length > 7 ? ulong.Parse(parts[7]) : 0;
        ulong steal = parts.Length > 8 ? ulong.Parse(parts[8]) : 0;

        var idleAll = idle + iowait;
        var total = user + nice + system + idle + iowait + irq + softirq + steal;

        if (!_initialized)
        {
            _lastIdle = idleAll;
            _lastTotal = total;
            _initialized = true;
            return null;
        }

        var totalDelta = total - _lastTotal;
        var idleDelta = idleAll - _lastIdle;
        _lastIdle = idleAll;
        _lastTotal = total;

        if (totalDelta == 0)
            return null;

        var usage = (1.0 - (double)idleDelta / (double)totalDelta) * 100.0;
        return (float)Math.Clamp(usage, 0, 100);
    }
}
