using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
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
            Environment.OSVersion.VersionString,
            RuntimeInformation.FrameworkDescription,
            AppVersionService.Instance.Version.VersionTitle,
            new Dictionary<string, object?>(),
            new Dictionary<string, object?> { ["package"] = GetPackageType() });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8,
            "application/json");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.telemetry_httpError.CurrentValue(),
                (int)response.StatusCode, endpoint.Host, endpoint.AbsolutePath, responseBody));
            response.EnsureSuccessStatusCode();
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.telemetry_sent.CurrentValue(),
            (int)response.StatusCode, endpoint.Host, endpoint.AbsolutePath));
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
