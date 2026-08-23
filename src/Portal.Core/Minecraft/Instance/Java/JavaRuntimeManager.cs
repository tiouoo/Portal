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

    /// <summary>
    /// 检测列表中不再存在于文件系统的 Java，从列表中移除，并为受影响的版本自动切换默认 Java。
    /// </summary>
    public static async Task<JavaReconcileResult> ReconcileAsync(
        ICollection<JavaRuntimeEntry> javaRuntimes,
        IDictionary<int, string>? javaVersionDefaultPaths,
        CancellationToken cancellationToken = default)
    {
        var snapshot = javaRuntimes.ToArray();
        if (snapshot.Length == 0)
            return JavaReconcileResult.Empty;

        var existence = await Task.Run(() =>
        {
            var result = new bool[snapshot.Length];
            for (var index = 0; index < snapshot.Length; index++)
                result[index] = File.Exists(snapshot[index].JavaPath);
            return result;
        }, cancellationToken);

        var removed = new List<JavaRuntimeEntry>();
        for (var index = 0; index < snapshot.Length; index++)
            if (!existence[index])
                removed.Add(snapshot[index]);

        if (removed.Count == 0)
            return JavaReconcileResult.Empty;

        var remainingByVersion = snapshot
            .Where((_, index) => existence[index])
            .GroupBy(runtime => runtime.MajorVersion)
            .ToDictionary(group => group.Key, group => group.ToList());

        var switches = new List<JavaRuntimeSwitch>();
        var missing = new List<JavaRuntimeEntry>();
        foreach (var runtime in removed)
        {
            if (remainingByVersion.TryGetValue(runtime.MajorVersion, out var candidates) && candidates.Count > 0)
                switches.Add(new JavaRuntimeSwitch(runtime, candidates[0]));
            else
                missing.Add(runtime);
        }

        var removedPaths = new HashSet<string>(removed.Select(runtime => runtime.JavaPath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var runtime in removed)
            javaRuntimes.Remove(runtime);

        if (javaVersionDefaultPaths is not null)
        {
            var staleKeys = javaVersionDefaultPaths
                .Where(pair => removedPaths.Contains(pair.Value))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in staleKeys)
                javaVersionDefaultPaths.Remove(key);

            foreach (var javaRuntimeSwitch in switches)
                javaVersionDefaultPaths[javaRuntimeSwitch.Removed.MajorVersion] = javaRuntimeSwitch.Selected.JavaPath;
        }

        return new JavaReconcileResult(switches, missing);
    }

    public static IReadOnlyList<JavaReconcileMessage> BuildMessages(JavaReconcileResult result)
    {
        if (!result.HasChanges)
            return [];

        var messages = new List<JavaReconcileMessage>();
        foreach (var javaRuntimeSwitch in result.Switches)
        {
            messages.Add(new JavaReconcileMessage(
                string.Format(CommonLanguageManager.Instance.javaPage_reconcileSwitched.CurrentValue(),
                    javaRuntimeSwitch.Removed.DisplayName, javaRuntimeSwitch.Selected.DisplayName),
                false));
        }

        foreach (var runtime in result.Missing)
        {
            messages.Add(new JavaReconcileMessage(
                string.Format(CommonLanguageManager.Instance.javaPage_reconcileMissing.CurrentValue(),
                    runtime.DisplayName),
                true));
        }

        return messages;
    }
}

public sealed record JavaRuntimeSwitch(JavaRuntimeEntry Removed, JavaRuntimeEntry Selected);

public sealed record JavaReconcileResult(
    IReadOnlyList<JavaRuntimeSwitch> Switches,
    IReadOnlyList<JavaRuntimeEntry> Missing)
{
    public static JavaReconcileResult Empty { get; } = new([], []);

    public bool HasChanges => Switches.Count > 0 || Missing.Count > 0;
}

public sealed record JavaReconcileMessage(string Text, bool IsError);
