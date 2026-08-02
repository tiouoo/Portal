using System.Runtime.InteropServices;
using System.Text;

namespace Portal.Core.SystemResources;

/// <summary>
/// Windows GPU 使用率采集。通过 PDH（性能数据助手）API 读取
/// <c>\GPU Engine(*)\Utilization Percentage</c> 性能计数器，
/// 与任务管理器显示的 GPU 使用率一致。
/// 仅 Windows 10 1903+ 可用；其他平台或采集失败时返回 null。
/// </summary>
internal static class GpuUsageProvider
{
    private static IntPtr _query = IntPtr.Zero;
    private static readonly List<IntPtr> _counters = [];
    private static bool _initialized;
    private static bool _hasBaselineSample;

    /// <summary>返回 0-100 的 GPU 占用百分比。首次调用返回 null（需两次采样取差值）。</summary>
    public static float? GetUsage()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            return GetWindowsUsage();
        }
        catch
        {
            return null;
        }
    }

    // ---------- PDH 互操作 ----------

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_NO_DATA = 0x800007D5;
    private const uint PDH_INVALID_DATA = 0xC0000BC6;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PDH_FMT_COUNTERVALUE
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
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPathW(string? machine, string wildcardPath, IntPtr expandedPathList, ref uint bufferSize, uint flags);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    // ---------- 采集逻辑 ----------

    private static float? GetWindowsUsage()
    {
        // 首次调用：构建 query 并采集基准样本
        if (!_initialized)
        {
            BuildQuery();
            _initialized = true;
            if (_query != IntPtr.Zero)
                PdhCollectQueryData(_query); // 基准样本，无法获取格式化值
            return null;
        }

        if (_query == IntPtr.Zero || _counters.Count == 0)
        {
            // 计数器不存在（旧版 Windows 或无 GPU 驱动），尝试重建一次
            BuildQuery();
            if (_query == IntPtr.Zero || _counters.Count == 0)
                return null;
            PdhCollectQueryData(_query);
            return null;
        }

        // 采集当前样本
        var collectResult = PdhCollectQueryData(_query);
        if (collectResult == PDH_NO_DATA)
        {
            // 所有实例都已失效，重建
            BuildQuery();
            if (_query == IntPtr.Zero || _counters.Count == 0)
                return null;
            PdhCollectQueryData(_query);
            return null;
        }

        if (collectResult != 0)
            return null;

        // 取所有引擎中使用率的最大值（与任务管理器的 GPU 使用率口径一致）
        double max = 0;
        bool anyValid = false;
        bool anyStale = false;

        foreach (var counter in _counters)
        {
            var result = PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var value);
            if (result == 0 && value.CStatus == 0)
            {
                anyValid = true;
                if (value.DoubleValue > max)
                    max = value.DoubleValue;
            }
            else if (result == PDH_INVALID_DATA)
            {
                // 实例已失效，标记需要重建
                anyStale = true;
            }
        }

        if (anyStale)
            BuildQuery();

        return anyValid ? (float)Math.Clamp(max, 0, 100) : null;
    }

    /// <summary>
    /// 展开 <c>\GPU Engine(*)\Utilization Percentage</c> 通配符路径，
    /// 为每个引擎实例创建计数器。
    /// </summary>
    private static void BuildQuery()
    {
        // 清理旧的 query
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counters.Clear();
        }

        // 展开通配符路径，获取所有 GPU Engine 实例
        const string wildcardPath = @"\GPU Engine(*)\Utilization Percentage";
        uint bufSize = 0;
        uint status = PdhExpandWildCardPathW(null, wildcardPath, IntPtr.Zero, ref bufSize, 0);
        if (status != PDH_MORE_DATA || bufSize == 0)
            return;

        IntPtr buffer = Marshal.AllocHGlobal((int)bufSize * 2);
        try
        {
            status = PdhExpandWildCardPathW(null, wildcardPath, buffer, ref bufSize, 0);
            if (status != 0)
                return;

            var paths = ReadMultiString(buffer, (int)bufSize);
            if (paths.Count == 0)
                return;

            // 创建 query
            status = PdhOpenQuery(IntPtr.Zero, 0, out _query);
            if (status != 0)
            {
                _query = IntPtr.Zero;
                return;
            }

            foreach (var path in paths)
            {
                status = PdhAddCounterW(_query, path, 0, out var counter);
                if (status == 0)
                    _counters.Add(counter);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>解析 PDH 返回的双 null 结尾的宽字符串列表。</summary>
    private static List<string> ReadMultiString(IntPtr buffer, int sizeChars)
    {
        var result = new List<string>();
        int offset = 0;
        while (offset < sizeChars)
        {
            string? s = Marshal.PtrToStringUni(buffer + offset * 2);
            if (string.IsNullOrEmpty(s))
                break;
            result.Add(s);
            offset += s.Length + 1;
        }
        return result;
    }
}
