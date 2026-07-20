using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Models;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

public sealed partial class ProxyManagementService
{
    private const int MaxResinControlPlaneResponseBytes = 1024 * 1024;

    private async Task<OutboundProxy> TestAsyncCore(
        OutboundProxy proxy,
        CancellationToken cancellationToken)
    {
        var probeKey = $"telegram_panel_probe_{proxy.Id}";
        try
        {
            EgressProbeResult result;
            if (proxy.Kind == OutboundProxyKinds.Resin)
            {
                var controlError = await ValidateResinControlPlaneAsync(proxy, cancellationToken);
                result = controlError == null
                    ? await _probeService.ProbeProxyAsync(
                        proxy,
                        probeKey,
                        cancellationToken)
                    : new EgressProbeResult(
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        DateTime.UtcNow,
                        controlError);
            }
            else
            {
                result = await _probeService.ProbeProxyAsync(
                    proxy,
                    probeKey,
                    cancellationToken);
            }

            proxy.TestStatus = result.Success ? "ok" : "fail";
            proxy.LastError = result.Error;
            proxy.LastLatencyMs = result.Success ? result.LatencyMs : null;
            proxy.LastTestedAtUtc = result.CheckedAtUtc;
            proxy.UpdatedAtUtc = DateTime.UtcNow;
            if (result.Success)
            {
                proxy.EgressIp = result.Ip;
                proxy.EgressCountry = result.Country;
                proxy.EgressCity = result.City;
                proxy.EgressIsp = result.Isp;
            }
            else
            {
                proxy.EgressIp = null;
                proxy.EgressCountry = null;
                proxy.EgressCity = null;
                proxy.EgressIsp = null;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return proxy;
        }
        finally
        {
            if (proxy.Kind == OutboundProxyKinds.Resin)
            {
                await ReleaseResinLeaseBestEffortAsync(
                    proxy,
                    probeKey,
                    $"检测身份 {probeKey}",
                    CancellationToken.None);
            }
        }
    }

    private async Task<string?> ValidateResinControlPlaneAsync(
        OutboundProxy proxy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proxy.ResinAdminUrl))
            return null;

        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(proxy.ResinAdminUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var healthRequest = new HttpRequestMessage(HttpMethod.Get, "/healthz");
            using var health = await client.SendAsync(
                healthRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!health.IsSuccessStatusCode)
                return $"Resin 健康检查返回 HTTP {(int)health.StatusCode}";

            if (!string.IsNullOrWhiteSpace(proxy.ResinAdminToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    proxy.ResinAdminToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return $"Resin 管理鉴权返回 HTTP {(int)response.StatusCode}";

                var platformId = await ResolveResinPlatformIdAsync(client, proxy, cancellationToken);
                if (platformId == null)
                    return $"Resin Platform {proxy.ResinPlatform ?? "Default"} 不存在";
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Resin 控制面不可用：{SafeError(ex)}";
        }
    }

    private async Task ReleaseResinLeaseAsync(
        OutboundProxy proxy,
        int accountId,
        CancellationToken cancellationToken) =>
        await ReleaseResinLeaseAsync(
            proxy,
            $"tg_account_{accountId}",
            $"账号 {accountId}",
            cancellationToken);

    public async Task ReleaseImportResinLeaseBestEffortAsync(
        int proxyId,
        string stableImportKey,
        CancellationToken cancellationToken = default)
    {
        var proxy = await _db.OutboundProxies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == proxyId, cancellationToken);
        if (proxy?.Kind != OutboundProxyKinds.Resin || string.IsNullOrWhiteSpace(stableImportKey))
            return;

        await ReleaseResinLeaseBestEffortAsync(
            proxy,
            stableImportKey.Trim(),
            $"导入身份 {stableImportKey.Trim()}",
            cancellationToken);
    }

