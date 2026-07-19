using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 群组仓储接口
/// </summary>
public interface IGroupRepository : IRepository<Group>
{
    Task<Group?> GetByTelegramIdAsync(long telegramId);
    Task<IEnumerable<Group>> GetByCreatorAccountAsync(int accountId);
    /// <summary>
    /// 一次性保存目标群组分类的绑定，避免逐条查询/提交导致请求超时。
    /// </summary>
    Task<int> UpdateCategoryAssignmentsAsync(
        IReadOnlyCollection<int> scopeIds,
        IReadOnlySet<int> selectedIds,
        int? categoryId,
        CancellationToken cancellationToken = default);
    Task<int> DeleteOrphanedAsync(CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Group> Items, int TotalCount)> QueryForViewPagedAsync(
        int accountId,
        int? categoryId,
        string? filterType,
        string? membershipRole,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}
