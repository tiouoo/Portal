using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.SystemResources;

public readonly record struct MemoryOptimizationResult(long ReclaimedBytes);

public static class MemoryOptimizationService
{
    private static readonly object WorkingSetTrimLock = new();
    private static Timer? _workingSetTrimTimer;
    private static int _workingSetTrimInProgress;

    public static void StartAutomaticWorkingSetTrim()
    {
        if (!OperatingSystem.IsWindows()) return;

        lock (WorkingSetTrimLock)
        {
            if (_workingSetTrimTimer is not null) return;

            _workingSetTrimTimer = new Timer(_ => TrimWorkingSetOnTimer(), null, 0, 60000);
        }
    }

    public static bool TrimCurrentProcessWorkingSet()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return K32EmptyWorkingSet(GetCurrentProcess());
    }

    public static async Task<MemoryOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return new MemoryOptimizationResult(0);

        var before = GetAvailableMemoryBytes();
        try
        {
            OptimizeWindowsMemory();
        }
        catch (UnauthorizedAccessException)
        {
            await RunElevatedHelperAsync(cancellationToken);
        }

        var reclaimed = Math.Max(0, GetAvailableMemoryBytes() - before);
        return new MemoryOptimizationResult(reclaimed);
    }

    public static int OptimizeCurrentProcessContext()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        try
        {
            OptimizeWindowsMemory();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static async Task RunElevatedHelperAsync(CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("无法定位 Portal 可执行文件。");

        using var process = Process.Start(new ProcessStartInfo(executable, "--memory-optimize")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("无法申请管理员权限以执行内存优化。");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("管理员权限被拒绝或内存优化未完成。");
    }

    private static void OptimizeWindowsMemory()
    {
        const uint tokenAdjustPrivileges = 0x20;
        const uint tokenQuery = 0x8;
        const uint privilegeEnabled = 0x2;

        if (!OpenProcessToken(GetCurrentProcess(), tokenAdjustPrivileges | tokenQuery, out var token))
            throw CreatePrivilegeException();

        try
        {
            foreach (var privilege in new[] { "SeProfileSingleProcessPrivilege", "SeIncreaseQuotaPrivilege" })
            {
                if (!LookupPrivilegeValue(null, privilege, out var luid))
                    throw CreatePrivilegeException();

                var privileges = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Privileges = new LuidAndAttributes { Luid = luid, Attributes = privilegeEnabled }
                };
                Marshal.SetLastPInvokeError(0);
                if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                    throw CreatePrivilegeException();
                if (Marshal.GetLastWin32Error() == 1300)
                    throw new UnauthorizedAccessException("当前进程没有内存优化所需的权限。");
            }

            var statuses = new List<int>(7);
            var info = 2;
            statuses.Add(NtSetSystemInformation(80, ref info, sizeof(int)));
            var cache = new SystemFileCacheInformation
            {
                MinimumWorkingSet = nuint.MaxValue,
                MaximumWorkingSet = nuint.MaxValue
            };
            statuses.Add(NtSetSystemInformation(81, ref cache, Marshal.SizeOf<SystemFileCacheInformation>()));
            foreach (var value in new[] { 3, 4, 5 })
            {
                info = value;
                statuses.Add(NtSetSystemInformation(80, ref info, sizeof(int)));
            }
            statuses.Add(NtSetSystemInformation(155, IntPtr.Zero, 0));
            var combine = new MemoryCombineInformationEx();
            statuses.Add(NtSetSystemInformation(130, ref combine, Marshal.SizeOf<MemoryCombineInformationEx>()));

            if (statuses[0] < 0 && statuses[1] < 0)
                throw new UnauthorizedAccessException("内存优化需要管理员权限。");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static long GetAvailableMemoryBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)Math.Min(status.AvailablePhysical, long.MaxValue) : 0;
    }

    private static void TrimWorkingSetOnTimer()
    {
        if (Interlocked.Exchange(ref _workingSetTrimInProgress, 1) != 0) return;

        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var before = process.WorkingSet64;

            if (!TrimCurrentProcessWorkingSet())
            {
                Logger.Warning($"进程工作集修剪失败，Win32 错误码：{Marshal.GetLastWin32Error()}。");
                return;
            }

            process.Refresh();
            // Logger.Info($"进程工作集修剪完成：{before / 1024d / 1024d:F1} MiB -> " +
            //             $"{process.WorkingSet64 / 1024d / 1024d:F1} MiB。");
        }
        catch (Exception exception)
        {
            Logger.Error("进程工作集自动修剪发生异常。", exception);
        }
        finally
        {
            Volatile.Write(ref _workingSetTrimInProgress, 0);
        }
    }

    private static Exception CreatePrivilegeException()
    {
        var error = Marshal.GetLastWin32Error();
        return error is 5 or 1300
            ? new UnauthorizedAccessException("当前进程没有内存优化所需的权限。")
            : new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes { public Luid Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint PrivilegeCount; public LuidAndAttributes Privileges; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemFileCacheInformation
    {
        public nuint CurrentSize, PeakSize;
        public uint PageFaultCount;
        public nuint MinimumWorkingSet, MaximumWorkingSet, CurrentSizeIncludingTransitionInPages, PeakSizeIncludingTransitionInPages;
        public uint TransitionRePurposeCount, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCombineInformationEx { public IntPtr Handle; public nuint PagesCombined, Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool K32EmptyWorkingSet(IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref int information, int informationLength);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref SystemFileCacheInformation information, int informationLength);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref MemoryCombineInformationEx information, int informationLength);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, IntPtr information, int informationLength);
}
