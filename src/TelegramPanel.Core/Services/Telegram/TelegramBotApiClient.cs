using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TelegramPanel.Core.Services.Telegram;

public class TelegramBotApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<TelegramBotApiClient> _logger;

    public TelegramBotApiClient(HttpClient http, ILogger<TelegramBotApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<JsonElement> CallAsync(string token, string method, IReadOnlyDictionary<string, string?> query, CancellationToken cancellationToken)
    {
        token = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Bot Token 为空");

        method = (method ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(method))
            throw new InvalidOperationException("method 为空");

        var url = BuildUrl(token, method, query);
        using var resp = await _http.GetAsync(url, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            var code = root.TryGetProperty("error_code", out var codeEl) ? codeEl.GetInt32() : 0;
            var desc = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : "未知错误";
            throw new InvalidOperationException($"Bot API 调用失败：{method} ({code}) {desc}");
        }

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"Bot API 返回缺少 result：{method}");

        return result.Clone();
    }

    /// <summary>
    /// 调用 Bot API 并上传文件（使用 multipart/form-data）
    /// </summary>
    public async Task<JsonElement> CallWithFileAsync(
        string token,
        string method,
        IReadOnlyDictionary<string, string?> parameters,
        string fileParameterName,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        token = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Bot Token 为空");

        method = (method ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(method))
            throw new InvalidOperationException("method 为空");

        if (string.IsNullOrWhiteSpace(fileParameterName))
            throw new InvalidOperationException("fileParameterName 为空");

        if (fileStream == null)
            throw new InvalidOperationException("fileStream 为空");

        fileName = (fileName ?? "file").Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "file";

        if (fileStream.CanSeek)
            fileStream.Position = 0;

        var url = $"https://api.telegram.org/bot{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(method)}";

        using var content = new MultipartFormDataContent();

        // 添加普通参数
        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(key) && value != null)
            {
                content.Add(new StringContent(value), key);
            }
        }

        // 添加文件
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, fileParameterName, fileName);

        using var resp = await _http.PostAsync(url, content, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            var code = root.TryGetProperty("error_code", out var codeEl) ? codeEl.GetInt32() : 0;
            var desc = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : "未知错误";
            throw new InvalidOperationException($"Bot API 调用失败：{method} ({code}) {desc}");
        }

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"Bot API 返回缺少 result：{method}");

        return result.Clone();
    }

    /// <summary>
    /// 调用 Bot API 并上传多个文件（使用 multipart/form-data）。
    /// 典型用途：sendMediaGroup（attach://file1...）。
    /// </summary>
    public async Task<JsonElement> CallWithFilesAsync(
        string token,
        string method,
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlyDictionary<string, (Stream Stream, string FileName, string? ContentType)> files,
        CancellationToken cancellationToken)
    {
        token = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Bot Token 为空");

        method = (method ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(method))
            throw new InvalidOperationException("method 为空");

        if (files == null || files.Count == 0)
            throw new InvalidOperationException("files 为空");

        var url = $"https://api.telegram.org/bot{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(method)}";

        using var content = new MultipartFormDataContent();

        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(key) && value != null)
                content.Add(new StringContent(value), key);
        }

        foreach (var (name, file) in files)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var stream = file.Stream ?? throw new InvalidOperationException($"files[{name}] stream 为空");
            if (stream.CanSeek)
                stream.Position = 0;

            var fileName = (file.FileName ?? "file").Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "file";

            var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(streamContent, name, fileName);
        }

        using var resp = await _http.PostAsync(url, content, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            var code = root.TryGetProperty("error_code", out var codeEl) ? codeEl.GetInt32() : 0;
            var desc = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : "未知错误";
            throw new InvalidOperationException($"Bot API 调用失败：{method} ({code}) {desc}");
        }

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"Bot API 返回缺少 result：{method}");

        return result.Clone();
    }

    /// <summary>
    /// 设置 Webhook URL。
    /// </summary>
    /// <param name="token">Bot Token</param>
    /// <param name="url">Webhook URL（必须是 HTTPS）</param>
    /// <param name="secretToken">可选的 secret token，用于验证请求来源</param>
    /// <param name="allowedUpdates">允许的更新类型 JSON 数组，如 ["message","my_chat_member"]</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SetWebhookAsync(
        string token,
        string url,
        string? secretToken = null,
        string? allowedUpdates = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["url"] = url,
            ["drop_pending_updates"] = "false",
            ["max_connections"] = "40"
        };

        if (!string.IsNullOrWhiteSpace(secretToken))
            parameters["secret_token"] = secretToken;

        if (!string.IsNullOrWhiteSpace(allowedUpdates))
            parameters["allowed_updates"] = allowedUpdates;

        await CallAsync(token, "setWebhook", parameters, cancellationToken);
        _logger.LogInformation("Webhook set for bot: url={Url}", url);
    }

    /// <summary>
    /// 删除 Webhook 并切换回 getUpdates 模式。
    /// </summary>
    public async Task DeleteWebhookAsync(string token, bool dropPendingUpdates = false, CancellationToken cancellationToken = default)
    {
        await CallAsync(token, "deleteWebhook", new Dictionary<string, string?>
        {
            ["drop_pending_updates"] = dropPendingUpdates ? "true" : "false"
        }, cancellationToken);
        _logger.LogInformation("Webhook deleted for bot");
    }

    /// <summary>
    /// 获取当前 Webhook 信息。
    /// </summary>
    public async Task<WebhookInfo> GetWebhookInfoAsync(string token, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(token, "getWebhookInfo", new Dictionary<string, string?>(), cancellationToken);

        var url = result.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        var hasCustomCert = result.TryGetProperty("has_custom_certificate", out var certEl) && certEl.GetBoolean();
        var pendingCount = result.TryGetProperty("pending_update_count", out var pendingEl) ? pendingEl.GetInt32() : 0;
        var lastErrorDate = result.TryGetProperty("last_error_date", out var errDateEl) ? errDateEl.GetInt64() : 0;
        var lastErrorMessage = result.TryGetProperty("last_error_message", out var errMsgEl) ? errMsgEl.GetString() : null;

        return new WebhookInfo(
            Url: url,
            HasCustomCertificate: hasCustomCert,
            PendingUpdateCount: pendingCount,
            LastErrorDate: lastErrorDate > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastErrorDate).UtcDateTime : null,
            LastErrorMessage: lastErrorMessage
        );
    }

    public sealed record WebhookInfo(
        string? Url,
        bool HasCustomCertificate,
        int PendingUpdateCount,
        DateTime? LastErrorDate,
        string? LastErrorMessage
    )
    {
        public bool IsActive => !string.IsNullOrWhiteSpace(Url);
    }

    private static string BuildUrl(string token, string method, IReadOnlyDictionary<string, string?> query)
    {
        var sb = new StringBuilder();
        sb.Append("https://api.telegram.org/bot");
        sb.Append(Uri.EscapeDataString(token));
        sb.Append('/');
        sb.Append(Uri.EscapeDataString(method));

        if (query.Count == 0)
            return sb.ToString();

        var first = true;
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null)
                continue;

            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }
}
