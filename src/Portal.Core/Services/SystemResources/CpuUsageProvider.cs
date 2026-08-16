using System.Runtime.InteropServices;

namespace Portal.Core.Services.SystemResources;

internal static partial class CpuUsageProvider
{
    private static ulong _lastIdle;
    private static ulong _lastTotal;
    private static bool _initialized;

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
        }

        return null;
    }

    [LibraryImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out Filetime idleTime, out Filetime kernelTime, out Filetime userTime);

    private static ulong ToUInt64(Filetime ft)
    {
        return ((ulong)ft.High << 32) | ft.Low;
    }

    private static float? GetWindowsUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return null;

        var idleNow = ToUInt64(idle);
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

        var usage = (1.0 - idleDelta / (double)totalDelta) * 100.0;
        return (float)Math.Clamp(usage, 0, 100);
    }

    private static float? GetLinuxUsage()
    {
        var firstLine = File.ReadAllLines("/proc/stat").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return null;

        var user = ulong.Parse(parts[1]);
        var nice = ulong.Parse(parts[2]);
        var system = ulong.Parse(parts[3]);
        var idle = ulong.Parse(parts[4]);
        var iowait = parts.Length > 5 ? ulong.Parse(parts[5]) : 0;
        var irq = parts.Length > 6 ? ulong.Parse(parts[6]) : 0;
        var softirq = parts.Length > 7 ? ulong.Parse(parts[7]) : 0;
        var steal = parts.Length > 8 ? ulong.Parse(parts[8]) : 0;

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

        var usage = (1.0 - (double)idleDelta / totalDelta) * 100.0;
        return (float)Math.Clamp(usage, 0, 100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Filetime
    {
        public uint Low;
        public uint High;
    }
}