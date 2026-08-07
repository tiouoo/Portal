using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Portal.Core.Minecraft.Instance.Java;

public static class JavaRuntimeVerifier
{
    private static readonly ConcurrentDictionary<string, bool> ModuleCache = new(StringComparer.OrdinalIgnoreCase);
    
    public static async Task<bool> IsUsableAsync(string javaPath, int majorVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            return false;
        if (majorVersion < 9)
            return true;

        var key = Path.GetFullPath(javaPath);
        if (ModuleCache.TryGetValue(key, out var cached))
            return cached;

        var usable = await ProbeModulesAsync(javaPath, cancellationToken);
        ModuleCache[key] = usable;
        return usable;
    }

    private static async Task<bool> ProbeModulesAsync(string javaPath, CancellationToken cancellationToken)
    {
        var executable = javaPath;
        if (OperatingSystem.IsWindows())
        {
            var windowless = Path.GetFileName(javaPath).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase);
            var console = Path.Combine(Path.GetDirectoryName(javaPath) ?? string.Empty, "java.exe");
            if (windowless && File.Exists(console))
                executable = console;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            var startInfo = new ProcessStartInfo(executable, "--list-modules")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                return false;

            return output.Contains("jdk.zipfs", StringComparison.OrdinalIgnoreCase)
                && output.Contains("jdk.unsupported", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}