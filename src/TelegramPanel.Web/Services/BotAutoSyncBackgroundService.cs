using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;

namespace TelegramPanel.Web.Services;

/// <summary>
/// Bot 频道自动同步（轮询），用于在 Bot 被拉进新频道后自动出现在列表里。
/// </summary>
public class BotAutoSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotAutoSyncBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly BotUpdateHub _updateHub;

    public BotAutoSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        BotUpdateHub updateHub,
        IConfiguration configuration,
        ILogger<BotAutoSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _updateHub = updateHub;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 默认开启：满足“Bot 拉进频道后自动出现”的直觉
        var enabled = _configuration.GetValue("Telegram:BotAutoSyncEnabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Bot auto sync disabled (Telegram:BotAutoSyncEnabled=false)");
            return;
        }

        // 这里的 interval 用于：刷新 bot 列表 + 批量 drain 已收集的 updates（避免每条 update 都写 DB）
        var seconds = _configuration.GetValue("Telegram:BotAutoSyncIntervalSeconds", 2);
        if (seconds < 2) seconds = 2;
        if (seconds > 60) seconds = 60;
        var interval = TimeSpan.FromSeconds(seconds);

        _logger.LogInformation("Bot auto sync started (via BotUpdateHub), interval {IntervalSeconds} seconds", seconds);

        // 延迟一点，避免与启动时 DB 迁移抢资源
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var subscriptions = new Dictionary<int, BotUpdateHub.BotUpdateSubscription>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(subscriptions, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bot auto sync loop failed");
            }

            await Task.Delay(interval, stoppingToken);
        }

        foreach (var sub in subscriptions.Values)
        {
            try { await sub.DisposeAsync(); }
            catch { /* ignore */ }
        }
    }

    private async Task SyncOnceAsync(
        Dictionary<int, BotUpdateHub.BotUpdateSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var botManagement = scope.ServiceProvider.GetRequiredService<BotManagementService>();
        var botTelegram = scope.ServiceProvider.GetRequiredService<BotTelegramService>();

        var bots = (await botManagement.GetAllBotsAsync()).Where(b => b.IsActive).ToList();
        if (bots.Count == 0)
            return;

        // 1) 确保订阅已建立（每个 botId 一个订阅即可）
        var aliveBotIds = bots.Select(b => b.Id).ToHashSet();
        foreach (var bot in bots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (subscriptions.ContainsKey(bot.Id))
                continue;

            try
            {
                subscriptions[bot.Id] = await _updateHub.SubscribeAsync(bot.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bot auto sync subscribe failed for bot {BotId}", bot.Id);
            }
        }

        // 2) 清理已停用/已删除 bot 的订阅
        foreach (var staleId in subscriptions.Keys.Where(id => !aliveBotIds.Contains(id)).ToList())
        {
            if (subscriptions.Remove(staleId, out var sub))
            {
                try { await sub.DisposeAsync(); }
                catch { /* ignore */ }
            }
        }

        // 3) 批量 drain：只取 my_chat_member，按 botId 分批应用（去重在 BotTelegramService 内部做）
        foreach (var bot in bots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!subscriptions.TryGetValue(bot.Id, out var sub))
                continue;

            var batch = new List<JsonElement>();
            while (sub.Reader.TryRead(out var update))
            {
                if (update.ValueKind == System.Text.Json.JsonValueKind.Object
                    && update.TryGetProperty("my_chat_member", out _))
                {
                    batch.Add(update);
                    if (batch.Count >= 200)
                        break;
                }
            }

            if (batch.Count == 0)
                continue;

            try
            {
                var count = await botTelegram.ApplyMyChatMemberUpdatesAsync(bot.Id, batch, cancellationToken);
                if (count > 0)
                    _logger.LogInformation("Bot auto sync: bot {BotId} applied {Count} updates", bot.Id, count);
                else
                    _logger.LogDebug("Bot auto sync: bot {BotId} applied 0 updates", bot.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bot auto sync apply failed for bot {BotId}", bot.Id);
            }
        }
    }
}
