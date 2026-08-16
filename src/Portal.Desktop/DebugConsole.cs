using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Portal.Const;
using Portal.Core.Const;

namespace Portal.Desktop;

internal static partial class DebugConsole
{
    public static void ShowIfEnabled()
    {
        if (!IsEnabled()) return;

        if (OperatingSystem.IsWindows())
        {
            if (AllocConsole())
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
            Console.Error.WriteLine($"无法读取调试模式配置：{exception.Message}");
            return false;
        }
    }

    private static void RedirectStandardOutput()
    {
        var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetOut(output);
        Console.SetError(error);
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Portal 调试终端已启动。");
    }

    private static void StartLinuxTerminal()
    {
        var command = $"echo 'Portal 调试终端已启动。正在等待应用日志...'; tail -n 0 -F {QuoteForShell(Path.Combine(ConfigPath.LogFolderPath, "latest.log"))}; exec bash";
        foreach (var (fileName, arguments) in new[]
                 {
                     ("gnome-terminal", ["--", "bash", "-c", command]),
                     ("konsole", ["-e", "bash", "-c", command]),
                     ("xterm", new[] { "-e", "bash", "-c", command })
                 })
        {
            try
            {
                var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
                foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
                if (Process.Start(startInfo) is not null) return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                
            }
        }
    }

    private static void StartMacOsTerminal()
    {
        var command = $"echo 'Portal 调试终端已启动。正在等待应用日志...'; tail -n 0 -F {QuoteForShell(Path.Combine(ConfigPath.LogFolderPath, "latest.log"))}";
        try
        {
            var startInfo = new ProcessStartInfo("osascript") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"tell application \"Terminal\" to do script {QuoteForAppleScript(command)}");
            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            
        }
    }

    private static string QuoteForShell(string value) => $"'{value.Replace("'", "'\\\"'\\\"'")}'";

    private static string QuoteForAppleScript(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();
}
