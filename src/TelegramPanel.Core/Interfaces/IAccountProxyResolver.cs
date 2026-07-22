using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 解析账号最终使用的 Telegram 连接路由。
/// </summary>
public interface IAccountProxyResolver
{
    Task<AccountProxyResolution> ResolveAsync(
        int accountId,
        CancellationToken cancellationToken = default);
}
