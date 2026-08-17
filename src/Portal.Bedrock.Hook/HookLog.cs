using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Portal.Bedrock.Hook;

/// <summary>XUserHook 内部诊断日志：写入 <c>config\Portal\logs\hook-*.log</c>。</summary>
internal static class HookLog
{
    private static readonly object WriteLock = new();
    private static string? _path;

    public static void Initialize(string gameDir)
    {
        try
        {
            var directory = Path.Combine(gameDir, "config", "Portal", "logs");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, $"hook-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            Info($"XUserHook log started | pid={Environment.ProcessId} | path={_path}");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        if (_path == null)
            return;
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
            lock (WriteLock)
            {
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
