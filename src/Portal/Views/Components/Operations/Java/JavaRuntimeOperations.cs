using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Views.Components.Operations.Java;

public static class JavaRuntimeOperations
{
    public static async Task<JavaRuntimeAddResult?> AddFromPickerAsync(
        TopLevel topLevel,
        ICollection<JavaRuntimeEntry> javaRuntimes,
        CancellationToken cancellationToken = default)
    {
        var options = new FilePickerOpenOptions
        {
            Title = CommonLanguageManager.Instance.javaRuntime_selectExecutable.CurrentValue(),
            AllowMultiple = false
        };

        if (OperatingSystem.IsWindows())
            options.FileTypeFilter =
            [
                new FilePickerFileType(CommonLanguageManager.Instance.javaRuntime_executableFileType.CurrentValue())
                {
                    Patterns = ["java", "java.exe", "javaw", "javaw.exe"]
                }
            ];

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0)
            return null;

        var path = files[0].TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path)
            ? new JavaRuntimeAddResult(null, false)
            : await AddAsync(path, javaRuntimes, cancellationToken);
    }

    public static async Task<JavaRuntimeAddResult> AddAsync(
        string javaPath,
        ICollection<JavaRuntimeEntry> javaRuntimes,
        CancellationToken cancellationToken = default)
    {
        var java = await JavaRuntimeManager.FromPathAsync(javaPath, cancellationToken);
        if (java == null)
            return new JavaRuntimeAddResult(null, false);

        var existingJava = javaRuntimes.FirstOrDefault(runtime => runtime.Equals(java));
        if (existingJava is not null)
            return new JavaRuntimeAddResult(existingJava, true);

        javaRuntimes.Add(java);
        return new JavaRuntimeAddResult(java, false);
    }

    public static async Task<JavaRuntimeScanResult> ScanAndAddAsync(
        ICollection<JavaRuntimeEntry> javaRuntimes,
        CancellationToken cancellationToken = default)
    {
        var addedCount = 0;
        var duplicateCount = 0;
        foreach (var java in await JavaRuntimeManager.ScanAsync(cancellationToken))
        {
            if (javaRuntimes.Contains(java))
            {
                duplicateCount++;
                continue;
            }

            javaRuntimes.Add(java);
            addedCount++;
        }

        return new JavaRuntimeScanResult(addedCount, duplicateCount);
    }

    public static (ManagedTask Task, Task<(int Added, int Duplicate)> Result) CreateDeepScanTask(
        ICollection<JavaRuntimeEntry> javaRuntimes)
    {
        var resultSource = new TaskCompletionSource<(int Added, int Duplicate)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TaskManager.Instance.CreateTask(
            new TaskOptions
            {
                Name = CommonLanguageManager.Instance.javaRuntime_forceScanName.CurrentValue(),
                Description = CommonLanguageManager.Instance.javaRuntime_preparingScan.CurrentValue(),
                Progress = 0.0,
                Actions = []
            },
            async ctx =>
            {
                var addedCount = 0;
                var duplicateCount = 0;
                try
                {
                    ctx.SetRunning(CommonLanguageManager.Instance.javaRuntime_preparingDiskScan.CurrentValue());

                    var progress = new Progress<DeepScanProgress>(p =>
                    {
                        if (ctx.CancellationToken.IsCancellationRequested) return;
                        try
                        {
                            var progressRatio = p.DirectoriesQueued > 0
                                ? Math.Clamp((double)p.DirectoriesScanned / p.DirectoriesQueued, 0.0, 1.0)
                                : null as double?;

                            ctx.ReportProgress(progressRatio);
                            ctx.SetDescription(string.Format(
                                CommonLanguageManager.Instance.javaRuntime_scanProgress.CurrentValue(),
                                p.CurrentStatus, p.JavasFound));
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    });

                    var onFound = new Progress<JavaRuntimeEntry>(java =>
                    {
                        if (ctx.CancellationToken.IsCancellationRequested) return;

                        if (javaRuntimes.Contains(java))
                        {
                            duplicateCount++;
                        }
                        else
                        {
                            javaRuntimes.Add(java);
                            addedCount++;
                        }
                    });

                    try
                    {
                        await JavaRuntimeManager.DeepScanAsync(progress, onFound, ctx.CancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    var cancelled = ctx.CancellationToken.IsCancellationRequested;
                    var finishText = cancelled
                        ? string.Format(CommonLanguageManager.Instance.javaRuntime_scanCancelled.CurrentValue(),
                            addedCount, duplicateCount)
                        : string.Format(CommonLanguageManager.Instance.javaPage_scanComplete.CurrentValue(),
                            addedCount, duplicateCount);


                    if (!cancelled)
                    {
                        ctx.SetDescription(finishText);
                        ctx.ReportProgress(1.0);
                    }

                    resultSource.TrySetResult((addedCount, duplicateCount));
                }
                catch (Exception ex)
                {
                    resultSource.TrySetException(ex);
                    ctx.SetDescription(string.Format(
                        CommonLanguageManager.Instance.javaRuntime_scanFailed.CurrentValue(), ex.Message));
                    throw;
                }
            });

        return (task, resultSource.Task);
    }

    public static JavaRuntimeEntry? Remove(
        ICollection<JavaRuntimeEntry> javaRuntimes,
        JavaRuntimeEntry javaRuntime,
        JavaRuntimeEntry? defaultJavaRuntime)
    {
        if (!javaRuntimes.Remove(javaRuntime) || defaultJavaRuntime != javaRuntime)
            return defaultJavaRuntime;

        return javaRuntimes.FirstOrDefault();
    }

    public static void Restore(ICollection<JavaRuntimeEntry> javaRuntimes, JavaRuntimeEntry javaRuntime)
    {
        if (!javaRuntimes.Contains(javaRuntime))
            javaRuntimes.Add(javaRuntime);
    }
}

public record JavaRuntimeAddResult(JavaRuntimeEntry? JavaRuntime, bool IsDuplicate)
{
    public bool IsValid => JavaRuntime != null;
    public bool IsAdded => IsValid && !IsDuplicate;
}

public record JavaRuntimeScanResult(int AddedCount, int DuplicateCount);