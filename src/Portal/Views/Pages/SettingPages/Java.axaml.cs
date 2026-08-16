using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Portal.Const;
using Portal.Core.App.Service.SystemResources;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Operations.Java;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("Java 环境", "设置/Java 环境", "Java")]
public partial class Java : DataUserControl, INotifyPropertyChanged, IDisposable
{
    private int _totalMemoryMb;
    private int _availableMemoryMb;
    private readonly DispatcherTimer _memoryRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _isDisposed;

    private event PropertyChangedEventHandler? MemoryStatusChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => MemoryStatusChanged += value;
        remove => MemoryStatusChanged -= value;
    }

    public bool HasMemoryStatus { get; private set; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsWindowsOrLinux => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
    public int TotalMemoryMb => _totalMemoryMb;
    public GridLength SystemMemoryWidth => CreateMemoryWidth(SystemUsedMemoryMb);
    public GridLength MinecraftMemoryWidth => CreateMemoryWidth(Data.ConfigEntry.MinecraftMaxMemory);

    public GridLength RemainingMemoryWidth =>
        new(Math.Max(0, TotalMemoryMb - SystemUsedMemoryMb - Data.ConfigEntry.MinecraftMaxMemory), GridUnitType.Star);

    public int SystemUsedMemoryMb => Math.Max(0, _totalMemoryMb - _availableMemoryMb);
    public string SystemMemoryDescription => $"系统已使用 {SystemUsedMemoryMb:N0} MB";
    public string MinecraftMemoryDescription => $"Minecraft {Data.ConfigEntry.MinecraftMaxMemory:N0} MB";
    public bool HasMemoryWarning => HasMemoryStatus && Data.ConfigEntry.MinecraftMaxMemory > _availableMemoryMb;

    public Java()
    {
        InitializeComponent();
        DataContext = this;
        _memoryRefreshTimer.Tick += MemoryRefreshTimer_OnTick;
        Data.ConfigEntry.PropertyChanged += ConfigEntry_PropertyChanged;
        Slider.Value = Data.ConfigEntry.MinecraftMaxMemory;
        Slider.ValueChanged += (_, _) => Data.ConfigEntry.MinecraftMaxMemory = (int)Slider.Value;
        Loaded += (_, _) => Slider.Value = Data.ConfigEntry.MinecraftMaxMemory;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshMemoryStatus();
        _memoryRefreshTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _memoryRefreshTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void ConfigEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Data.ConfigEntry.MinecraftMaxMemory)) return;

        OnPropertyChanged(nameof(MinecraftMemoryWidth));
        OnPropertyChanged(nameof(RemainingMemoryWidth));
        OnPropertyChanged(nameof(MinecraftMemoryDescription));
        OnPropertyChanged(nameof(HasMemoryWarning));
    }

    private void MemoryRefreshTimer_OnTick(object? sender, EventArgs e) => RefreshMemoryStatus();

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _memoryRefreshTimer.Stop();
        _memoryRefreshTimer.Tick -= MemoryRefreshTimer_OnTick;
        Data.ConfigEntry.PropertyChanged -= ConfigEntry_PropertyChanged;
        DataContext = null;
    }

    private void RefreshMemoryStatus()
    {
        HasMemoryStatus = false;
        _totalMemoryMb = 0;
        _availableMemoryMb = 0;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && GetMemoryStatus(out var memoryStatus))
            {
                _totalMemoryMb = ToMegabytes(memoryStatus.TotalPhysical);
                _availableMemoryMb = ToMegabytes(memoryStatus.AvailablePhysical);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                TryGetLinuxMemoryStatus(out _totalMemoryMb, out _availableMemoryMb);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                TryGetMacOsMemoryStatus(out _totalMemoryMb, out _availableMemoryMb);
            }

            HasMemoryStatus = _totalMemoryMb > 0 && _availableMemoryMb >= 0;
        }
        catch (Exception)
        {
            HasMemoryStatus = false;
        }

        if (!HasMemoryStatus)
        {
            _totalMemoryMb = 65536;
            _availableMemoryMb = 65536;

            HasMemoryStatus = false;
        }

        OnPropertyChanged(nameof(HasMemoryStatus));
        OnPropertyChanged(nameof(TotalMemoryMb));
        OnPropertyChanged(nameof(SystemMemoryWidth));
        OnPropertyChanged(nameof(MinecraftMemoryWidth));
        OnPropertyChanged(nameof(RemainingMemoryWidth));
        OnPropertyChanged(nameof(SystemMemoryDescription));
        OnPropertyChanged(nameof(HasMemoryWarning));
    }

    private GridLength CreateMemoryWidth(int memoryMb) =>
        new(Math.Min(Math.Max(0, memoryMb), TotalMemoryMb), GridUnitType.Star);

    private static int ToMegabytes(ulong bytes) => (int)Math.Min(int.MaxValue, bytes / 1024 / 1024);

    private static bool TryGetLinuxMemoryStatus(out int totalMemoryMb, out int availableMemoryMb)
    {
        totalMemoryMb = 0;
        availableMemoryMb = 0;

        const string memoryInfoPath = "/proc/meminfo";
        if (!File.Exists(memoryInfoPath)) return false;

        foreach (var line in File.ReadLines(memoryInfoPath))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0) continue;

            var key = line[..separatorIndex];
            var valueText = line[(separatorIndex + 1)..].Trim().Split(' ')[0];
            if (!ulong.TryParse(valueText, out var valueKb)) continue;

            if (key == "MemTotal") totalMemoryMb = ToMegabytes(valueKb * 1024);
            if (key == "MemAvailable") availableMemoryMb = ToMegabytes(valueKb * 1024);
        }

        return totalMemoryMb > 0 && availableMemoryMb >= 0;
    }

    private static bool TryGetMacOsMemoryStatus(out int totalMemoryMb, out int availableMemoryMb)
    {
        totalMemoryMb = 0;
        availableMemoryMb = 0;

        if (!TryGetSysctlUnsignedLong("hw.memsize", out var totalMemoryBytes)) return false;

        var vmStatistics = new VmStatistics();
        var count = (uint)(Marshal.SizeOf<VmStatistics>() / sizeof(uint));
        if (HostStatistics(MachHostSelf(), HostVmInfo, ref vmStatistics, ref count) != 0) return false;

        var pageSize = Sysconf(_ScPagesize);
        if (pageSize <= 0) return false;

        totalMemoryMb = ToMegabytes(totalMemoryBytes);
        availableMemoryMb =
            ToMegabytes((ulong)(vmStatistics.FreeCount + vmStatistics.InactiveCount + vmStatistics.PurgeableCount +
                                vmStatistics.SpeculativeCount) * (ulong)pageSize);
        return totalMemoryMb > 0 && availableMemoryMb >= 0;
    }

    private static bool TryGetSysctlUnsignedLong(string name, out ulong value)
    {
        value = 0;
        var size = (nuint)sizeof(ulong);
        return SysctlByName(name, ref value, ref size, IntPtr.Zero, 0) == 0 && value > 0;
    }

    private void OnPropertyChanged(string propertyName) =>
        MemoryStatusChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusExNative(ref MemoryStatusEx buffer);

    private static bool GetMemoryStatus(out MemoryStatusEx memoryStatus)
    {
        memoryStatus = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusExNative(ref memoryStatus);
    }

    private const int HostVmInfo = 2;
    private const int _ScPagesize = 29;

    [StructLayout(LayoutKind.Sequential)]
    private struct VmStatistics
    {
        public uint FreeCount;
        public uint ActiveCount;
        public uint InactiveCount;
        public uint WireCount;
        public uint ZeroFillCount;
        public uint Reactivations;
        public uint PageIns;
        public uint PageOuts;
        public uint Faults;
        public uint CowFaults;
        public uint Lookups;
        public uint Hits;
        public uint Purges;
        public uint PurgeableCount;
        public uint SpeculativeCount;
    }

    [DllImport("libSystem.B.dylib", EntryPoint = "sysctlbyname")]
    private static extern int SysctlByName(string name, ref ulong oldValue, ref nuint oldValueLength, IntPtr newValue,
        nuint newValueLength);

    [DllImport("libSystem.B.dylib", EntryPoint = "mach_host_self")]
    private static extern IntPtr MachHostSelf();

    [DllImport("libSystem.B.dylib", EntryPoint = "host_statistics")]
    private static extern int HostStatistics(IntPtr host, int flavor, ref VmStatistics hostInfo,
        ref uint hostInfoCount);

    [DllImport("libSystem.B.dylib", EntryPoint = "sysconf")]
    private static extern long Sysconf(int name);

    private async void OptimizeMemory_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || !OperatingSystem.IsWindows()) return;

        try
        {
            topLevel.Notice("正在优化系统内存");
            var result = await MemoryOptimizationService.OptimizeAsync();
            RefreshMemoryStatus();
            topLevel.Notice($"内存优化完成，已释放 {FormatMemory(result.ReclaimedBytes)}", NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice($"内存优化失败：{ex.Message}", NotificationType.Error);
        }
    }

    private static string FormatMemory(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:F1} GB 内存"
        : $"{bytes / 1024d / 1024:F0} MB 内存";

    private async void AddJava_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        await AddJavaAsync(topLevel);
    }

    private async void AutoScan_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            topLevel.Notice("正在扫描中");
            var result = await JavaRuntimeOperations.ScanAndAddAsync(Data.ConfigEntry.JavaRuntimes);
            if (Data.ConfigEntry.DefaultJavaRuntime == null)
                Data.ConfigEntry.DefaultJavaRuntime = Data.ConfigEntry.JavaRuntimes.FirstOrDefault();

            topLevel.Notice(
                $"扫描完成：新增 {result.AddedCount} 个 Java，重复 {result.DuplicateCount} 个",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice($"Java 扫描失败：{ex.Message}", NotificationType.Error);
        }
    }

    private async void DeepScan_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        // 防止重复点击
        DeepScanButton.IsEnabled = false;
        try
        {
            var (task, resultTask) = JavaRuntimeOperations.CreateDeepScanTask(Data.ConfigEntry.JavaRuntimes);
            task.Start();
            topLevel.Notice("强力扫描已启动，可在右上角任务列表中取消");

            (int Added, int Duplicate) result;
            try
            {
                result = await resultTask;
            }
            catch (OperationCanceledException)
            {
                // 取消时也可能抛出，此时任务描述里已经有结果
                result = (0, 0);
            }

            if (Data.ConfigEntry.DefaultJavaRuntime == null)
                Data.ConfigEntry.DefaultJavaRuntime = Data.ConfigEntry.JavaRuntimes.FirstOrDefault();

            if (task.Status == Tio.Avalonia.Standard.Modules.Tasks.ManagedTaskStatus.Cancelled)
            {
                topLevel.Notice(
                    $"扫描已取消，已找到的 Java 已添加：{result.Added} 个新增，{result.Duplicate} 个重复",
                    NotificationType.Warning);
            }
            else
            {
                topLevel.Notice(
                    $"强力扫描完成：新增 {result.Added} 个 Java，重复 {result.Duplicate} 个",
                    NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            topLevel.Notice($"强力扫描失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            DeepScanButton.IsEnabled = true;
        }
    }

    private async Task AddJavaAsync(TopLevel topLevel)
    {
        try
        {
            var result = await JavaRuntimeOperations.AddFromPickerAsync(topLevel, Data.ConfigEntry.JavaRuntimes);
            if (result == null) return;

            if (!result.IsValid)
            {
                topLevel.Notice("无法识别该 Java 可执行文件", NotificationType.Error);
                return;
            }

            if (Data.ConfigEntry.DefaultJavaRuntime is null)
                Data.ConfigEntry.DefaultJavaRuntime = result.JavaRuntime;

            if (result.IsDuplicate)
            {
                topLevel.Notice("该 Java 已在列表中", NotificationType.Warning);
                return;
            }

            topLevel.Notice("Java 已添加", NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice($"添加 Java 失败：{ex.Message}", NotificationType.Error);
        }
    }

    private void RemoveJava_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: JavaRuntimeEntry java }) return;

        Data.ConfigEntry.DefaultJavaRuntime = JavaRuntimeOperations.Remove(
            Data.ConfigEntry.JavaRuntimes,
            java,
            Data.ConfigEntry.DefaultJavaRuntime);

        this.AsTopLevel().Notice(new NotificationOptions
        {
            Content = $"已移除 Java：{java.DisplayName}",
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons =
            [
                new OperateButtonEntry("撤销", _ =>
                {
                    JavaRuntimeOperations.Restore(Data.ConfigEntry.JavaRuntimes, java);
                    Data.ConfigEntry.DefaultJavaRuntime = java;
                }, true)
            ]
        });
    }
}
