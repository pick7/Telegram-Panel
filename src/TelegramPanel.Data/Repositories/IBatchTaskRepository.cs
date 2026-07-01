using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 批量任务仓储接口
/// </summary>
public interface IBatchTaskRepository : IRepository<BatchTask>
{
    Task<BatchTask?> GetFreshByIdAsync(int id);
    Task UpdateFreshAsync(BatchTask entity);
    Task<IEnumerable<BatchTask>> GetByStatusAsync(string status);
    Task<IEnumerable<BatchTask>> GetRunningTasksAsync();
    Task<IReadOnlyList<BatchTask>> GetActiveTasksAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<BatchTask>> GetRecentTasksAsync(int count = 20);
    Task<IReadOnlyList<BatchTask>> GetTaskCenterItemsAsync(int historyCount = 100, CancellationToken cancellationToken = default);
    Task<int> CountActiveTasksAsync(CancellationToken cancellationToken = default);
    Task<int> TrimHistoryTasksAsync(int keepCount, CancellationToken cancellationToken = default);
}
