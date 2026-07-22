using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;

namespace TelegramPanel.Web.Services;

public sealed class WarpMaintenanceState
{
    private readonly object _gate = new();
    private bool _running;
    private DateTime? _lastRunAtUtc;
    private DateTime? _nextRunAtUtc;
    private string? _lastError;
    private int _checkedCount;
    private int _healthyCount;
    private int _recoveredCount;
    private int _failedCount;

    public void Schedule(DateTime? nextRunAtUtc)
    {
        lock (_gate)
            _nextRunAtUtc = nextRunAtUtc;
    }

    public void MarkRunning()
    {
        lock (_gate)
        {
            _running = true;
            _nextRunAtUtc = null;
        }
    }

    public void Complete(
        WarpMaintenanceBatchResult? result,
        string? error,
        DateTime? nextRunAtUtc)
    {
        lock (_gate)
        {
            _running = false;
            _lastRunAtUtc = DateTime.UtcNow;
            _nextRunAtUtc = nextRunAtUtc;
            _lastError = error;
            _checkedCount = result?.Checked ?? 0;
            _healthyCount = result?.Healthy ?? 0;
            _recoveredCount = result?.Recovered ?? 0;
            _failedCount = result?.Failed ?? 0;
        }
    }

    public WarpMaintenanceRuntimeStatus Snapshot(WarpMaintenanceOptions options)
    {
        lock (_gate)
        {
            return new WarpMaintenanceRuntimeStatus(
                options.Enabled,
                _running,
                options.HealthCheckIntervalMinutes,
                options.FailureThreshold,
                options.RecoveryCooldownMinutes,
                options.ScheduledRefreshEnabled,
                options.ScheduledRefreshIntervalMinutes,
                _lastRunAtUtc,
                _nextRunAtUtc,
                _lastError,
                _checkedCount,
                _healthyCount,
                _recoveredCount,
                _failedCount);
        }
    }
}

/// <summary>
/// 定期检查受管 WARP 出口。容器进程退出由 Docker restart policy 处理；
/// 本服务负责处理“容器仍在运行但 WARP/GOST 已失效”的情况。
/// </summary>
public sealed class WarpMaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly WarpMaintenanceState _state;
    private readonly ILogger<WarpMaintenanceBackgroundService> _logger;

    public WarpMaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        WarpMaintenanceState state,
        ILogger<WarpMaintenanceBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = WarpMaintenanceOptions.From(_configuration);
        if (!options.Enabled)
        {
            _state.Schedule(null);
            _logger.LogInformation("Managed WARP maintenance is disabled");
            return;
        }

        var firstRunAtUtc = DateTime.UtcNow.AddSeconds(options.InitialDelaySeconds);
        _state.Schedule(firstRunAtUtc);
        if (options.InitialDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(options.InitialDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            options = WarpMaintenanceOptions.From(_configuration);
            if (!options.Enabled)
            {
                _state.Schedule(null);
                return;
            }

            _state.MarkRunning();
            WarpMaintenanceBatchResult? result = null;
            string? error = null;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ProxyManagementService>();
                result = await service.MaintainAllWarpAsync(
                    options,
                    forceRestart: false,
                    stoppingToken);
                if (result.Failed > 0)
                    error = $"{result.Failed} 个 WARP 出口仍异常，后台会按阈值和冷却策略继续恢复";

                if (result.Recovered > 0)
                {
                    _logger.LogInformation(
                        "Managed WARP maintenance recovered {Recovered}/{Checked} proxies",
                        result.Recovered,
                        result.Checked);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _state.Complete(result, "WARP 自动维护已随服务停止", null);
                break;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogError(ex, "Managed WARP maintenance sweep failed");
            }

            var nextRunAtUtc = DateTime.UtcNow.AddMinutes(options.HealthCheckIntervalMinutes);
            _state.Complete(result, error, nextRunAtUtc);
            await Task.Delay(
                TimeSpan.FromMinutes(options.HealthCheckIntervalMinutes),
                stoppingToken);
        }
    }
}
