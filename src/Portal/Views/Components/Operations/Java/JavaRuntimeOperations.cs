using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Instance.Java;
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
            Title = "选择 Java 可执行文件",
            AllowMultiple = false
        };

        if (OperatingSystem.IsWindows())
            options.FileTypeFilter =
            [
                new FilePickerFileType("Java 可执行文件")
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
                Name = "强力扫描 Java",
                Description = "准备扫描…",
                Progress = 0.0,
                Actions = []
            },
            async ctx =>
            {
                var addedCount = 0;
                var duplicateCount = 0;
                try
                {
                    ctx.SetRunning("准备扫描磁盘中的 Java 运行时…");

                    var progress = new Progress<DeepScanProgress>(p =>
                    {
                        if (ctx.CancellationToken.IsCancellationRequested) return;
                        try
                        {
                            var progressRatio = p.DirectoriesQueued > 0
                                ? Math.Clamp((double)p.DirectoriesScanned / p.DirectoriesQueued, 0.0, 1.0)
                                : null as double?;

                            ctx.ReportProgress(progressRatio);
                            ctx.SetDescription($"{p.CurrentStatus}（已找到 {p.JavasFound} 个）");
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    });

                    IReadOnlyList<JavaRuntimeEntry> found;
                    try
                    {
                        found = await JavaRuntimeManager.DeepScanAsync(progress, ctx.CancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        found = Array.Empty<JavaRuntimeEntry>();
                    }

                    foreach (var java in found)
                        if (javaRuntimes.Contains(java))
                        {
                            duplicateCount++;
                        }
                        else
                        {
                            javaRuntimes.Add(java);
                            addedCount++;
                        }

                    var cancelled = ctx.CancellationToken.IsCancellationRequested;
                    var finishText = cancelled
                        ? $"已取消，已找到的新增 {addedCount} 个 Java，重复 {duplicateCount} 个"
                        : $"扫描完成：新增 {addedCount} 个 Java，重复 {duplicateCount} 个";


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
                    ctx.SetDescription($"扫描失败：{ex.Message}");
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