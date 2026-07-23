using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;

namespace TelegramPanel.Core.Services;

/// <summary>
/// 批量任务管理服务
/// </summary>
public class BatchTaskManagementService
{
    private readonly IBatchTaskRepository _batchTaskRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BatchTaskManagementService> _logger;

    public BatchTaskManagementService(
        IBatchTaskRepository batchTaskRepository,
        IConfiguration configuration,
        ILogger<BatchTaskManagementService> logger)
    {
        _batchTaskRepository = batchTaskRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<BatchTask?> GetTaskAsync(int id)
    {
        return await _batchTaskRepository.GetFreshByIdAsync(id);
    }

    public async Task<IEnumerable<BatchTask>> GetAllTasksAsync()
    {
        return await _batchTaskRepository.GetAllAsync();
    }

    public async Task<IEnumerable<BatchTask>> GetTasksByStatusAsync(string status)
    {
        return await _batchTaskRepository.GetByStatusAsync(status);
    }

    public async Task<IEnumerable<BatchTask>> GetRunningTasksAsync()
    {
        return await _batchTaskRepository.GetRunningTasksAsync();
    }

    public async Task<IReadOnlyList<BatchTask>> GetActiveTasksAsync(CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.GetActiveTasksAsync(cancellationToken);
    }

    public async Task<IEnumerable<BatchTask>> GetRecentTasksAsync(int count = 20)
    {
        return await _batchTaskRepository.GetRecentTasksAsync(count);
    }

    public async Task<IReadOnlyList<BatchTask>> GetTaskCenterItemsAsync(int historyCount = 100, CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.GetTaskCenterItemsAsync(historyCount, cancellationToken);
    }

    public async Task<int> CountActiveTasksAsync(CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.CountActiveTasksAsync(cancellationToken);
    }

    public async Task<int> TrimHistoryTasksAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.TrimHistoryTasksAsync(keepCount, cancellationToken);
    }

    public async Task<BatchTask> CreateTaskAsync(BatchTask task)
    {
        task.CreatedAt = DateTime.UtcNow;
        task.Status = "pending";
        return await _batchTaskRepository.AddAsync(task);
    }

    public async Task UpdateTaskProgressAsync(int taskId, int completed, int failed)
    {
        await _batchTaskRepository.UpdateProgressColumnsAsync(taskId, completed, failed);
    }

    public async Task UpdateTaskConfigAsync(int taskId, string? config)
    {
        await _batchTaskRepository.UpdateConfigColumnAsync(taskId, config);
    }

    public async Task UpdateTaskDraftAsync(int taskId, int total, string? config)
    {
        if (total < 0) total = 0;
        await _batchTaskRepository.UpdateDraftColumnsAsync(taskId, total, config);
    }

    public async Task<bool> TryUpdateEditableTaskDraftAsync(
        int taskId,
        int total,
        string? config,
        CancellationToken cancellationToken = default)
    {
        if (total < 0) total = 0;
        return await _batchTaskRepository.TryUpdateEditableDraftAsync(
            taskId,
            total,
            config,
            cancellationToken);
    }

    public async Task StartTaskAsync(int taskId)
    {
        await TryStartTaskAsync(taskId);
    }

    public async Task<bool> TryStartTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.TryStartAsync(taskId, DateTime.UtcNow, cancellationToken);
    }

    public async Task PauseTaskAsync(int taskId)
    {
        await TryPauseTaskAsync(taskId);
    }

    public async Task<bool> TryPauseTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.TryPauseAsync(taskId, cancellationToken);
    }

    public async Task ResumeTaskAsync(int taskId)
    {
        await TryResumeTaskAsync(taskId);
    }

    public async Task<bool> TryResumeTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        return await _batchTaskRepository.TryResumeAsync(taskId, cancellationToken);
    }

    public async Task<int> RequeueRunningTasksAsync(
        Func<BatchTask, bool>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        var runningTasks = (await _batchTaskRepository.GetByStatusAsync("running")).ToList();
        if (runningTasks.Count == 0)
            return 0;

        var requeued = 0;
        foreach (var task in runningTasks)
        {
            if (predicate != null && !predicate(task))
                continue;

            if (await _batchTaskRepository.TryRequeueAsync(task.Id, cancellationToken))
                requeued++;
        }

        return requeued;
    }

    public async Task CompleteTaskAsync(int taskId, bool success = true)
    {
        var transitioned = await _batchTaskRepository.TryCompleteAsync(
            taskId,
            success,
            DateTime.UtcNow);
        if (!transitioned)
            return;

        await TrimHistoryTasksIfNeededAsync();
    }

    public async Task CancelTaskAsync(int taskId)
    {
        await TryCancelTaskAsync(taskId);
    }

    public async Task<bool> TryCancelTaskAsync(
        int taskId,
        CancellationToken cancellationToken = default)
    {
        var transitioned = await _batchTaskRepository.TryCancelAsync(
            taskId,
            DateTime.UtcNow,
            cancellationToken);
        if (!transitioned)
            return false;

        await TrimHistoryTasksIfNeededAsync(cancellationToken);
        return true;
    }

    public async Task DeleteTaskAsync(int id)
    {
        var task = await _batchTaskRepository.GetFreshByIdAsync(id);
        if (task != null)
        {
            await _batchTaskRepository.DeleteAsync(task);
        }
    }

    private async Task TrimHistoryTasksIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var keepCount = GetHistoryRetentionLimit();
        if (keepCount <= 0)
            return;

        var deletedCount = await _batchTaskRepository.TrimHistoryTasksAsync(keepCount, cancellationToken);
        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Trimmed {DeletedCount} historical batch tasks, keepCount={KeepCount}",
                deletedCount,
                keepCount);
        }
    }

    private int GetHistoryRetentionLimit()
    {
        var rawValue = _configuration["BatchTasks:HistoryRetentionLimit"];
        if (!int.TryParse(rawValue, out var keepCount) || keepCount < 0)
            return 0;

        return Math.Min(keepCount, 5000);
    }
}
