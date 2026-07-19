using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 频道分组仓储实现
/// </summary>
public class ChannelGroupRepository : Repository<ChannelGroup>, IChannelGroupRepository
{
    public ChannelGroupRepository(AppDbContext context) : base(context)
    {
    }

    public override async Task<IEnumerable<ChannelGroup>> GetAllAsync()
    {
        return await _dbSet
            .Include(g => g.Channels)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<ChannelGroup?> GetByNameAsync(string name)
    {
        return await _dbSet
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.Name == name);
    }

    public async Task<bool> NameExistsAsync(
        string name,
        int? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = (name ?? string.Empty).Trim().ToUpperInvariant();
        return await _dbSet
            .AsNoTracking()
            .AnyAsync(
                g => g.Name.ToUpper() == normalized
                     && (!excludingId.HasValue || g.Id != excludingId.Value),
                cancellationToken);
    }
}
