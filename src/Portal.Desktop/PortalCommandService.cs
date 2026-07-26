using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Portal.Module.Ipc;

namespace Portal.Desktop;

/// <summary>
/// 命令行 / portal:// 命令通道（跨平台）：
/// 第二个进程把命令通过命名管道转发给已运行的 Portal 实例；
/// 若没有运行中的实例，则正常启动界面并在 UI 加载完成后执行命令。
/// </summary>
internal static class PortalCommandService
{
    private const string PipeName = "xyz.tiouo.Portal.Command";

    /// <summary>
    /// 在 Main 最前面调用。返回 true 表示本进程只承担命令转发/帮助输出，应直接退出。
    /// </summary>
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

    public static void StartCommandServer() => _ = Task.Run(ListenForCommandsAsync);

    private static bool TryForwardToRunningInstance(PortalCommand command)
    {
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
                if (JsonSerializer.Deserialize<PortalCommand>(json) is { } command)
                    PortalCommandQueue.Enqueue(command);
            }
            catch (IOException)
            {
                // 管道被另一个实例占用，或客户端提前断开；稍后重试，避免空转。
                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// Portal.Desktop 在 Windows 上是 WinExe（无控制台）；从终端调用时挂到父进程控制台再输出。
    /// </summary>
    private static void WriteConsole(string message)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AttachConsole(AttachParentProcess);
            Console.WriteLine();
        }
        Console.WriteLine(message);
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
