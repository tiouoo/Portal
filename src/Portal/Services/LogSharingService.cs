using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Portal.Services;

/// <summary>
/// 单个日志分享平台的分享结果。
/// </summary>
public sealed record LogShareResult(string Platform, string? Url, string? Error)
{
    public bool IsSuccess => Url is not null && Error is null;
}

/// <summary>
/// 日志分享服务：同时将日志分享到 LogShare.CN 与 mclo.gs 两个平台，其中一个失败时展示另一个；
/// 两个都失败时抛出错误信息。同时提供 LogShare.CN 的免费 AI 分析（SSE 流式）。
/// </summary>
public static class LogSharingService
{
    private const string Source = "Portal";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    /// <summary>并行分享到两个平台。</summary>
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
                return new("LogShare.CN", null, $"HTTP {(int)response.StatusCode}：{ExtractError(body) ?? "服务器返回错误"}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("url", out urlElement))
                url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
                return new("LogShare.CN", null, "服务器未返回分享链接");
            return new("LogShare.CN", url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new("LogShare.CN", null, ex.Message);
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
                return new("mclo.gs", null, $"HTTP {(int)response.StatusCode}：{ExtractError(body) ?? "服务器返回错误"}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                return new("mclo.gs", null, "服务器未返回分享链接");
            return new("mclo.gs", url, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new("mclo.gs", null, ex.Message);
        }
    }

    /// <summary>
    /// 使用 LogShare.CN 的免费大模型分析日志（SSE 流式）。<paramref name="onChunk"/> 在收到每个内容块时被调用。
    /// </summary>
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
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}：{ExtractError(error) ?? "服务器返回错误"}");
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
                // 忽略无法解析的 SSE 数据块。
            }
        }

        if (builder.Length == 0)
            throw new InvalidOperationException("AI 分析未返回结果");
        return builder.ToString();
    }

    /// <summary>
    /// 限制日志内容大小：超过字节上限时截断为最后一个指定行数，避免超出平台限制。
    /// </summary>
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
