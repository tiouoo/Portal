using System.Runtime.InteropServices;

namespace Portal.Core.App.Service.SystemResources;

internal static class GpuUsageProvider
{
    private static IntPtr _query = IntPtr.Zero;
    private static readonly List<IntPtr> Counters = [];
    private static bool _initialized;

    public static float? GetUsage()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return GetWindowsUsage(); }
        catch { return null; }
    }

    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhNoData = 0x800007D5;
    private const uint PdhInvalidData = 0xC0000BC6;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhFmtCountervalue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(IntPtr dataSource, uint userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddCounterW(IntPtr query, string counterPath, uint userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCountervalue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPathW(string? machine, string wildcardPath, IntPtr expandedPathList, ref uint bufferSize, uint flags);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private static float? GetWindowsUsage()
    {
        if (!_initialized)
        {
            BuildQuery();
            _initialized = true;
            if (_query != IntPtr.Zero) PdhCollectQueryData(_query);
            return null;
        }

        if (_query == IntPtr.Zero || Counters.Count == 0)
        {
            BuildQuery();
            if (_query == IntPtr.Zero || Counters.Count == 0) return null;
            PdhCollectQueryData(_query);
            return null;
        }

        var collectResult = PdhCollectQueryData(_query);
        if (collectResult == PdhNoData)
        {
            BuildQuery();
            if (_query == IntPtr.Zero || Counters.Count == 0) return null;
            PdhCollectQueryData(_query);
            return null;
        }
        if (collectResult != 0) return null;

        double max = 0;
        bool anyValid = false, anyStale = false;
        foreach (var c in Counters)
        {
            var res = PdhGetFormattedCounterValue(c, PdhFmtDouble, out _, out var val);
            switch (res)
            {
                case 0 when val.CStatus == 0:
                {
                    anyValid = true;
                    if (val.DoubleValue > max) max = val.DoubleValue;
                    break;
                }
                case PdhInvalidData:
                    anyStale = true;
                    break;
            }
        }
        if (anyStale) BuildQuery();
        return anyValid ? (float)Math.Clamp(max, 0, 100) : null;
    }

    private static void BuildQuery()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            Counters.Clear();
        }

        const string wildcardPath = @"\GPU Engine(*)\Utilization Percentage";
        uint bufSize = 0;
        var status = PdhExpandWildCardPathW(null, wildcardPath, IntPtr.Zero, ref bufSize, 0);
        if (status != PdhMoreData || bufSize == 0) return;

        var buffer = Marshal.AllocHGlobal((int)bufSize * 2);
        try
        {
            status = PdhExpandWildCardPathW(null, wildcardPath, buffer, ref bufSize, 0);
            if (status != 0) return;
            var paths = ReadMultiString(buffer, (int)bufSize);
            if (paths.Count == 0) return;

            status = PdhOpenQuery(IntPtr.Zero, 0, out _query);
            if (status != 0) { _query = IntPtr.Zero; return; }
            foreach (var p in paths)
            {
                if (PdhAddCounterW(_query, p, 0, out var c) == 0) Counters.Add(c);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static List<string> ReadMultiString(IntPtr buffer, int sizeChars)
    {
        var list = new List<string>();
        var offset = 0;
        while (offset < sizeChars)
        {
            var s = Marshal.PtrToStringUni(buffer + offset * 2);
            if (string.IsNullOrEmpty(s)) break;
            list.Add(s);
            offset += s.Length + 1;
        }
        return list;
    }
}