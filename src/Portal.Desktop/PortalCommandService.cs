using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Portal.Module.Ipc;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Desktop;

internal static partial class PortalCommandService
{
    private const string PipeName = "cc.tiouo.Portal.Command";
    
    public static bool TryHandleStartupArgs(string[] args)
    {
        switch (PortalCommandParser.Parse(args, out var command, out var error))
        {
            case PortalCliParseStatus.NotACommand:
                return false;
            case PortalCliParseStatus.Help:
                WriteConsole(PortalCommandParser.GetUsageText());
                return true;
            case PortalCliParseStatus.Error:
                WriteConsole($"参数错误：{error}{Environment.NewLine}{Environment.NewLine}{PortalCommandParser.GetUsageText()}");
                return true;
            case PortalCliParseStatus.Command when command is not null:
                if (TryForwardToRunningInstance(command))
                {
                    WriteConsole("已将命令转发给正在运行的 Portal 实例。");
                    return true;
                }
                PortalCommandQueue.Enqueue(command);
                return false;
            default:
                return false;
        }
    }

    public static void Initialize() => PortalCommandQueue.Initialize();

    public static void StartCommandServer()
    {
        Logger.Info("正在启动命令行命名管道服务。");
        Task.Run(ListenForCommandsAsync).Forget("命令行命名管道服务");
    }
    
    internal static bool TryForwardToRunningInstance(PortalCommand command, int attempts = 1)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (TryForwardOnce(command))
                return true;
            if (attempt + 1 < attempts)
                Thread.Sleep(250);
        }
        return false;
    }

    private static bool TryForwardOnce(PortalCommand command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(250);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true);
            writer.AutoFlush = true;
            writer.Write(JsonSerializer.Serialize(command));
            return true;
        }
        catch (TimeoutException exception)
        {
            Logger.Debug($"连接已有 Portal 命令服务超时。{Environment.NewLine}{exception}");
            return false;
        }
        catch (IOException exception)
        {
            Logger.Debug($"无法连接已有 Portal 命令服务。{Environment.NewLine}{exception}");
            return false;
        }
    }

    private static async Task ListenForCommandsAsync()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json))
                    continue;
                if (JsonSerializer.Deserialize<PortalCommand>(json) is not { } command) continue;
                Logger.Info("已从命名管道接收到外部命令。");
                PortalCommandQueue.Enqueue(command);
            }
            catch (JsonException exception)
            {
                Logger.Warning($"命令行命名管道收到无效负载，已丢弃。{Environment.NewLine}{exception}");
            }
            catch (IOException exception)
            {
                Logger.Warning($"命令行命名管道发生 I/O 错误，1 秒后重试。{Environment.NewLine}{exception}");
                await Task.Delay(1000);
            }
            catch (Exception exception)
            {
                Logger.Error("命令行命名管道发生未预期错误，1 秒后重试。", exception);
                await Task.Delay(1000);
            } 
        }
        // ReSharper disable once FunctionNeverReturns
    }
    
    internal static void WriteConsole(string message)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AttachConsole(AttachParentProcess);
            Console.WriteLine();
        }
        Console.WriteLine(message);
    }

    private const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void AttachConsole(int processId);
}