    private async Task ReleaseResinLeaseAsync(
        OutboundProxy proxy,
        string accountKey,
        string subject,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proxy.ResinAdminUrl)
            || string.IsNullOrWhiteSpace(proxy.ResinAdminToken))
        {
            return;
        }

        var platform = string.IsNullOrWhiteSpace(proxy.ResinPlatform)
            ? "Default"
            : proxy.ResinPlatform;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(proxy.ResinAdminUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            proxy.ResinAdminToken);
        var platformId = await ResolveResinPlatformIdAsync(client, proxy, cancellationToken);
        if (platformId == null)
        {
            _logger.LogInformation(
                "Resin Platform {Platform} no longer exists; lease {AccountKey} is already absent",
                platform,
                accountKey);
            return;
        }

        accountKey = Uri.EscapeDataString(accountKey);
        var platformKey = Uri.EscapeDataString(platformId);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/platforms/{platformKey}/leases/{accountKey}");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Resin Lease 释放返回 HTTP {(int)response.StatusCode}，{subject} 的状态保持不变");
        }
    }

    private async Task ReleaseResinLeaseBestEffortAsync(
        OutboundProxy proxy,
        int accountId,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReleaseResinLeaseAsync(proxy, accountId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release Resin lease for account {AccountId}", accountId);
        }
    }

    private async Task ReleaseResinLeaseBestEffortAsync(
        OutboundProxy proxy,
        string accountKey,
        string subject,
        CancellationToken cancellationToken)
    {
        try
        {
            await ReleaseResinLeaseAsync(proxy, accountKey, subject, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to release Resin lease {AccountKey} for proxy {ProxyId}",
                accountKey,
                proxy.Id);
        }
    }

    private static async Task<string?> ResolveResinPlatformIdAsync(
        HttpClient client,
        OutboundProxy proxy,
        CancellationToken cancellationToken)
    {
        var platformName = string.IsNullOrWhiteSpace(proxy.ResinPlatform)
            ? "Default"
            : proxy.ResinPlatform.Trim();
        var path = $"/api/v1/platforms?keyword={Uri.EscapeDataString(platformName)}&limit=100&offset=0";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Resin Platform 查询返回 HTTP {(int)response.StatusCode}");
        }

        if (response.Content.Headers.ContentLength is > MaxResinControlPlaneResponseBytes)
        {
            throw new InvalidDataException(
                $"Resin Platform 查询响应超过 {MaxResinControlPlaneResponseBytes / 1024}KB 限制");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limited = new MemoryStream(capacity: Math.Min(
            (int)(response.Content.Headers.ContentLength ?? 0),
            MaxResinControlPlaneResponseBytes));
        var buffer = new byte[81920];
        var totalBytes = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            totalBytes += read;
            if (totalBytes > MaxResinControlPlaneResponseBytes)
            {
                throw new InvalidDataException(
                    $"Resin Platform 查询响应超过 {MaxResinControlPlaneResponseBytes / 1024}KB 限制");
            }

            await limited.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        limited.Position = 0;
        using var document = await JsonDocument.ParseAsync(limited, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Resin Platform 查询响应缺少 items 数组");
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name)
                || !string.Equals(name.GetString(), platformName, StringComparison.OrdinalIgnoreCase)
                || !item.TryGetProperty("id", out var id)
                || string.IsNullOrWhiteSpace(id.GetString()))
            {
                continue;
            }
            return id.GetString();
        }
        return null;
    }

    private async Task DeleteUnusedProxyCoreAsync(
        OutboundProxy proxy,
        CancellationToken cancellationToken)
    {
        var stillUsed = await _db.Accounts.AnyAsync(
            x => x.ProxyId == proxy.Id,
            cancellationToken);
        if (stillUsed)
            throw new ProxyInUseException("代理仍被账号使用");

        if (proxy.WarpProfile != null)
        {
            proxy.IsEnabled = false;
            proxy.TestStatus = "fail";
            proxy.LastError = "WARP 资源正在清理或上次清理未完成";
            proxy.UpdatedAtUtc = DateTime.UtcNow;
            proxy.WarpProfile.Status = "deleting";
            proxy.WarpProfile.DesiredEnabled = false;
            proxy.WarpProfile.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _warpManager.DeleteResourcesAsync(proxy.WarpProfile, purgeData: true, cancellationToken);
        }

        _db.OutboundProxies.Remove(proxy);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string SafeError(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current != null && messages.Count < 3; current = current.InnerException)
        {
            var message = current.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (message.Length > 500)
                message = message[..500];
            if (message.Length > 0)
                messages.Add(message);
        }
        return messages.Count == 0 ? "未知错误" : string.Join(" | ", messages.Distinct());
    }
}
