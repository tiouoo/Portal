using System.Runtime.CompilerServices;
using Iridium.Models.Java;
using Iridium.Providers;

namespace Portal.Core.Minecraft.Instance.Java.Iridium;

internal static class IridiumJavaRuntimeScanner
{
    private static readonly JavaProvider Provider = new();

    public static async Task<JavaRuntimeEntry?> FromPathAsync(string javaPath,
        CancellationToken cancellationToken = default)
    {
        var java = await Provider.GetJavaEntryAsync(javaPath, cancellationToken);
        return java is null ? null : Convert(java);
    }

    public static async IAsyncEnumerable<JavaRuntimeEntry> EnumerableJavaAsync(
        bool fullDiskSearch,
        IProgress<JavaScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var java in Provider.EnumerableJavaAsync(fullDiskSearch, progress, cancellationToken))
            if (java is not null)
                yield return Convert(java);
    }

    public static IReadOnlyList<JavaRuntimeEntry> Deduplicate(IEnumerable<JavaRuntimeEntry> entries)
    {
        var bestByHome = new Dictionary<string, JavaRuntimeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.JavaPath))
                continue;

            var key = JavaHomeOf(entry.JavaPath) ?? entry.JavaPath;
            if (!bestByHome.TryGetValue(key, out var existing) ||
                PrefersBinary(entry.JavaPath, existing.JavaPath))
                bestByHome[key] = entry;
        }

        return bestByHome.Values.ToList();
    }

    private static string? JavaHomeOf(string javaPath)
    {
        var binDirectory = Path.GetDirectoryName(javaPath);
        return string.IsNullOrEmpty(binDirectory) ? null : Path.GetDirectoryName(binDirectory);
    }

    private static bool PrefersBinary(string candidate, string existing)
    {
        return BinaryRank(candidate) < BinaryRank(existing);
    }

    private static int BinaryRank(string path)
    {
        return Path.GetFileName(path).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static JavaRuntimeEntry Convert(JavaEntry java)
    {
        return new JavaRuntimeEntry
        {
            JavaPath = java.JavaPath,
            JavaType = MapVendorToType(java.Vendor),
            JavaVersion = java.Version,
            MajorVersion = java.MajorVersion,
            Is64Bit = java.Is64Bit
        };
    }

    private static string MapVendorToType(string vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor)) return "OpenJDK";
        if (vendor.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) return "Java";
        if (vendor.Contains("Azul", StringComparison.OrdinalIgnoreCase) ||
            vendor.Contains("Zulu", StringComparison.OrdinalIgnoreCase)) return "ZuluJDK";
        return "OpenJDK";
    }
}
