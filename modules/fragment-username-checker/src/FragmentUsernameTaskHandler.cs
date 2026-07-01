using FragmentUsernameChecker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;

namespace FragmentUsernameChecker;

/// <summary>
/// Fragment 用户名监控任务配置。
/// </summary>
public class FragmentUsernameTaskConfig
{
    /// <summary>要监控的用户名列表。</summary>
    public List<string> Usernames { get; set; } = new();

    /// <summary>目标频道分类 ID 列表。</summary>
    public List<int> TargetGroupIds { get; set; } = new();

    /// <summary>检查间隔（秒）。</summary>
    public int CheckIntervalSeconds { get; set; } = 300;

    /// <summary>查询延迟（毫秒），避免请求过快。</summary>
    public int QueryDelayMs { get; set; } = 1500;

    /// <summary>运行时长（小时），0 表示持续运行。</summary>
    public int DurationHours { get; set; }

    /// <summary>任务首次启动时间（UTC）。</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>已成功分配的用户名。</summary>
    public List<string> AssignedUsernames { get; set; } = new();

    /// <summary>上次检查时间。</summary>
    public DateTime? LastCheckTime { get; set; }

    /// <summary>错误信息。</summary>
    public string? Error { get; set; }

    /// <summary>是否已取消。</summary>
    public bool Canceled { get; set; }
}

/// <summary>
/// Fragment 用户名监控任务处理器。
/// </summary>
public sealed class FragmentUsernameTaskHandler : IModuleTaskHandler
{
    private static readonly Regex UsernameRegex = new("^[a-z][a-z0-9_]{4,31}$", RegexOptions.Compiled);

    public const string TaskType = "fragment_username_monitor";
    string IModuleTaskHandler.TaskType => TaskType;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var config = NormalizeConfig(
            JsonSerializer.Deserialize<FragmentUsernameTaskConfig>(host.Config ?? "{}")
            ?? new FragmentUsernameTaskConfig());

        if (config.Canceled)
            return;

        if (config.Usernames.Count == 0)
            throw new InvalidOperationException("至少需要一个待监控用户名");

        if (config.TargetGroupIds.Count == 0)
            throw new InvalidOperationException("至少需要一个目标频道分类");

        var logger = host.Services.GetRequiredService<ILogger<FragmentUsernameTaskHandler>>();
        var fragmentChecker = host.Services.GetRequiredService<FragmentCheckerService>();
        var channelService = host.Services.GetRequiredService<IChannelService>();
        var channelManagement = host.Services.GetRequiredService<ChannelManagementService>();
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();

        var random = new Random();

        config.StartedAtUtc ??= DateTime.UtcNow;
        config.Error = null;
        await SaveConfigAsync(taskManagement, host.TaskId, config);

        logger.LogInformation(
            "开始监控 {UsernameCount} 个用户名，目标分类 {GroupCount} 个",
            config.Usernames.Count,
            config.TargetGroupIds.Count);

