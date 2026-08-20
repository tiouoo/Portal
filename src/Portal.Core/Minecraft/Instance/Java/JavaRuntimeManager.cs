using Iridium.Models.Java;
using Portal.Core.Minecraft.Instance.Java.Iridium;
using Portal.Localization;

namespace Portal.Core.Minecraft.Instance.Java;

public static class JavaRuntimeManager
{
    private const int DeepScanProgressInterval = 200;

    public static async Task<JavaRuntimeEntry?> FromPathAsync(string javaPath,
        CancellationToken cancellationToken = default)
    {
        return await IridiumJavaRuntimeScanner.FromPathAsync(javaPath, cancellationToken);
    }

    public static async Task<IReadOnlyList<JavaRuntimeEntry>> ScanAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var result = new List<JavaRuntimeEntry>();
            await foreach (var java in IridiumJavaRuntimeScanner.EnumerableJavaAsync(false, cancellationToken: cancellationToken))
                result.Add(java);

            return IridiumJavaRuntimeScanner.Deduplicate(result);
        }, cancellationToken);
    }

    public static async Task<IReadOnlyList<JavaRuntimeEntry>> DeepScanAsync(
        IProgress<DeepScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<JavaRuntimeEntry>();
        progress?.Report(new DeepScanProgress(0, 0, 0, CommonLanguageManager.Instance.javaRuntime_scanStage1.CurrentValue()));

        try
        {
            await Task.Run(async () =>
            {
                var scanProgress = progress is null ? null : new Progress<JavaScanProgress>(p =>
                {
                    if (p.DirectoriesScanned % DeepScanProgressInterval != 0)
                        return;

                    progress.Report(new DeepScanProgress(
                        p.DirectoriesScanned,
                        p.DirectoriesQueued,
                        result.Count,
                        string.Format(CommonLanguageManager.Instance.javaRuntime_scanning.CurrentValue(), p.CurrentDirectory)));
                });

                await foreach (var java in IridiumJavaRuntimeScanner.EnumerableJavaAsync(true, scanProgress, cancellationToken))
                {
                    result.Add(java);
                    progress?.Report(new DeepScanProgress(0, 0, result.Count,
                        string.Format(CommonLanguageManager.Instance.javaRuntime_autoScanFound.CurrentValue(), java.JavaVersion)));
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        var deduplicated = IridiumJavaRuntimeScanner.Deduplicate(result);
        progress?.Report(new DeepScanProgress(0, 0, deduplicated.Count,
            string.Format(CommonLanguageManager.Instance.javaRuntime_scanComplete.CurrentValue(), deduplicated.Count)));

        return deduplicated;
    }
}
