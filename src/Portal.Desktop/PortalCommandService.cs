using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Portal.Core.Module.Ipc;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Desktop;

internal static partial class PortalCommandService
{
    private const string PipeName = "cc.tiouo.Portal.Command";

    private const int AttachParentProcess = -1;

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
                WriteConsole(
                    string.Format(CommonLanguageManager.Instance.desktop_commandService_argumentError.CurrentValue(), error, Environment.NewLine, Environment.NewLine, PortalCommandParser.GetUsageText()));
                return true;
            case PortalCliParseStatus.Command when command is not null:
                if (TryForwardToRunningInstance(command))
                {
                    WriteConsole(CommonLanguageManager.Instance.desktop_commandService_forwarded.CurrentValue());
                    return true;
                }

                PortalCommandQueue.Enqueue(command);
                return false;
            default:
                return false;
        }
    }

    public static void Initialize()
    {
        PortalCommandQueue.Initialize();
    }

    public static void StartCommandServer()
    {
        Logger.Info(LogLanguageManager.Instance.desktop_commandService_starting.CurrentValue());
        Task.Run(ListenForCommandsAsync).Forget(CommonLanguageManager.Instance.desktop_commandService_forgetLabel.CurrentValue());
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
            Logger.Debug(string.Format(LogLanguageManager.Instance.desktop_commandService_connectTimeout.CurrentValue(), Environment.NewLine, exception));
            return false;
        }
        catch (IOException exception)
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.desktop_commandService_connectFailed.CurrentValue(), Environment.NewLine, exception));
            return false;
        }
    }

    private static async Task ListenForCommandsAsync()
    {
        while (true)
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, Encoding.UTF8, false,
                    leaveOpen: true);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json))
                    continue;
                if (JsonSerializer.Deserialize<PortalCommand>(json) is not { } command) continue;
                Logger.Info(LogLanguageManager.Instance.desktop_commandService_received.CurrentValue());
                PortalCommandQueue.Enqueue(command);
            }
            catch (JsonException exception)
            {
                Logger.Warning(string.Format(LogLanguageManager.Instance.desktop_commandService_invalidPayload.CurrentValue(), Environment.NewLine, exception));
            }
            catch (IOException exception)
            {
                Logger.Warning(string.Format(LogLanguageManager.Instance.desktop_commandService_ioError.CurrentValue(), Environment.NewLine, exception));
                await Task.Delay(1000);
            }
            catch (Exception exception)
            {
                Logger.Error(LogLanguageManager.Instance.desktop_commandService_unexpectedError.CurrentValue(), exception);
                await Task.Delay(1000);
            }
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void AttachConsole(int processId);
}