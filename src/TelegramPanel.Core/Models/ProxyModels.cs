namespace TelegramPanel.Core.Models;

/// <summary>
/// 出站代理类型。
/// </summary>
public static class OutboundProxyKinds
{
    public const string Manual = "manual";
    public const string Resin = "resin";
    public const string Warp = "warp";

    public static bool IsSupported(string? value) =>
        value is Manual or Resin or Warp;
}

/// <summary>
/// Telegram 支持的代理协议。
/// </summary>
public static class OutboundProxyProtocols
{
    public const string Http = "http";
    public const string Socks5 = "socks5";
    public const string MtProto = "mtproto";

    public static bool IsSupported(string? value) =>
        value is Http or Socks5 or MtProto;
}

/// <summary>
/// 连接代理所需的运行时参数。
/// </summary>
public sealed record ProxyConnectionOptions(
    int ProxyId,
    string Name,
    string Kind,
    string Protocol,
    string Host,
    int Port,
    string? Username,
    string? Password,
    string? Secret);

/// <summary>
/// 账号的最终代理路由；Proxy 为空时由 UseGlobalProxy 区分全局配置与显式直连。
/// </summary>
public sealed record AccountProxyResolution(
    ProxyConnectionOptions? Proxy,
    bool UseGlobalProxy);

/// <summary>
/// 公网出口检测结果。
/// </summary>
public sealed record EgressProbeResult(
    bool Success,
    string? Ip,
    string? Country,
    string? City,
    string? Isp,
    string? WarpStatus,
    int? LatencyMs,
    DateTime CheckedAtUtc,
    string? Error);

/// <summary>
/// 代理保存输入。
/// </summary>
public sealed record OutboundProxyInput(
    string? Name,
    string? Kind,
    string? Protocol,
    string? Host,
    int Port,
    string? Username,
    string? Password,
    string? Secret,
    string? ResinPlatform,
    string? ResinAdminUrl,
    string? ResinAdminToken,
    bool IsEnabled = true,
    bool TestAfterSave = false);

/// <summary>
/// 账号代理绑定输入。
/// </summary>
public sealed record AccountProxyBindingInput(
    string Strategy,
    int? ProxyId = null,
    int? ExpectedProxyId = null);

/// <summary>
/// 单个账号代理操作结果。
/// </summary>
public sealed record AccountProxyOperationResult(
    int AccountId,
    string? Phone,
    bool Success,
    string Summary,
    string? Error,
    int? ProxyId = null);

/// <summary>
/// 批量账号代理操作结果。
/// </summary>
public sealed record AccountProxyBatchResult(
    int Success,
    int Failed,
    IReadOnlyList<AccountProxyOperationResult> Items);

/// <summary>
/// WARP 运行环境状态。
/// </summary>
public sealed record WarpRuntimeStatus(
    bool PlatformSupported,
    bool Enabled,
    bool DockerAvailable,
    string? DockerVersion,
    string? Error,
    string Image,
    string Network,
    string ProxyHostMode);
