using Portal.Core.Minecraft.Instance.Java.Iridium;
using Portal.Localization;

namespace Portal.Core.Minecraft.Instance.Java;

public static class JavaRuntimeManager
{
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
        IProgress<JavaRuntimeEntry>? onFound = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<JavaRuntimeEntry>();
        progress?.Report(new DeepScanProgress(0, 0, 0, CommonLanguageManager.Instance.javaRuntime_scanStage1.CurrentValue()));

        try
        {
            await Task.Run(async () =>
            {
                await foreach (var java in IridiumJavaRuntimeScanner.EnumerableJavaAsync(true, cancellationToken))
                {
                    result.Add(java);
                    onFound?.Report(java);
                    progress?.Report(new DeepScanProgress(0, 0, result.Count,
                        string.Format(CommonLanguageManager.Instance.javaRuntime_deepScanFound.CurrentValue(), java.JavaVersion)));
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
