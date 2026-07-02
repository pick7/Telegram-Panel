using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 轻量刷新“连接失败/超时”等临时状态，避免账号列表长期停留在误判结果。
/// </summary>
public sealed class AccountStatusAutoRefreshBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountStatusAutoRefreshBackgroundService> _logger;

    public AccountStatusAutoRefreshBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AccountStatusAutoRefreshBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var enabled = _configuration.GetValue("TelegramStatus:AutoRefreshTransientFailures", true);
            var intervalMinutes = Math.Clamp(_configuration.GetValue("TelegramStatus:AutoRefreshIntervalMinutes", 30), 5, 1440);

            if (enabled)
            {
                try
                {
                    await RefreshOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // ignore
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Account transient status auto refresh failed");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_configuration.GetValue("TelegramStatus:AutoRefreshBatchSize", 3), 1, 20);
        var minAgeMinutes = Math.Clamp(_configuration.GetValue("TelegramStatus:AutoRefreshMinAgeMinutes", 10), 1, 1440);
        var delayMs = Math.Clamp(_configuration.GetValue("TelegramStatus:AutoRefreshDelayMs", 5000), 0, 60000);

        using var scope = _scopeFactory.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountManagementService>();
        var accountTools = scope.ServiceProvider.GetRequiredService<AccountTelegramToolsService>();

        var targets = await accounts.GetTransientFailedStatusAccountsAsync(
            batchSize,
            TimeSpan.FromMinutes(minAgeMinutes),
            cancellationToken);

        if (targets.Count == 0)
            return;

        _logger.LogInformation("Refreshing {Count} transient failed Telegram account statuses", targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = targets[i];

            try
            {
                var status = await accountTools.RefreshAccountStatusAsync(
                    account.Id,
                    probeCreateChannel: false,
                    cancellationToken: cancellationToken);

                if (status.Ok)
                    _logger.LogInformation("Transient account status recovered: {AccountId} {Phone}", account.Id, account.DisplayPhone);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Transient account status refresh failed: {AccountId}", account.Id);
            }

            if (delayMs > 0 && i < targets.Count - 1)
                await Task.Delay(delayMs, cancellationToken);
        }
    }
}
