using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Portal.Core.Const;
using Portal.Localization;

namespace Portal.Desktop;

internal static partial class DebugConsole
{
    private const string TerminalHostFlag = "--portal-debug-terminal-host";
    private static readonly TimeSpan TerminalStartTimeout = TimeSpan.FromSeconds(5);

    public static bool TryRunTerminalHost(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args is not [TerminalHostFlag, var responsePath, var parentProcessIdText])
            return false;

        if (!uint.TryParse(parentProcessIdText, out var parentProcessId) || parentProcessId == 0)
            return true;

        if (!AttachConsole(uint.MaxValue))
        {
            File.WriteAllText(responsePath, "0");
            return true;
        }

        File.WriteAllText(responsePath, Environment.ProcessId.ToString());
        try
        {
            using var parentProcess = Process.GetProcessById((int)parentProcessId);
            parentProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The Portal process exited before the terminal host started waiting.
        }
        finally
        {
            File.Delete(responsePath);
        }

        return true;
    }

    public static void ShowIfEnabled()
    {
        if (!IsEnabled()) return;

        if (OperatingSystem.IsWindows())
        {
            if (TryAttachWindowsTerminal() || AllocConsole())
                RedirectStandardOutput();
            return;
        }

        if (OperatingSystem.IsLinux())
            StartLinuxTerminal();
        else if (OperatingSystem.IsMacOS())
            StartMacOsTerminal();
    }

    private static bool IsEnabled()
    {
        try
        {
            if (!File.Exists(ConfigPath.DebugConsoleDataPath)) return false;
            return File.ReadAllText(ConfigPath.DebugConsoleDataPath) == "true";
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(string.Format(CommonLanguageManager.Instance.desktop_debugConsole_configReadFailed.CurrentValue(), exception.Message));
            return false;
        }
    }

    private static void RedirectStandardOutput()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var output = new StreamWriter(OpenConsoleOutput()) { AutoFlush = true };
        var error = new StreamWriter(OpenConsoleOutput()) { AutoFlush = true };
        Console.SetOut(output);
        Console.SetError(error);
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new TextWriterTraceListener(output));
        Trace.AutoFlush = true;
        Console.WriteLine(CommonLanguageManager.Instance.desktop_debugConsole_started.CurrentValue());
    }

    private static Stream OpenConsoleOutput()
    {
        return OperatingSystem.IsWindows()
            ? new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite)
            : Console.OpenStandardOutput();
    }

    private static bool TryAttachWindowsTerminal()
    {
        string? responsePath = null;
        try
        {
            var exchangeDirectory = Path.Combine(Path.GetTempPath(), "Portal", "terminal-host");
            Directory.CreateDirectory(exchangeDirectory);
            responsePath = Path.Combine(exchangeDirectory, $"{Guid.NewGuid():N}.response");
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath)) return false;

            var command = $"start \"\" /wait /b {QuoteForCommand(executablePath)} {TerminalHostFlag} " +
                          $"{QuoteForCommand(responsePath)} {Environment.ProcessId}";
            var startInfo = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            foreach (var argument in new[]
                     {
                         "-w", "-1", "new-tab", "--title",
                         CommonLanguageManager.Instance.desktop_debugConsole_terminalTitle.CurrentValue(),
                         "--suppressApplicationTitle", "cmd.exe", "/d", "/s", "/c", command
                     })
                startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TerminalStartTimeout)
            {
                if (File.Exists(responsePath) &&
                    uint.TryParse(File.ReadAllText(responsePath), out var hostProcessId))
                {
                    File.Delete(responsePath);
                    if (hostProcessId == 0) return false;

                    FreeConsole();
                    return AttachConsole(hostProcessId);
                }

                Thread.Sleep(50);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            if (responsePath is not null)
                try
                {
                    File.Delete(responsePath);
                }
                catch (IOException)
                {
                }
        }

        return false;
    }

    private static string QuoteForCommand(string value)
    {
        return $"\"{value.Replace("%", "%%")}\"";
    }

    private static void StartLinuxTerminal()
    {
        var command =
            string.Format(CommonLanguageManager.Instance.desktop_debugConsole_linuxCommand.CurrentValue(), QuoteForShell(Path.Combine(ConfigPath.LogFolderPath, "latest.log")));
        foreach (var (fileName, arguments) in new[]
                 {
                     ("gnome-terminal", ["--", "bash", "-c", command]),
                     ("konsole", ["-e", "bash", "-c", command]),
                     ("xterm", new[] { "-e", "bash", "-c", command })
                 })
            try
            {
                var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
                foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
                if (Process.Start(startInfo) is not null) return;
            }
            catch (Win32Exception)
            {
            }
    }

    private static void StartMacOsTerminal()
    {
        var command =
            string.Format(CommonLanguageManager.Instance.desktop_debugConsole_macosCommand.CurrentValue(), QuoteForShell(Path.Combine(ConfigPath.LogFolderPath, "latest.log")));
        try
        {
            var startInfo = new ProcessStartInfo("osascript") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"tell application \"Terminal\" to do script {QuoteForAppleScript(command)}");
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
        }
    }

    private static string QuoteForShell(string value)
    {
        return $"'{value.Replace("'", "'\\\"'\\\"'")}'";
    }

    private static string QuoteForAppleScript(string value)
    {
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
