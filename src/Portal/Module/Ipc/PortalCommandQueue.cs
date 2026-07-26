using Avalonia.Threading;

namespace Portal.Module.Ipc;

/// <summary>
/// 外部命令队列：命令行/管道（Portal.Desktop）与 macOS 协议激活（App）都往这里入队，
/// UI 加载完成后统一在 UI 线程上执行。
/// </summary>
public static class PortalCommandQueue
{
    private static readonly Queue<PortalCommand> PendingCommands = [];
    private static readonly object CommandLock = new();
    private static bool _isReady;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        App.UiLoaded += _ =>
        {
            // _isReady 会被管道监听线程读取，必须与入队操作用同一把锁同步
            lock (CommandLock)
                _isReady = true;
            Dispatcher.UIThread.Post(DrainPendingCommands);
        };
    }

    public static void Enqueue(PortalCommand command)
    {
        bool isReady;
        lock (CommandLock)
        {
            PendingCommands.Enqueue(command);
            isReady = _isReady;
        }

        if (isReady)
            Dispatcher.UIThread.Post(DrainPendingCommands);
    }

    private static void DrainPendingCommands()
    {
        while (true)
        {
            PortalCommand? command;
            lock (CommandLock)
                command = PendingCommands.Count > 0 ? PendingCommands.Dequeue() : null;
            if (command == null)
                return;

            _ = PortalCommandExecutor.ExecuteAsync(command);
        }
    }
}
