using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Portal.Bedrock.Standard.Interface;

namespace Portal.Bedrock;

internal static class BedrockNativeLogMonitor
{
    public static void Start(string logPath, Func<Process?> processProvider, Action<string, BedrockLogLevel>? log)
    {
        if (log == null)
            return;
        _ = Task.Run(() => FollowAsync(logPath, processProvider, log)); // FollowAsync records every terminal failure.
    }

    private static async Task FollowAsync(string logPath, Func<Process?> processProvider,
        Action<string, BedrockLogLevel> log)
    {
        try
        {
            for (var attempt = 0; attempt < 300 && !File.Exists(logPath); attempt++)
                await Task.Delay(100).ConfigureAwait(false);
            if (!File.Exists(logPath))
            {
                log("未找到 PreloadCpp 运行日志文件", BedrockLogLevel.Warning);
                return;
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var waitingForProcess = 0;
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line != null)
                {
                    log($"[PreloadCpp] {line}", GetLevel(line));
                    continue;
                }

                var process = processProvider();
                if (process == null)
                {
                    if (waitingForProcess++ >= 300)
                        break;
                    await Task.Delay(100).ConfigureAwait(false);
                    continue;
                }

                if (!IsRunning(process))
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    if (await reader.ReadLineAsync().ConfigureAwait(false) is not { } finalLine) break;
                    log($"[PreloadCpp] {finalLine}", GetLevel(finalLine));
                    continue;
                }
                waitingForProcess = 0;
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError($"读取 PreloadCpp 日志失败：{logPath}{Environment.NewLine}{exception}");
            log($"读取 PreloadCpp 日志失败：{exception}", BedrockLogLevel.Warning);
        }
    }

    private static BedrockLogLevel GetLevel(string line)
    {
        if (line.Contains(" ERROR ", StringComparison.OrdinalIgnoreCase)) return BedrockLogLevel.Error;
        if (line.Contains(" WARNING ", StringComparison.OrdinalIgnoreCase)) return BedrockLogLevel.Warning;
        return BedrockLogLevel.Information;
    }

    private static bool IsRunning(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException exception)
        {
            Trace.TraceError($"检查 Minecraft 进程状态失败。{Environment.NewLine}{exception}");
            return false;
        }
    }
}
