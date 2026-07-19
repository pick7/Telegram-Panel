using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 频道仓储接口
/// </summary>
public interface IChannelRepository : IRepository<Channel>
{
    Task<Channel?> GetByTelegramIdAsync(long telegramId);
    Task<IEnumerable<Channel>> GetCreatedAsync();
    Task<IEnumerable<Channel>> GetByCreatorAccountAsync(int accountId);
    Task<IEnumerable<Channel>> GetForAccountAsync(int accountId, bool includeNonCreator);
    Task<IEnumerable<Channel>> GetByGroupAsync(int groupId);
    Task<IEnumerable<Channel>> GetBroadcastChannelsAsync();
    /// <summary>
    /// 一次性保存目标频道分组的绑定，避免逐条查询/提交导致请求超时。
    /// </summary>
    Task<int> UpdateGroupAssignmentsAsync(
        IReadOnlyCollection<int> scopeIds,
        IReadOnlySet<int> selectedIds,
        int? groupId,
        CancellationToken cancellationToken = default);
    Task<int> DeleteOrphanedAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Channel> Items, int TotalCount)> QueryForViewPagedAsync(
        int accountId,
        int? groupId,
        string? filterType,
        string? membershipRole,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}
