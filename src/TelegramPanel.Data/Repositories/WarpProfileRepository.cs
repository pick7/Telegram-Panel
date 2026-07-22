using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// WARP 配置仓储实现。
/// </summary>
public class WarpProfileRepository : Repository<WarpProfile>, IWarpProfileRepository
{
    public WarpProfileRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<WarpProfile>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(x => x.Proxy)
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<WarpProfile?> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return _dbSet
            .Include(x => x.Proxy)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<WarpProfile?> GetByProfileIdAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return _dbSet
            .Include(x => x.Proxy)
            .FirstOrDefaultAsync(x => x.ProfileId == profileId, cancellationToken);
    }

    public Task<WarpProfile?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return _dbSet
            .Include(x => x.Proxy)
            .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
    }

    public Task<WarpProfile?> GetByProxyIdAsync(
        int proxyId,
        CancellationToken cancellationToken = default)
    {
        return _dbSet
            .Include(x => x.Proxy)
            .FirstOrDefaultAsync(x => x.OutboundProxyId == proxyId, cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await SaveChangesWithSqliteLockRetryAsync(cancellationToken);
    }
}
