using Microsoft.EntityFrameworkCore;
using TelegramPanel.Core.Models;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

public sealed partial class ProxyManagementService
{
    private async Task EnsureNoDuplicateAsync(
        OutboundProxyInput input,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        var duplicate = await _db.OutboundProxies
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != exceptId
                     && x.Kind == input.Kind
                     && x.Protocol == input.Protocol
                     && x.Host == input.Host
                     && x.Port == input.Port
                     && (x.Username ?? string.Empty) == (input.Username ?? string.Empty)
                     && (x.ResinPlatform ?? string.Empty) == (input.ResinPlatform ?? string.Empty),
                cancellationToken);
        if (duplicate)
            throw new InvalidOperationException("相同代理已存在");
    }

    private static OutboundProxyInput NormalizeInput(
        OutboundProxyInput input,
        OutboundProxy? existing)
    {
        ArgumentNullException.ThrowIfNull(input);
        var kind = (input.Kind ?? existing?.Kind ?? OutboundProxyKinds.Manual)
            .Trim()
            .ToLowerInvariant();
        var protocol = (input.Protocol ?? existing?.Protocol ?? OutboundProxyProtocols.Http)
            .Trim()
            .ToLowerInvariant();
        if (!OutboundProxyKinds.IsSupported(kind))
            throw new ArgumentException("代理类型仅支持 manual、resin 或 warp");
        if (!OutboundProxyProtocols.IsSupported(protocol))
            throw new ArgumentException("代理协议仅支持 http、socks5 或 mtproto");
        if (kind == OutboundProxyKinds.Resin && protocol == OutboundProxyProtocols.MtProto)
            throw new ArgumentException("Resin 仅支持 HTTP 或 SOCKS5 数据面接入");

        var host = NormalizeHost(input.Host ?? existing?.Host);
        var port = input.Port > 0 ? input.Port : existing?.Port ?? 0;
        if (port is < 1 or > 65535)
            throw new ArgumentException("代理端口必须在 1-65535 之间");

        var name = NormalizeName(input.Name, existing?.Name ?? $"{protocol}://{host}:{port}");
        var resinPlatform = NormalizeOptional(input.ResinPlatform ?? existing?.ResinPlatform, 100);
        if (kind == OutboundProxyKinds.Resin)
        {
            resinPlatform ??= "Default";
            if (resinPlatform.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_'))
                throw new ArgumentException("Resin Platform 仅允许字母、数字、横线和下划线");
        }
        else
        {
            resinPlatform = null;
        }

        var adminUrl = kind == OutboundProxyKinds.Resin
            ? NormalizeAdminUrl(input.ResinAdminUrl ?? existing?.ResinAdminUrl)
            : null;
        var secret = protocol == OutboundProxyProtocols.MtProto
            ? NormalizeOptional(PreserveSensitiveValue(input.Secret, existing?.Secret), 500)
            : null;
        if (protocol == OutboundProxyProtocols.MtProto && string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("MTProxy 必须填写 Secret");

        var resinAdminToken = kind == OutboundProxyKinds.Resin
            ? NormalizeOptional(
                PreserveSensitiveValue(input.ResinAdminToken, existing?.ResinAdminToken),
                500)
            : null;

        return input with
        {
            Name = name,
            Kind = kind,
            Protocol = protocol,
            Host = host,
            Port = port,
            Username = NormalizeOptional(input.Username ?? existing?.Username, 255),
            Password = NormalizeOptional(input.Password ?? existing?.Password, 500),
            Secret = secret,
            ResinPlatform = resinPlatform,
            ResinAdminUrl = adminUrl,
            ResinAdminToken = resinAdminToken
        };
    }

    private static string? PreserveSensitiveValue(string? incoming, string? existing) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming;

    private static string NormalizeName(string? value, string fallback)
    {
        value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (value.Length > 200)
            throw new ArgumentException("代理名称不能超过 200 个字符");
        return value;
    }

    private static string NormalizeHost(string? value)
    {
        value = (value ?? string.Empty).Trim().Trim('[', ']');
        if (value.Length is 0 or > 253
            || value.Any(char.IsControl)
            || value.Any(char.IsWhiteSpace)
            || value.Contains('/')
            || value.Contains('@'))
        {
            throw new ArgumentException("代理主机格式无效");
        }
        return value.ToLowerInvariant();
    }

    private static string? NormalizeAdminUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("Resin 管理地址必须是有效的 HTTP(S) URL");
        }
        return value;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentException($"字段长度不能超过 {maxLength}");
        return value;
    }

    private static string NormalizeStrategy(string? strategy)
    {
        strategy = (strategy ?? "direct").Trim().ToLowerInvariant();
        return strategy switch
        {
            "direct" or "global" or "existing" or "warp_per_account" => strategy,
            _ => throw new ArgumentException("代理策略仅支持 direct、global、existing 或 warp_per_account")
        };
    }

    private static OutboundProxyInput ParseImportLine(
        string line,
        bool testAfterImport)
    {
        var raw = line;
        if (!line.Contains("://", StringComparison.Ordinal))
        {
            var legacy = line.Split(':');
            if (legacy.Length == 4 && int.TryParse(legacy[1], out _))
            {
                raw = $"http://{Uri.EscapeDataString(legacy[2])}:{Uri.EscapeDataString(legacy[3])}@{legacy[0]}:{legacy[1]}";
            }
            else
            {
                raw = $"http://{line}";
            }
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.Port is < 1 or > 65535)
        {
            throw new ArgumentException($"代理地址格式无效：{line}");
        }

        var protocol = uri.Scheme.ToLowerInvariant() switch
        {
            "http" => OutboundProxyProtocols.Http,
            "https" => throw new ArgumentException(
                "不支持 HTTPS 代理地址：当前连接器只支持明文 HTTP CONNECT，请使用 http://"),
            "socks" or "socks5" or "socks5h" => OutboundProxyProtocols.Socks5,
            "mtproto" => OutboundProxyProtocols.MtProto,
            _ => throw new ArgumentException($"不支持的代理协议：{uri.Scheme}")
        };
        string? username = null;
        string? password = null;
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            username = Uri.UnescapeDataString(parts[0]);
            password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        }

        var secret = protocol == OutboundProxyProtocols.MtProto
            ? GetQueryValue(uri.Query, "secret")
            : null;
        return new OutboundProxyInput(
            Name: $"{protocol}://{uri.Host}:{uri.Port}",
            Kind: OutboundProxyKinds.Manual,
            Protocol: protocol,
            Host: uri.Host,
            Port: uri.Port,
            Username: username,
            Password: password,
            Secret: secret,
            ResinPlatform: null,
            ResinAdminUrl: null,
            ResinAdminToken: null,
            IsEnabled: true,
            TestAfterSave: testAfterImport);
    }

    private static string? GetQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.OrdinalIgnoreCase))
                return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }
        return null;
    }
}
