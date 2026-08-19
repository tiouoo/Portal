using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Portal.Bedrock.Standard.Interface;
using Portal.Localization;

namespace Portal.Bedrock;

internal static class BedrockNativeLogMonitor
{
    public static void Start(string logPath, Func<Process?> processProvider, Action<string, BedrockLogLevel>? log)
    {
        if (log == null)
            return;
        _ = Task.Run(() => FollowAsync(logPath, processProvider, log)); 
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
                log(LogLanguageManager.Instance.bedrock_nativeLogNotFound.CurrentValue(), BedrockLogLevel.Warning);
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
                    log(string.Format(LogLanguageManager.Instance.bedrock_nativeLogLine.CurrentValue(), line), GetLevel(line));
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
                    log(string.Format(LogLanguageManager.Instance.bedrock_nativeLogLine.CurrentValue(), finalLine), GetLevel(finalLine));
                    continue;
                }
                waitingForProcess = 0;
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_nativeLogReadFailed.CurrentValue(), logPath, Environment.NewLine, exception));
            log(string.Format(LogLanguageManager.Instance.bedrock_nativeLogReadFailedShort.CurrentValue(), exception), BedrockLogLevel.Warning);
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
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_checkProcessStateFailed.CurrentValue(), Environment.NewLine, exception));
            return false;
        }
    }
}
