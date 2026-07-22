using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// WARP 配置仓储接口。
/// </summary>
public interface IWarpProfileRepository : IRepository<WarpProfile>
{
    Task<IReadOnlyList<WarpProfile>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<WarpProfile?> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<WarpProfile?> GetByProfileIdAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<WarpProfile?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken = default);

    Task<WarpProfile?> GetByProxyIdAsync(
        int proxyId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
