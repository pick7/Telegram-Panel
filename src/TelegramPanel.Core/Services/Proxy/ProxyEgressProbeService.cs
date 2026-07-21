using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using TelegramPanel.Core.Models;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

public interface IProxyEgressProbeService
{
    Task<EgressProbeResult> ProbePanelAsync(CancellationToken cancellationToken = default);

    Task<EgressProbeResult> ProbeProxyAsync(
        OutboundProxy proxy,
        string stableAccountKey,
        CancellationToken cancellationToken = default);

    Task<EgressProbeResult> ProbeProxyAsync(
        ProxyConnectionOptions options,
        bool requireWarp = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 通过 Cloudflare Trace 检测代理和面板的公网出口。
/// </summary>
public sealed class ProxyEgressProbeService : IProxyEgressProbeService
{
    private static readonly Uri ProbeUri = new("https://cloudflare.com/cdn-cgi/trace");
    private const int MaxResponseBytes = 256 * 1024;

    public Task<EgressProbeResult> ProbePanelAsync(CancellationToken cancellationToken = default)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };
        return ProbeAsync(handler, requireWarp: false, cancellationToken);
    }

    public Task<EgressProbeResult> ProbeProxyAsync(
        OutboundProxy proxy,
        string stableAccountKey,
        CancellationToken cancellationToken = default)
    {
        var options = AccountProxyResolver.BuildConnectionOptions(proxy, stableAccountKey);
        return ProbeProxyAsync(
            options,
            requireWarp: proxy.Kind == OutboundProxyKinds.Warp,
            cancellationToken);
    }

    public Task<EgressProbeResult> ProbeProxyAsync(
        ProxyConnectionOptions options,
        bool requireWarp = false,
        CancellationToken cancellationToken = default)
    {
        if (options.Protocol == OutboundProxyProtocols.MtProto)
        {
            return Task.FromResult(new EgressProbeResult(
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                DateTime.UtcNow,
                "MTProxy 仅用于 Telegram MTProto，不能通过 HTTP 请求检测公网 IP"));
        }

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectCallback = async (context, token) =>
            {
                var tcpClient = await ProxyTcpConnector.ConnectAsync(
                    context.DnsEndPoint.Host,
                    context.DnsEndPoint.Port,
                    options,
                    token);
                return tcpClient.GetStream();
            }
        };
        return ProbeAsync(handler, requireWarp, cancellationToken);
    }

    private static async Task<EgressProbeResult> ProbeAsync(
        SocketsHttpHandler handler,
        bool requireWarp,
        CancellationToken cancellationToken)
    {
        var checkedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using (handler)
            using (var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Get, ProbeUri))
            {
                request.Headers.UserAgent.ParseAdd("TelegramPanel-EgressProbe/1.0");
                request.Headers.Accept.ParseAdd("text/plain");

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"出口检测返回 HTTP {(int)response.StatusCode}");

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var body = await ReadLimitedAsync(responseStream, cancellationToken);
                var values = ParseTrace(body);

                if (!values.TryGetValue("ip", out var ipText)
                    || !IPAddress.TryParse(ipText, out var ip)
                    || !IsPublicAddress(ip))
                {
                    throw new InvalidDataException("出口检测未返回有效的公网 IP");
                }

                values.TryGetValue("loc", out var country);
                values.TryGetValue("warp", out var warpStatus);
                if (requireWarp && warpStatus is not ("on" or "plus"))
                    throw new InvalidDataException($"Cloudflare Trace 未报告 WARP 已启用：warp={warpStatus ?? "unknown"}");

                stopwatch.Stop();
                return new EgressProbeResult(
                    true,
                    ip.ToString(),
                    NormalizeOptional(country),
                    null,
                    null,
                    NormalizeOptional(warpStatus),
                    (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                    checkedAtUtc,
                    null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new EgressProbeResult(
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                checkedAtUtc,
                SanitizeError(ex));
        }
    }

    private static async Task<string> ReadLimitedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var output = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("出口检测响应超过 256 KiB");
            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static Dictionary<string, string> ParseTrace(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) != 0xfc && !address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();
        if (octets[0] is 0 or 10 or 127 || octets[0] >= 224)
            return false;
        if (octets[0] == 169 && octets[1] == 254)
            return false;
        if (octets[0] == 172 && octets[1] is >= 16 and <= 31)
            return false;
        if (octets[0] == 192 && octets[1] == 168)
            return false;
        if (octets[0] == 100 && octets[1] is >= 64 and <= 127)
            return false;
        if (octets[0] == 198 && octets[1] is 18 or 19)
            return false;
        if (octets[0] == 192 && octets[1] == 0 && octets[2] == 2)
            return false;
        if (octets[0] == 198 && octets[1] == 51 && octets[2] == 100)
            return false;
        if (octets[0] == 203 && octets[1] == 0 && octets[2] == 113)
            return false;
        return true;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SanitizeError(Exception exception)
    {
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 500 ? message : message[..500];
    }
}
