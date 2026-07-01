using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

/// <summary>
/// 批量任务仓储实现
/// </summary>
public class BatchTaskRepository : Repository<BatchTask>, IBatchTaskRepository
{
    public BatchTaskRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<BatchTask?> GetFreshByIdAsync(int id)
    {
        DetachTrackedEntity(id);
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task UpdateFreshAsync(BatchTask entity)
    {
        DetachTrackedEntity(entity.Id);
        _dbSet.Update(entity);
        await SaveChangesWithSqliteLockRetryAsync();
    }

    public async Task<IEnumerable<BatchTask>> GetByStatusAsync(string status)
    {
        return await _dbSet
            .Where(t => t.Status == status)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<BatchTask>> GetRunningTasksAsync()
    {
        return await _dbSet
            .Where(t => t.Status == "running")
            .OrderBy(t => t.StartedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BatchTask>> GetActiveTasksAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(t => t.Status == "pending" || t.Status == "running" || t.Status == "paused")
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => ToListItem(t))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BatchTask>> GetRecentTasksAsync(int count = 20)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(t => t.Status == "completed" || t.Status == "failed" || t.Status == "canceled")
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .Select(t => ToListItem(t))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<BatchTask>> GetTaskCenterItemsAsync(int historyCount = 100, CancellationToken cancellationToken = default)
    {
        if (historyCount <= 0)
            historyCount = 100;
        if (historyCount > 500)
            historyCount = 500;

        var activeTasks = await _dbSet
            .AsNoTracking()
            .Where(t => t.Status == "pending" || t.Status == "running" || t.Status == "paused")
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => ToListItem(t))
            .ToListAsync(cancellationToken);

        var historyTasks = await _dbSet
            .AsNoTracking()
            .Where(t => t.Status == "completed" || t.Status == "failed" || t.Status == "canceled")
            .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(historyCount)
            .Select(t => ToListItem(t))
            .ToListAsync(cancellationToken);

        return activeTasks
            .Concat(historyTasks)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
    }

    private static BatchTask ToListItem(BatchTask task) => new()
    {
        Id = task.Id,
        TaskType = task.TaskType,
        Status = task.Status,
        Total = task.Total,
        Completed = task.Completed,
        Failed = task.Failed,
        Config = null,
        CreatedAt = task.CreatedAt,
        StartedAt = task.StartedAt,
        CompletedAt = task.CompletedAt
    };

    public async Task<int> CountActiveTasksAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .CountAsync(t => t.Status == "pending" || t.Status == "running" || t.Status == "paused", cancellationToken);
    }

    public async Task<int> TrimHistoryTasksAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        if (keepCount <= 0)
            return 0;

        var staleTasks = await _dbSet
            .Where(t => t.Status == "completed" || t.Status == "failed" || t.Status == "canceled")
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip(keepCount)
            .ToListAsync(cancellationToken);

        if (staleTasks.Count == 0)
            return 0;

        _dbSet.RemoveRange(staleTasks);
        await SaveChangesWithSqliteLockRetryAsync(cancellationToken);
        return staleTasks.Count;
    }

    private void DetachTrackedEntity(int id)
    {
        foreach (var entry in _context.ChangeTracker.Entries<BatchTask>())
        {
            if (entry.Entity.Id == id)
                entry.State = EntityState.Detached;
        }
    }
}
