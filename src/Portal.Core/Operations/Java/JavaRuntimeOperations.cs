using Portal.Core.Minecraft.Instance.Java;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Portal.Core.Operations.Java;

public static class JavaRuntimeOperations
{
    public static async Task<JavaRuntimeAddResult?> AddFromPickerAsync(
        TopLevel topLevel,
        ICollection<JavaRuntimeEntry> javaRuntimes,
        CancellationToken cancellationToken = default)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Java 可执行文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Java 可执行文件")
                {
                    Patterns = ["java", "java.exe", "javaw", "javaw.exe"]
                }
            ]
        });
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

        if (javaRuntimes.Contains(java))
            return new JavaRuntimeAddResult(java, true);

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
