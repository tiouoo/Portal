using System.Text;
using System.Text.Json;
using Portal.Localization;

namespace Portal.Core.Services;

public sealed record LogShareResult(string Platform, string? Url, string? Error)
{
    public bool IsSuccess => Url is not null && Error is null;
}

public static class LogSharingService
{
    private const string Source = "Portal";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static async Task<LogShareResult[]> ShareAllAsync(string content, CancellationToken ct)
    {
        var logShareTask = ShareToLogShareCnAsync(content, ct);
        var mcloTask = ShareToMcLogsAsync(content, ct);
        await Task.WhenAll(logShareTask, mcloTask);
        return [logShareTask.Result, mcloTask.Result];
    }

    private static async Task<LogShareResult> ShareToLogShareCnAsync(string content, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.logshare.cn/v1/log");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { content = LimitContent(content, 1_048_576, 50_000), source = Source }),
                Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new LogShareResult("LogShare.CN", null,
                    string.Format(CommonLanguageManager.Instance.logSharing_httpError.CurrentValue(), (int)response.StatusCode,
                        ExtractError(body) ?? CommonLanguageManager.Instance.logSharing_serverError.CurrentValue()));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("url", out urlElement))
                url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
                return new LogShareResult("LogShare.CN", null, CommonLanguageManager.Instance.logSharing_noShareLink.CurrentValue());
            return new LogShareResult("LogShare.CN", url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new LogShareResult("LogShare.CN", null, ex.Message);
        }
    }

    private static async Task<LogShareResult> ShareToMcLogsAsync(string content, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mclo.gs/1/log");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { content = LimitContent(content, 10_485_760, 25_000), source = Source }),
                Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new LogShareResult("mclo.gs", null,
                    string.Format(CommonLanguageManager.Instance.logSharing_httpError.CurrentValue(), (int)response.StatusCode,
                        ExtractError(body) ?? CommonLanguageManager.Instance.logSharing_serverError.CurrentValue()));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                return new LogShareResult("mclo.gs", null, CommonLanguageManager.Instance.logSharing_noShareLink.CurrentValue());
            return new LogShareResult("mclo.gs", url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new LogShareResult("mclo.gs", null, ex.Message);
        }
    }

    public static async Task<string> AnalyseAiAsync(string content, Action<string>? onChunk, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.logshare.cn/v1/ai/analyse");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { content = LimitContent(content, 1_048_576, 50_000) }),
            Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.logSharing_httpError.CurrentValue(),
                (int)response.StatusCode, ExtractError(error) ?? CommonLanguageManager.Instance.logSharing_serverError.CurrentValue()));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var builder = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                break;
            if (data.Length == 0)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.String)
                {
                    var chunk = contentElement.GetString() ?? string.Empty;
                    if (chunk.Length > 0)
                    {
                        builder.Append(chunk);
                        onChunk?.Invoke(chunk);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        if (builder.Length == 0)
            throw new InvalidOperationException(CommonLanguageManager.Instance.logSharing_aiNoResult.CurrentValue());
        return builder.ToString();
    }

    private static string LimitContent(string content, int maxBytes, int maxLines)
    {
        if (Encoding.UTF8.GetByteCount(content) <= maxBytes)
            return content;
        var lines = content.Split('\n');
        if (lines.Length <= maxLines)
            return content;
        return string.Join('\n', lines[^maxLines..]);
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }
}