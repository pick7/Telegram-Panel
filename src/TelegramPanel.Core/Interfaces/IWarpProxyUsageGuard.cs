namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 协调受管 WARP 的登录占用与维护窗口。
/// </summary>
public interface IWarpProxyUsageGuard
{
    bool OwnsWarpProxy(int proxyId);

    /// <summary>
    /// 尝试冻结一个 WARP 供首次连接流程使用。代理正在维护或已被另一流程占用时返回 null。
    /// </summary>
    IDisposable? TryAcquireUsage(int proxyId);

    /// <summary>
    /// 尝试独占一个 WARP 的维护窗口。代理正被登录或导入流程使用时返回 null。
    /// </summary>
    IDisposable? TryAcquireMaintenance(int proxyId);
}
