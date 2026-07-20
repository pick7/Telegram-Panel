using Microsoft.Extensions.Configuration;
using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Services.Proxy;

/// <summary>
/// 将 Telegram 全局代理配置转换为统一的运行时连接参数。
/// </summary>
public static class GlobalTelegramProxyConfiguration
{
    public static ProxyConnectionOptions? Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var host = (configuration["Telegram:Proxy:Server"] ?? string.Empty)
            .Trim()
            .Trim('[', ']');
        var portText = (configuration["Telegram:Proxy:Port"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(portText))
            return null;
        if (string.IsNullOrWhiteSpace(host)
            || !int.TryParse(portText, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Telegram 全局代理地址或端口配置无效");
        }

        var secret = NormalizeOptional(configuration["Telegram:Proxy:Secret"]);
        return new ProxyConnectionOptions(
            0,
            "Telegram 全局代理",
            OutboundProxyKinds.Manual,
            secret == null ? OutboundProxyProtocols.Socks5 : OutboundProxyProtocols.MtProto,
            host,
            port,
            NormalizeOptional(configuration["Telegram:Proxy:Username"]),
            NormalizeOptional(configuration["Telegram:Proxy:Password"]),
            secret);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