        var round = 0;
        var keepWaitingWithoutChannel = IsContinuousMonitor(config);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await host.IsStillRunningAsync(cancellationToken))
                return;

            if (config.Canceled)
                return;

            round++;
            logger.LogInformation("开始第 {Round} 轮检查", round);

            if (config.DurationHours > 0)
            {
                var elapsed = DateTime.UtcNow - (config.StartedAtUtc ?? DateTime.UtcNow);
                if (elapsed.TotalHours >= config.DurationHours)
                {
                    logger.LogInformation("达到运行时长限制 {Hours} 小时，停止监控", config.DurationHours);
                    break;
                }
            }

            var targetChannels = await GetTargetChannelsAsync(
                channelManagement,
                config.TargetGroupIds,
                cancellationToken);

            var assignedUsernames = ReconcileAssignedUsernames(config.Usernames, targetChannels, config, logger);
            var pendingUsernames = config.Usernames
                .Where(x => !assignedUsernames.Contains(x))
                .ToList();
            if (pendingUsernames.Count == 0)
            {
                config.Error = null;
                await UpdateProgressAsync(host, assignedUsernames.Count, cancellationToken);
                await SaveConfigAsync(taskManagement, host.TaskId, config);

                if (!IsContinuousMonitor(config))
                {
                    logger.LogInformation("所有用户名已分配完成");
                    break;
                }

                logger.LogInformation("当前所有用户名都已绑定，持续监控模式将在下一轮继续校验");
                if (!await DelayWithPauseCheckAsync(host, TimeSpan.FromSeconds(config.CheckIntervalSeconds), cancellationToken))
                    return;
                continue;
            }

            var availableChannels = targetChannels
                .Where(x => x.CreatorAccountId.HasValue && x.CreatorAccountId.Value > 0)
                .Where(x => string.IsNullOrWhiteSpace(x.Username))
                .ToList();
            if (availableChannels.Count == 0)
            {
                config.Error = "没有可用私密频道";
                await SaveConfigAsync(taskManagement, host.TaskId, config);
                if (!keepWaitingWithoutChannel)
                {
                    logger.LogWarning("目标分类下没有可用私密频道，任务结束");
                    break;
                }

                logger.LogWarning("目标分类下暂时没有可用私密频道，将在下一轮继续等待");
                await UpdateProgressAsync(host, assignedUsernames.Count, cancellationToken);
                if (!await DelayWithPauseCheckAsync(host, TimeSpan.FromSeconds(config.CheckIntervalSeconds), cancellationToken))
                    return;
                continue;
            }

            var roundCompleted = 0;
            foreach (var username in pendingUsernames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (config.Canceled)
                    return;

                if (!await host.IsStillRunningAsync(cancellationToken))
                    return;

                var checkResult = await fragmentChecker.CheckUsernameAsync(username, cancellationToken);
                config.LastCheckTime = DateTime.UtcNow;

                if (!checkResult.IsAvailable)
                {
                    await UpdateProgressAsync(host, assignedUsernames.Count, cancellationToken);
                    await SaveConfigAsync(taskManagement, host.TaskId, config);
                    if (!await DelayWithPauseCheckAsync(host, TimeSpan.FromMilliseconds(config.QueryDelayMs), cancellationToken))
                        return;
                    continue;
                }

                logger.LogInformation("用户名 {Username} 未注册，开始从私密频道池选择频道", username);

                var selectedChannel = await TryAssignUsernameToPrivateChannelAsync(
                    username,
                    availableChannels,
                    channelManagement,
                    channelService,
                    logger,
                    random,
                    cancellationToken);

                if (selectedChannel is not null)
                {
                    config.Error = null;
                    roundCompleted++;
                    availableChannels.RemoveAll(x => x.Id == selectedChannel.Id);
                }
                else
                {
                    logger.LogWarning("用户名 {Username} 未注册，但没有找到可成功切换的私密频道", username);
                }

                var refreshedAssignedUsernames = ReconcileAssignedUsernames(config.Usernames, targetChannels, config, logger);
                await UpdateProgressAsync(host, refreshedAssignedUsernames.Count, cancellationToken);
                await SaveConfigAsync(taskManagement, host.TaskId, config);

                if (!await DelayWithPauseCheckAsync(host, TimeSpan.FromMilliseconds(config.QueryDelayMs), cancellationToken))
                    return;
            }

            config.LastCheckTime = DateTime.UtcNow;
            await SaveConfigAsync(taskManagement, host.TaskId, config);

            logger.LogInformation(
                "第 {Round} 轮完成：成功 {Completed}，剩余待监控 {Pending}",
                round,
                roundCompleted,
                pendingUsernames.Count - roundCompleted);

            if (config.CheckIntervalSeconds > 0 && pendingUsernames.Count > roundCompleted)
            {
                if (!await DelayWithPauseCheckAsync(host, TimeSpan.FromSeconds(config.CheckIntervalSeconds), cancellationToken))
                    return;
            }
        }

        logger.LogInformation("监控任务结束：当前已绑定 {Count} 个用户名", config.AssignedUsernames.Count);
    }

    private static bool IsContinuousMonitor(FragmentUsernameTaskConfig config)
    {
        return config.DurationHours <= 0;
    }

    private static FragmentUsernameTaskConfig NormalizeConfig(FragmentUsernameTaskConfig config)
    {
        config.Usernames = (config.Usernames ?? new List<string>())
            .Select(NormalizeUsername)
            .Where(x => UsernameRegex.IsMatch(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        config.TargetGroupIds = (config.TargetGroupIds ?? new List<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        config.AssignedUsernames = (config.AssignedUsernames ?? new List<string>())
            .Select(NormalizeUsername)
            .Where(x => UsernameRegex.IsMatch(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        config.CheckIntervalSeconds = Math.Clamp(config.CheckIntervalSeconds, 60, 3600);
        config.QueryDelayMs = Math.Clamp(config.QueryDelayMs, 0, 10000);
        config.DurationHours = Math.Clamp(config.DurationHours, 0, 720);
        return config;
    }

    private async Task<List<Channel>> GetTargetChannelsAsync(
        ChannelManagementService channelManagement,
        IReadOnlyCollection<int> groupIds,
        CancellationToken cancellationToken)
    {
        var channels = new List<Channel>();
        foreach (var groupId in groupIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupChannels = await channelManagement.GetChannelsByGroupAsync(groupId);
            channels.AddRange(groupChannels);
        }

        return channels
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .ToList();
    }

    private static HashSet<string> ReconcileAssignedUsernames(
        IReadOnlyCollection<string> monitoredUsernames,
        IReadOnlyCollection<Channel> targetChannels,
        FragmentUsernameTaskConfig config,
        ILogger logger)
    {
        var monitoredSet = new HashSet<string>(monitoredUsernames, StringComparer.OrdinalIgnoreCase);
        var currentAssigned = targetChannels
            .Where(x => !string.IsNullOrWhiteSpace(x.Username))
            .Select(x => NormalizeUsername(x.Username!))
            .Where(monitoredSet.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var previousAssigned = new HashSet<string>(config.AssignedUsernames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var removed = previousAssigned.Except(currentAssigned, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var added = currentAssigned.Except(previousAssigned, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        if (removed.Count > 0)
        {
            logger.LogInformation("检测到 {Count} 个用户名已不再绑定到目标频道，将恢复监控：{Usernames}", removed.Count, string.Join(", ", removed));
        }

        if (added.Count > 0)
        {
            logger.LogInformation("检测到 {Count} 个用户名当前已绑定到目标频道：{Usernames}", added.Count, string.Join(", ", added));
        }

        config.AssignedUsernames = currentAssigned
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return currentAssigned;
    }

    private static async Task<Channel?> TryAssignUsernameToPrivateChannelAsync(
        string username,
        IEnumerable<Channel> availableChannels,
        ChannelManagementService channelManagement,
        IChannelService channelService,
        ILogger logger,
        Random random,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = NormalizeUsername(username);
        var candidateChannels = availableChannels
            .OrderBy(_ => random.Next())
            .ToList();

        foreach (var channel in candidateChannels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accountId = await channelManagement.ResolveExecuteAccountIdAsync(channel);
            if (!accountId.HasValue)
            {
                logger.LogWarning("频道 {ChannelId} 没有可用执行账号，跳过", channel.Id);
                continue;
            }

            try
            {
                var success = await channelService.SetChannelVisibilityAsync(
                    accountId.Value,
                    channel.TelegramId,
                    isPublic: true,
                    username: normalizedUsername);
                if (!success)
                {
                    logger.LogWarning("频道 {ChannelId} 切换公开用户名失败", channel.Id);
                    continue;
                }

                channel.Username = normalizedUsername;
                await channelManagement.UpdateChannelAsync(channel);
                logger.LogInformation("成功将私密频道 {ChannelId} 切换为公开用户名 @{Username}", channel.Id, normalizedUsername);
                return channel;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "频道 {ChannelId} 设置 @{Username} 失败", channel.Id, normalizedUsername);
            }
        }

        return null;
    }

    private static async Task UpdateProgressAsync(
        IModuleTaskExecutionHost host,
        int completed,
        CancellationToken cancellationToken)
    {
        try
        {
            await host.UpdateProgressAsync(completed, 0, cancellationToken);
        }
        catch
        {
        }
    }

    private static async Task<bool> DelayWithPauseCheckAsync(
        IModuleTaskExecutionHost host,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
            return true;

        var remaining = delay;
        while (remaining > TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await host.IsStillRunningAsync(cancellationToken))
                return false;

            var slice = remaining > TimeSpan.FromSeconds(1)
                ? TimeSpan.FromSeconds(1)
                : remaining;

            await Task.Delay(slice, cancellationToken);
            remaining -= slice;
        }

        return true;
    }

    private static string NormalizeUsername(string value)
    {
        return (value ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
    }

    private static async Task SaveConfigAsync(
        BatchTaskManagementService taskManagement,
        int taskId,
        FragmentUsernameTaskConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await taskManagement.UpdateTaskConfigAsync(taskId, json);
        }
        catch
        {
        }
    }
}
