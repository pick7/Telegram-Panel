using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Data.Repositories;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// Bot API 更新分发 Hub：
/// - 同一个 Bot Token 只允许一个 getUpdates 长轮询，否则会 409 Conflict
/// - 这里为每个 botId 维护一个单一轮询器，并把同一份 updates 广播给多个消费者（模块/后台服务）
/// </summary>
public sealed class BotUpdateHub : IAsyncDisposable
{
    // 固定允许的更新类型：覆盖当前项目使用场景（转发/监听/入群事件）
    private const string AllowedUpdatesJson = "[\"message\",\"edited_message\",\"channel_post\",\"edited_channel_post\",\"my_chat_member\"]";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramBotApiClient _botApi;
    private readonly ILogger<BotUpdateHub> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, BotPoller> _pollersByToken = new(StringComparer.Ordinal);

    public BotUpdateHub(
        IServiceScopeFactory scopeFactory,
        TelegramBotApiClient botApi,
        ILogger<BotUpdateHub> logger)
    {
        _scopeFactory = scopeFactory;
        _botApi = botApi;
        _logger = logger;
    }

    public async Task<BotUpdateSubscription> SubscribeAsync(int botId, CancellationToken cancellationToken)
    {
        if (botId <= 0)
            throw new ArgumentException("botId 无效", nameof(botId));

        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var botRepo = scope.ServiceProvider.GetRequiredService<IBotRepository>();
            var bot = await botRepo.GetByIdAsync(botId);
            if (bot == null)
                throw new InvalidOperationException($"Bot 不存在：{botId}");
            if (!bot.IsActive)
                throw new InvalidOperationException("Bot 未启用");

            var token = (bot.Token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Bot Token 为空");

            if (!_pollersByToken.TryGetValue(token, out var poller))
            {
                poller = await BotPoller.CreateAsync(botId, token, bot.LastUpdateId, _scopeFactory, _botApi, _logger, cancellationToken);
                _pollersByToken[token] = poller;
            }

            return poller.Subscribe();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<BotPoller> pollers;

        await _gate.WaitAsync();
        try
        {
            pollers = _pollersByToken.Values.ToList();
            _pollersByToken.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var p in pollers)
        {
            try { await p.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Dispose bot poller failed: {BotId}", p.BotId); }
        }
    }

    public sealed class BotUpdateSubscription : IAsyncDisposable
    {
        private readonly Func<ValueTask> _dispose;

        internal BotUpdateSubscription(int botId, ChannelReader<JsonElement> reader, Func<ValueTask> dispose)
        {
            BotId = botId;
            Reader = reader;
            _dispose = dispose;
        }

        public int BotId { get; }
        public ChannelReader<JsonElement> Reader { get; }

        public ValueTask DisposeAsync() => _dispose();
    }

    private sealed class BotPoller : IAsyncDisposable
    {
        private static readonly BoundedChannelOptions SubscriberChannelOptions = new(512)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        };

        private readonly int _persistBotId;
        private readonly string _token;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TelegramBotApiClient _botApi;
        private readonly ILogger _logger;

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;

        private readonly object _subscribersLock = new();
        private readonly Dictionary<Guid, Channel<JsonElement>> _subscribers = new();

        private long _nextOffset;

        public int BotId => _persistBotId;

        private BotPoller(
            int persistBotId,
            string token,
            long nextOffset,
            IServiceScopeFactory scopeFactory,
            TelegramBotApiClient botApi,
            ILogger logger)
        {
            _persistBotId = persistBotId;
            _token = token;
            _nextOffset = nextOffset;
            _scopeFactory = scopeFactory;
            _botApi = botApi;
            _logger = logger;

            _loopTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public static async Task<BotPoller> CreateAsync(
            int botId,
            string token,
            long? lastUpdateId,
            IServiceScopeFactory scopeFactory,
            TelegramBotApiClient botApi,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // 初始化 offset：
            // - 如果数据库里已有 LastUpdateId，则从其后开始
            // - 否则快进到最新，避免冷启动回放历史消息造成刷屏
            long nextOffset;
            if (lastUpdateId.HasValue)
            {
                nextOffset = lastUpdateId.Value + 1;
            }
            else
            {
                nextOffset = await FastForwardOffsetAsync(botId, token, scopeFactory, botApi, logger, cancellationToken);
            }

            return new BotPoller(botId, token, nextOffset, scopeFactory, botApi, logger);
        }

        public BotUpdateSubscription Subscribe()
        {
            var id = Guid.NewGuid();
            var ch = Channel.CreateBounded<JsonElement>(SubscriberChannelOptions);

            lock (_subscribersLock)
            {
                _subscribers[id] = ch;
            }

            return new BotUpdateSubscription(_persistBotId, ch.Reader, async () =>
            {
                Channel<JsonElement>? removed = null;
                lock (_subscribersLock)
                {
                    if (_subscribers.Remove(id, out var existing))
                        removed = existing;
                }

                if (removed != null)
                {
                    try { removed.Writer.TryComplete(); }
                    catch { /* ignore */ }
                }

                await ValueTask.CompletedTask;
            });
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // 给启动期一点喘息
            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (OperationCanceledException) { return; }

            var conflictStreak = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 没有订阅者时也继续拉取并确认 offset，避免积压导致后续刷屏；
                    // 同时还能保证 Bot 频道自动同步等后台能力可工作。

                    var updates = await _botApi.CallAsync(_token, "getUpdates", new Dictionary<string, string?>
                    {
                        ["offset"] = _nextOffset.ToString(),
                        ["timeout"] = "25",
                        ["limit"] = "100",
                        ["allowed_updates"] = AllowedUpdatesJson
                    }, cancellationToken);

                    if (updates.ValueKind != JsonValueKind.Array)
                        continue;

                    long? maxUpdateId = null;
                    foreach (var update in updates.EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!TryGetUpdateId(update, out var updateId))
                            continue;

                        maxUpdateId = maxUpdateId.HasValue ? Math.Max(maxUpdateId.Value, updateId) : updateId;

                        Broadcast(update);
                    }

                    if (maxUpdateId.HasValue)
                    {
                        _nextOffset = maxUpdateId.Value + 1;
                        await SaveLastUpdateIdAsync(maxUpdateId.Value, cancellationToken);
                    }

                    conflictStreak = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("getUpdates (409)", StringComparison.OrdinalIgnoreCase))
                {
                    // 典型原因：
                    // - 同一个 bot token 还有别的实例在 long-poll
                    // - 或者本进程有 bug 造成并发（理论上 Hub 已避免）
                    conflictStreak++;
                    var backoffSeconds = Math.Min(60, 2 * conflictStreak);
                    _logger.LogWarning(ex, "Bot getUpdates 409 conflict: botId={BotId}（请确保该 Bot Token 仅有一个实例在轮询 getUpdates）", _persistBotId);
                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bot update poll loop failed: botId={BotId}", _persistBotId);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        private void Broadcast(JsonElement update)
        {
            List<Channel<JsonElement>> targets;
            lock (_subscribersLock)
            {
                if (_subscribers.Count == 0)
                    return;
                targets = _subscribers.Values.ToList();
            }

            foreach (var ch in targets)
            {
                try
                {
                    // Clone：JsonElement 的生命周期绑定 JsonDocument
                    ch.Writer.TryWrite(update.Clone());
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Broadcast update failed: botId={BotId}", _persistBotId);
                }
            }
        }

        private static bool TryGetUpdateId(JsonElement update, out long updateId)
        {
            updateId = 0;
            if (!update.TryGetProperty("update_id", out var el))
                return false;
            if (el.ValueKind != JsonValueKind.Number)
                return false;
            return el.TryGetInt64(out updateId);
        }

        private async Task SaveLastUpdateIdAsync(long updateId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var botRepo = scope.ServiceProvider.GetRequiredService<IBotRepository>();
            var bot = await botRepo.GetByIdAsync(_persistBotId);
            if (bot == null)
                return;

            if (!bot.LastUpdateId.HasValue || updateId > bot.LastUpdateId.Value)
            {
                bot.LastUpdateId = updateId;
                await botRepo.UpdateAsync(bot);
            }
        }

        private static async Task<long> FastForwardOffsetAsync(
            int botId,
            string token,
            IServiceScopeFactory scopeFactory,
            TelegramBotApiClient botApi,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            long offset = 0;

            // 尝试最多 20 次（2000 条）以“清空积压”，避免首次启用模块直接刷历史。
            for (var i = 0; i < 20; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var updates = await botApi.CallAsync(token, "getUpdates", new Dictionary<string, string?>
                {
                    ["offset"] = offset.ToString(),
                    ["timeout"] = "0",
                    ["limit"] = "100",
                    ["allowed_updates"] = AllowedUpdatesJson
                }, cancellationToken);

                if (updates.ValueKind != JsonValueKind.Array)
                    break;

                long? maxUpdateId = null;
                foreach (var u in updates.EnumerateArray())
                {
                    if (!TryGetUpdateId(u, out var id))
                        continue;
                    maxUpdateId = maxUpdateId.HasValue ? Math.Max(maxUpdateId.Value, id) : id;
                }

                if (!maxUpdateId.HasValue)
                    break;

                offset = maxUpdateId.Value + 1;
            }

            // 写入数据库（用于下次启动直接从最新开始）
            try
            {
                using var scope = scopeFactory.CreateScope();
                var botRepo = scope.ServiceProvider.GetRequiredService<IBotRepository>();
                var bot = await botRepo.GetByIdAsync(botId);
                if (bot != null && offset > 0)
                {
                    var last = offset - 1;
                    if (!bot.LastUpdateId.HasValue || last > bot.LastUpdateId.Value)
                    {
                        bot.LastUpdateId = last;
                        await botRepo.UpdateAsync(bot);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FastForwardOffset persistence failed: botId={BotId}", botId);
            }

            return offset;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _loopTask; } catch { /* ignore */ }

            lock (_subscribersLock)
            {
                foreach (var ch in _subscribers.Values)
                {
                    try { ch.Writer.TryComplete(); } catch { /* ignore */ }
                }
                _subscribers.Clear();
            }

            _cts.Dispose();
        }
    }
}
