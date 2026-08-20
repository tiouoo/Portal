using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Portal.Core.Const;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Module.AggregatedSearch;
using Portal.Core.Services.SystemResources;
using Portal.Localization;
using Portal.ViewModels;
using Portal.Views.Components.Operations.Java;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_java", "pages_javaPath", "Java")]
public partial class Java : Dsc, INotifyPropertyChanged, IDisposable
{
    private const int HostVmInfo = 2;
    private const int _ScPagesize = 29;
    private readonly DispatcherTimer _memoryRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private int _availableMemoryMb;
    private bool _isDisposed;
    private int _totalMemoryMb;

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

    public bool HasMemoryStatus { get; private set; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsWindowsOrLinux => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
    public int TotalMemoryMb => _totalMemoryMb;
    public GridLength SystemMemoryWidth => CreateMemoryWidth(SystemUsedMemoryMb);
    public GridLength MinecraftMemoryWidth => CreateMemoryWidth(Data.ConfigEntry.MinecraftMaxMemory);

    public GridLength RemainingMemoryWidth =>
        new(Math.Max(0, TotalMemoryMb - SystemUsedMemoryMb - Data.ConfigEntry.MinecraftMaxMemory), GridUnitType.Star);

    public int SystemUsedMemoryMb => Math.Max(0, _totalMemoryMb - _availableMemoryMb);
    public string SystemMemoryDescription =>
        string.Format(CommonLanguageManager.Instance.javaPage_systemMemoryUsed.CurrentValue(), SystemUsedMemoryMb);
    public string MinecraftMemoryDescription =>
        string.Format(CommonLanguageManager.Instance.javaPage_minecraftMemory.CurrentValue(),
            Data.ConfigEntry.MinecraftMaxMemory);
    public bool HasMemoryWarning => HasMemoryStatus && Data.ConfigEntry.MinecraftMaxMemory > _availableMemoryMb;

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

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => MemoryStatusChanged += value;
        remove => MemoryStatusChanged -= value;
    }

    private event PropertyChangedEventHandler? MemoryStatusChanged;

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

    private void MemoryRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        RefreshMemoryStatus();
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

    private GridLength CreateMemoryWidth(int memoryMb)
    {
        return new GridLength(Math.Min(Math.Max(0, memoryMb), TotalMemoryMb), GridUnitType.Star);
    }

    private static int ToMegabytes(ulong bytes)
    {
        return (int)Math.Min(int.MaxValue, bytes / 1024 / 1024);
    }

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
            ToMegabytes((vmStatistics.FreeCount + vmStatistics.InactiveCount + vmStatistics.PurgeableCount +
                         vmStatistics.SpeculativeCount) * (ulong)pageSize);
        return totalMemoryMb > 0 && availableMemoryMb >= 0;
    }

    private static bool TryGetSysctlUnsignedLong(string name, out ulong value)
    {
        value = 0;
        var size = (nuint)sizeof(ulong);
        return SysctlByName(name, ref value, ref size, IntPtr.Zero, 0) == 0 && value > 0;
    }

    private void OnPropertyChanged(string propertyName)
    {
        MemoryStatusChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusExNative(ref MemoryStatusEx buffer);

    private static bool GetMemoryStatus(out MemoryStatusEx memoryStatus)
    {
        memoryStatus = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusExNative(ref memoryStatus);
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
            topLevel.Notice(CommonLanguageManager.Instance.javaPage_optimizingMemory.CurrentValue());
            var result = await MemoryOptimizationService.OptimizeAsync();
            RefreshMemoryStatus();
            topLevel.Notice(string.Format(
                CommonLanguageManager.Instance.javaPage_optimizationComplete.CurrentValue(),
                FormatMemory(result.ReclaimedBytes)), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.javaPage_optimizationFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
        }
    }

    private static string FormatMemory(long bytes)
    {
        return bytes >= 1024L * 1024 * 1024
            ? string.Format(CommonLanguageManager.Instance.javaPage_memoryGb.CurrentValue(),
                bytes / 1024d / 1024 / 1024)
            : string.Format(CommonLanguageManager.Instance.javaPage_memoryMb.CurrentValue(),
                bytes / 1024d / 1024);
    }

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
            topLevel.Notice(CommonLanguageManager.Instance.javaPage_scanning.CurrentValue());
            var result = await JavaRuntimeOperations.ScanAndAddAsync(Data.ConfigEntry.JavaRuntimes);
            if (Data.ConfigEntry.DefaultJavaRuntime == null)
                Data.ConfigEntry.DefaultJavaRuntime = Data.ConfigEntry.JavaRuntimes.FirstOrDefault();

            topLevel.Notice(
                string.Format(CommonLanguageManager.Instance.javaPage_scanComplete.CurrentValue(),
                    result.AddedCount, result.DuplicateCount),
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.javaPage_scanFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
        }
    }

    private async void DeepScan_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;


        DeepScanButton.IsEnabled = false;
        try
        {
            var (task, resultTask) = JavaRuntimeOperations.CreateDeepScanTask(Data.ConfigEntry.JavaRuntimes);
            task.Start();
            topLevel.Notice(CommonLanguageManager.Instance.javaPage_deepScanStarted.CurrentValue());

            (int Added, int Duplicate) result;
            try
            {
                result = await resultTask;
            }
            catch (OperationCanceledException)
            {
                result = (0, 0);
            }

            if (Data.ConfigEntry.DefaultJavaRuntime == null)
                Data.ConfigEntry.DefaultJavaRuntime = Data.ConfigEntry.JavaRuntimes.FirstOrDefault();

            if (task.Status == ManagedTaskStatus.Cancelled)
                topLevel.Notice(
                    string.Format(CommonLanguageManager.Instance.javaPage_deepScanCancelled.CurrentValue(),
                        result.Added, result.Duplicate),
                    NotificationType.Warning);
            else
                topLevel.Notice(
                    string.Format(CommonLanguageManager.Instance.javaPage_deepScanComplete.CurrentValue(),
                        result.Added, result.Duplicate),
                    NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.javaPage_deepScanFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
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
                topLevel.Notice(CommonLanguageManager.Instance.javaPage_unrecognizedJava.CurrentValue(),
                    NotificationType.Error);
                return;
            }

            if (Data.ConfigEntry.DefaultJavaRuntime is null)
                Data.ConfigEntry.DefaultJavaRuntime = result.JavaRuntime;

            if (result.IsDuplicate)
            {
                topLevel.Notice(CommonLanguageManager.Instance.javaPage_javaDuplicate.CurrentValue(),
                    NotificationType.Warning);
                return;
            }

            topLevel.Notice(CommonLanguageManager.Instance.javaPage_javaAdded.CurrentValue(), NotificationType.Success);
        }
        catch (Exception ex)
        {
            topLevel.Notice(string.Format(CommonLanguageManager.Instance.javaPage_addJavaFailed.CurrentValue(),
                ex.Message), NotificationType.Error);
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
            Content = string.Format(CommonLanguageManager.Instance.javaPage_javaRemoved.CurrentValue(),
                java.DisplayName),
            Type = NotificationType.Success,
            Expiration = TimeSpan.FromSeconds(3),
            OperateButtons =
            [
                new OperateButtonEntry(CommonLanguageManager.Instance.titleBar_undo.CurrentValue(), _ =>
                {
                    JavaRuntimeOperations.Restore(Data.ConfigEntry.JavaRuntimes, java);
                    Data.ConfigEntry.DefaultJavaRuntime = java;
                }, true)
            ]
        });
    }

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
}