#if WINDOWS
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Services;
using Portal.Services;
using Portal.Views;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;

namespace Portal.Desktop;

internal static class WindowsJumpListService
{
    private const string AppUserModelId = "cc.tiouo.Portal";
    private const string PipeName = "cc.tiouo.Portal.JumpList";
    private const string CommandArgument = "--jump-list-command";
    private static readonly RecentPlayService RecentPlayService = new();
    private static readonly Queue<JumpListCommand> PendingCommands = [];
    private static readonly object CommandLock = new();
    private static bool _isReady;
    private static bool _isDrainingCommands;

    public static void SetAppUserModelId() => SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

    public static bool TryForwardToRunningInstance(string[] args)
    {
        if (!TryParseCommand(args, out var command))
            return false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(250);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            writer.Write(JsonSerializer.Serialize(command));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void StartCommandServer() => _ = Task.Run(ListenForCommandsAsync);

    public static void Initialize(string[] args)
    {
        App.UiLoaded += ui =>
        {
            _isReady = true;
            DrainPendingCommands();
            _ = RefreshAsync();
            InstanceManager.Instance.InstancesChanged += (_, _) => _ = RefreshAsync();
            InstanceManager.Instance.StatisticsChanged += (_, _) => _ = RefreshAsync();
        };

        if (TryParseCommand(args, out var command))
            QueueCommand(command);
    }

    private static async Task ListenForCommandsAsync()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json))
                    continue;
                if (JsonSerializer.Deserialize<JumpListCommand>(json) is { } command)
                    QueueCommand(command);
            }
            catch (JsonException)
            {
                // 客户端提前断开或发送了截断的负载；丢弃本次连接，继续监听。
            }
            catch (IOException)
            {
                // 管道被另一个实例占用，或客户端提前断开；稍后重试，避免空转。
                await Task.Delay(1000);
            }
            catch (Exception)
            {
                // 监听循环不能因意外异常终止，否则后续命令会启动重复实例。
                await Task.Delay(1000);
            }
        }
    }

    private static void QueueCommand(JumpListCommand command)
    {
        lock (CommandLock)
            PendingCommands.Enqueue(command);

        if (_isReady)
            Dispatcher.UIThread.Post(DrainPendingCommands);
    }

    private static void DrainPendingCommands()
    {
        lock (CommandLock)
        {
            if (_isDrainingCommands)
                return;

            _isDrainingCommands = true;
        }

        _ = DrainPendingCommandsAsync();
    }

    private static async Task DrainPendingCommandsAsync()
    {
        try
        {
            while (true)
            {
                JumpListCommand? command;
                lock (CommandLock)
                {
                    if (PendingCommands.Count == 0)
                    {
                        _isDrainingCommands = false;
                        return;
                    }

                    command = PendingCommands.Dequeue();
                }

                try
                {
                    await ExecuteAsync(command);
                }
                catch (Exception exception)
                {
                    Logger.Error($"执行 Jump List 命令失败：{exception}");
                }
            }
        }
        finally
        {
            var restart = false;
            lock (CommandLock)
            {
                _isDrainingCommands = false;
                restart = _isReady && PendingCommands.Count > 0;
            }

            if (restart)
                Dispatcher.UIThread.Post(DrainPendingCommands);
        }
    }

    private static async Task ExecuteAsync(JumpListCommand command)
    {
        var window = App.MainWindow;
        if (window == null)
            return;

        window.Show();
        window.Activate();
        if (command.Kind == JumpListCommandKind.NewTab)
        {
            var tab = new TabEntry(window, new NewTabPage());
            window.CreateTab(tab);
            window.SelectTab(tab);
            return;
        }

        if (command.Kind == JumpListCommandKind.Settings)
        {
            var tab = new TabEntry(window, new SettingPage());
            window.CreateTab(tab);
            window.SelectTab(tab);
            return;
        }

        var instance = InstanceManager.Instance.Instances.FirstOrDefault(item =>
            string.Equals(item.InstanceFolderPath, command.InstanceFolderPath, StringComparison.OrdinalIgnoreCase));
        if (instance == null)
            return;

        RecentPlayTarget? target = null;
        if (command.Kind == JumpListCommandKind.RecentPlay)
        {
            target = (await RecentPlayService.ScanAsync(InstanceManager.Instance.Instances))
                .FirstOrDefault(item => item.Type == command.TargetType && item.Id == command.TargetId &&
                                        string.Equals(item.Instance.InstanceFolderPath, command.InstanceFolderPath,
                                            StringComparison.OrdinalIgnoreCase));
            if (target == null)
                return;
        }

        _ = MinecraftLaunchService.LaunchAsync(instance, window, MinecraftLaunchOptionsFactory.Create(), target);
    }

    private static async Task RefreshAsync()
    {
        try
        {
            var items = new List<(string Category, string Title, string Description, JumpListCommand Command)>();
            var recentInstance = InstanceManager.Instance.Instances
                .Where(instance => instance.LastPlayTime != DateTime.MinValue)
                .OrderByDescending(instance => instance.LastPlayTime)
                .FirstOrDefault();
            if (recentInstance != null)
                items.Add(("继续游戏", recentInstance.InstanceName, "启动最近运行的实例",
                    new JumpListCommand(JumpListCommandKind.Continue, recentInstance.InstanceFolderPath)));

            items.Add(("任务", "新标签页", "在 Portal 中创建一个新标签页",
                new JumpListCommand(JumpListCommandKind.NewTab, null)));

            items.Add(("任务", "设置", "打开 Portal 设置",
                new JumpListCommand(JumpListCommandKind.Settings, null)));

            var recentPlay = (await RecentPlayService.ScanAsync(InstanceManager.Instance.Instances)).FirstOrDefault();
            if (recentPlay != null)
            {
                items.Add(("继续游戏", recentPlay.Name, $"{recentPlay.Instance.InstanceName}·{recentPlay.Details}",
                    new JumpListCommand(JumpListCommandKind.RecentPlay, recentPlay.Instance.InstanceFolderPath,
                        recentPlay.Type, recentPlay.Id)));
            }

            BuildJumpList(items);
        }
        catch (Exception exception)
        {
            // Jump Lists are an optional Windows shell integration and must not affect launching Portal.
            Logger.Error($"更新 Windows 任务栏 Jump List 失败：{exception}");
        }
    }

    private static void BuildJumpList(IEnumerable<(string Category, string Title, string Description, JumpListCommand Command)> items)
    {
        var destinationList = (ICustomDestinationList)new CDestinationList();
        destinationList.SetAppID(AppUserModelId);
        var objectArrayGuid = typeof(IObjectArray).GUID;
        destinationList.BeginList(out _, ref objectArrayGuid, out _);
        foreach (var category in items.GroupBy(item => item.Category))
        {
            var collection = new ObjectCollection();
            foreach (var item in category)
                collection.AddObject(CreateShellLink(item.Title, item.Description, item.Command));
            destinationList.AppendCategory(category.Key, (IObjectArray)collection);
        }
        destinationList.CommitList();
    }

    private static IShellLinkW CreateShellLink(string title, string description, JumpListCommand command)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(Environment.ProcessPath!);
        link.SetArguments($"{CommandArgument} {Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)))}");
        link.SetDescription(description);
        link.SetIconLocation(Environment.ProcessPath!, 0);
        var propertyStore = (IPropertyStore)link;
        var key = PropertyKeys.Title;
        var titleValue = new PropVariant(title);
        try
        {
            propertyStore.SetValue(ref key, ref titleValue);
            propertyStore.Commit();
        }
        finally
        {
            // 释放 PropVariant 持有的 CoTaskMem 字符串，避免每次刷新 Jump List 都泄漏。
            PropVariantClear(ref titleValue);
        }
        return link;
    }

    private static bool TryParseCommand(string[] args, out JumpListCommand command)
    {
        command = null!;
        var index = Array.IndexOf(args, CommandArgument);
        if (index < 0 || index + 1 >= args.Length)
            return false;
        try
        {
            command = JsonSerializer.Deserialize<JumpListCommand>(Encoding.UTF8.GetString(Convert.FromBase64String(args[index + 1])))!;
            return command != null;
        }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }

    private enum JumpListCommandKind { Continue, NewTab, Settings, RecentPlay }
    private sealed record JumpListCommand(JumpListCommandKind Kind, string? InstanceFolderPath,
        RecentPlayTargetType? TargetType = null, string? TargetId = null);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant variant);

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint maxSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removedItems);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray objects);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removedItems);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComVisible(true), Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection : IObjectArray
    {
        new void GetCount(out uint count);
        new void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        void AddFromArray(IObjectArray source);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComVisible(true), Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ObjectCollection : IObjectCollection
    {
        private readonly List<object> _items = [];

        public void GetCount(out uint count) => count = (uint)_items.Count;

        public void GetAt(uint index, ref Guid riid, out object item) => item = _items[(int)index];

        public void AddObject(object item) => _items.Add(item);

        public void AddFromArray(IObjectArray source)
        {
            source.GetCount(out var count);
            for (uint index = 0; index < count; index++)
            {
                var objectGuid = typeof(object).GUID;
                source.GetAt(index, ref objectGuid, out var item);
                _items.Add(item);
            }
        }

        public void RemoveObjectAt(uint index) => _items.RemoveAt((int)index);

        public void Clear() => _items.Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, out IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _valueType;
        [FieldOffset(8)] private IntPtr _pointerValue;
        public PropVariant(string value)
        {
            this = default;
            _valueType = 31; // VT_LPWSTR
            _pointerValue = Marshal.StringToCoTaskMemUni(value);
        }
    }

    private static class PropertyKeys
    {
        public static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    }

    [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
    private class CDestinationList;
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink;
}
#endif
