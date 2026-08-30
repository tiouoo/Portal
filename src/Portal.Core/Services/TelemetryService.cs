using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Core.Classes.Config;
using Portal.Core.Const;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Services;

/// <summary>Best-effort product telemetry configured through build metadata or environment variables.</summary>
public static class TelemetryService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void EnsureIdentity(ConfigEntry config)
    {
        if (IsSha256(config.TelemetryUserId))
            return;

        var source = Guid.NewGuid().ToString("D");
        config.TelemetryUserId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"Portal:{source}"))).ToLowerInvariant();
    }

    public static async Task SendAppOpenAsync(CancellationToken cancellationToken)
    {
        var url = CredentialsService.TelemetryUrl;
        var apiKey = CredentialsService.TelemetryApiKey;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.Info(LogLanguageManager.Instance.telemetry_notConfigured.CurrentValue());
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            Logger.Warning(LogLanguageManager.Instance.telemetry_invalidEndpoint.CurrentValue());
            return;
        }

        var payload = new AppOpenEvent(
            "app-open",
            Data.ConfigEntry.TelemetryUserId!,
            DateTimeOffset.UtcNow,
            GetOperatingSystem(),
            GetOperatingSystemVersion(),
            RuntimeInformation.FrameworkDescription,
            AppVersionService.Instance.Version.VersionTitle,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?> { ["package"] = GetPackageType() });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8,
            "application/json");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.telemetry_httpError.CurrentValue(),
                (int)response.StatusCode));
            return;
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.telemetry_sent.CurrentValue(), (int)response.StatusCode));
    }

    private static bool IsSha256(string? value)
    {
        if (value is not { Length: 64 })
            return false;
        try
        {
            Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GetOperatingSystem() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown";

    private static string GetOperatingSystemVersion()
    {
        var kernelVersion = GetKernelVersion();
        var description = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? GetLinuxDistributionName() ?? RuntimeInformation.OSDescription
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? GetMacOsProductVersion() is { } macOsVersion ? $"macOS {macOsVersion}" : RuntimeInformation.OSDescription
                : RuntimeInformation.OSDescription;
        return string.IsNullOrWhiteSpace(kernelVersion) ? description : $"{description}; kernel {kernelVersion}";
    }

    private static string GetKernelVersion()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var kernelVersion = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
                if (!string.IsNullOrWhiteSpace(kernelVersion))
                    return kernelVersion;
            }
            catch
            {
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var kernelVersion = TryReadCommandOutput("/usr/bin/uname", "-r");
            if (!string.IsNullOrWhiteSpace(kernelVersion))
                return kernelVersion;
        }

        return Environment.OSVersion.Version.ToString();
    }

    private static string? GetMacOsProductVersion() =>
        TryReadCommandOutput("/usr/bin/sw_vers", "-productVersion");

    private static string? TryReadCommandOutput(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            if (!process.Start())
                return null;
            if (!process.WaitForExit(1000))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetLinuxDistributionName()
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                if (!line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    continue;

                var value = line["PRETTY_NAME=".Length..].Trim();
                return value.Trim('"').Replace("\\\"", "\"");
            }
        }
        catch
        {
        }

        return null;
    }

    private static string GetPackageType()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Data.Instance.PackageType))
                return Data.Instance.PackageType;
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            using var stream = typeof(TelemetryService).Assembly
                .GetManifestResourceStream("Portal.Core.Assets.package-type.txt");
            using var reader = stream is null ? null : new StreamReader(stream);
            var packageType = reader?.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(packageType) ? "unknown" : packageType;
        }
        catch
        {
            return "unknown";
        }
    }

    private sealed record AppOpenEvent(
        [property: JsonPropertyName("eventName")] string EventName,
        [property: JsonPropertyName("userId")] string UserId,
        [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("os")] string Os,
        [property: JsonPropertyName("osVersion")] string OsVersion,
        [property: JsonPropertyName("runtimeVersion")] string RuntimeVersion,
        [property: JsonPropertyName("appVersion")] string AppVersion,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, object?> Metadata,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, object?> Properties);
}
