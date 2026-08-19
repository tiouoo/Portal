using Avalonia.Threading;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Core.Module.Ipc;

public static class PortalCommandQueue
{
    private static readonly Queue<PortalCommand> PendingCommands = [];
    private static readonly object CommandLock = new();
    private static bool _isReady;
    private static bool _initialized;

    public static Func<PortalCommand, Task>? ExecutionHandler { get; set; }
    public static event Action? UiLoaded;

    public static void MarkUiLoaded()
    {
        UiLoaded?.Invoke();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        Logger.Info(LogLanguageManager.Instance.ipc_queueInitializeStart.CurrentValue());
        _initialized = true;
        UiLoaded += () =>
        {
            lock (CommandLock)
            {
                _isReady = true;
            }

            Logger.Info(LogLanguageManager.Instance.ipc_queueUiReadyDrain.CurrentValue());
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
            Logger.Info(string.Format(LogLanguageManager.Instance.ipc_queueCommandEnqueued.CurrentValue(), PendingCommands.Count));
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
            {
                command = PendingCommands.Count > 0 ? PendingCommands.Dequeue() : null;
            }

            if (command == null)
                return;

            if (ExecutionHandler is not null)
                ExecutionHandler(command).Forget(CommonLanguageManager.Instance.ipc_executeExternalCommand.CurrentValue());
        }
    }
}