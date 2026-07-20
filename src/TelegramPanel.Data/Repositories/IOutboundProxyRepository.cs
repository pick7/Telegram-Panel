using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 出站代理仓储接口。
/// </summary>
public interface IOutboundProxyRepository : IRepository<OutboundProxy>
{
    Task<IReadOnlyList<OutboundProxy>> ListAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default);

    Task<OutboundProxy?> GetAsync(
        int id,
        bool includeAccounts = false,
        CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAccountUsesAsync(
        int proxyId,
        CancellationToken cancellationToken = default);

    Task<int> BindAccountsAsync(
        IEnumerable<int> accountIds,
        int? proxyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Account>> GetAccountsAsync(
        int proxyId,
        CancellationToken cancellationToken = default);
}
