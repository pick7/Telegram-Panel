using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

/// <summary>
/// 代理生命周期回归测试，覆盖账号删除、凭据清理和客户端池竞态。
/// </summary>
public sealed class ProxyLifecycleRegressionTests
{
    [Fact]
    public async Task 直连与全局代理设置会持久化为不同路由()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var account = NewAccount(NewProxy("routing-mode"));
        account.Proxy = null;
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var service = CreateProxyService(db, new RecordingClientPool());

        await service.BindAccountsAsync(
            new[] { account.Id },
            new AccountProxyBindingInput("direct"));
        Assert.False((await db.Accounts.AsNoTracking().SingleAsync()).UseGlobalProxy);

        await service.BindAccountsAsync(
            new[] { account.Id },
            new AccountProxyBindingInput("global"));
        Assert.True((await db.Accounts.AsNoTracking().SingleAsync()).UseGlobalProxy);
    }

    [Fact]
    public async Task 显式直连不会回退全局代理且停用代理会阻止降级()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Accounts.Add(new Account
            {
                Phone = "8613800000999",
                UserId = 999,
                SessionPath = "sessions/direct.session",
                ApiId = 1,
                ApiHash = "hash",
                UseGlobalProxy = false
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(builder => builder.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        var resolver = new AccountProxyResolver(provider.GetRequiredService<IServiceScopeFactory>());
        var direct = await resolver.ResolveAsync(1);

        Assert.Null(direct.Proxy);
        Assert.False(direct.UseGlobalProxy);

        await using (var db = new AppDbContext(options))
        {
            var account = await db.Accounts.SingleAsync();
            var disabledProxy = NewProxy("disabled");
            disabledProxy.IsEnabled = false;
            account.Proxy = disabledProxy;
            account.UseGlobalProxy = false;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(1));
    }

    [Fact]
    public async Task 删除账号会先解绑代理并释放客户端()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = NewProxy("account-delete");
        var account = NewAccount(proxy);
        db.OutboundProxies.Add(proxy);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var pool = new RecordingClientPool();
        var proxyService = CreateProxyService(db, pool);
        var accountService = new AccountManagementService(
            new AccountRepository(db),
            new ChannelRepository(db),
            new GroupRepository(db),
            pool,
            new ConfigurationBuilder().Build(),
            NullLogger<AccountManagementService>.Instance,
            proxyService,
            new SessionPathResolver(new ConfigurationBuilder().Build()));

        await accountService.DeleteAccountAsync(account.Id);

        Assert.Empty(await db.Accounts.ToListAsync());
        Assert.Single(await db.OutboundProxies.ToListAsync());
        Assert.Equal(new[] { account.Id }, pool.RemovedAccountIds);
    }

    [Fact]
    public async Task WARP清理失败时账号删除会恢复绑定并抛错()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = NewWarpProxy("account-delete-failure", 42111);
        var account = NewAccount(proxy);
        account.UseGlobalProxy = true;
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var pool = new RecordingClientPool();
        var proxyService = CreateProxyService(db, pool, MissingDockerConfiguration());
        var accountService = new AccountManagementService(
            new AccountRepository(db),
            new ChannelRepository(db),
            new GroupRepository(db),
            pool,
            new ConfigurationBuilder().Build(),
            NullLogger<AccountManagementService>.Instance,
            proxyService,
            new SessionPathResolver(new ConfigurationBuilder().Build()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accountService.DeleteAccountAsync(account.Id));

        Assert.Contains("WARP 资源清理失败", error.Message);
        var preserved = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(proxy.Id, preserved.ProxyId);
        Assert.True(preserved.UseGlobalProxy);
        Assert.True(await db.OutboundProxies.AnyAsync(x => x.Id == proxy.Id));
        Assert.False((await db.OutboundProxies.AsNoTracking().SingleAsync()).IsEnabled);
        var profile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(proxy.WarpProfile!.ContainerName, profile.ContainerName);
        Assert.Equal(proxy.WarpProfile.VolumeName, profile.VolumeName);
    }

    [Fact]
    public async Task WARP清理失败时代理切换会恢复原绑定和路由模式()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var warpProxy = NewWarpProxy("switch-failure", 42112);
        var targetProxy = NewProxy("switch-target");
        targetProxy.Port = 8082;
        var account = NewAccount(warpProxy);
        account.UseGlobalProxy = true;
        db.AddRange(account, targetProxy);
        await db.SaveChangesAsync();

        var service = CreateProxyService(
            db,
            new RecordingClientPool(),
            MissingDockerConfiguration());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BindAccountsAsync(
                new[] { account.Id },
                new AccountProxyBindingInput("existing", targetProxy.Id)));

        Assert.Contains("WARP 资源清理失败", error.Message);
        var restored = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(warpProxy.Id, restored.ProxyId);
        Assert.True(restored.UseGlobalProxy);
        Assert.True(await db.OutboundProxies.AnyAsync(x => x.Id == warpProxy.Id));
        Assert.False((await db.OutboundProxies.AsNoTracking()
            .SingleAsync(x => x.Id == warpProxy.Id)).IsEnabled);
        Assert.True(await db.WarpProfiles.AnyAsync(x => x.OutboundProxyId == warpProxy.Id));
    }

    [Fact]
    public async Task 代理切换到非Resin和非MTProto会清除旧凭据()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = NewProxy("credential-clear");
        proxy.Kind = OutboundProxyKinds.Resin;
        proxy.ResinAdminToken = "resin-token";
        proxy.Secret = "stale-secret";
        proxy.TestStatus = "ok";
        proxy.EgressIp = "203.0.113.10";
        proxy.EgressCountry = "US";
        proxy.LastLatencyMs = 42;
        proxy.LastTestedAtUtc = DateTime.UtcNow;
        db.OutboundProxies.Add(proxy);
        await db.SaveChangesAsync();

        var service = CreateProxyService(db, new RecordingClientPool());
        await service.UpdateAsync(
            proxy.Id,
            NewInput(
                name: proxy.Name,
                kind: OutboundProxyKinds.Manual,
                protocol: OutboundProxyProtocols.Http,
                host: proxy.Host,
                port: proxy.Port));

        var updated = await db.OutboundProxies.AsNoTracking().SingleAsync();
        Assert.Null(updated.ResinAdminToken);
        Assert.Null(updated.Secret);
        Assert.Equal("unknown", updated.TestStatus);
        Assert.Null(updated.EgressIp);
        Assert.Null(updated.EgressCountry);
        Assert.Null(updated.LastLatencyMs);
        Assert.Null(updated.LastTestedAtUtc);
    }

    [Fact]
    public async Task 停用的账号代理出口检测不会降级为面板直连()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = NewProxy("disabled-egress");
        proxy.IsEnabled = false;
        var account = NewAccount(proxy);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var service = CreateProxyService(db, new RecordingClientPool());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProbeAccountAsync(account.Id));

        Assert.Contains("已停用", error.Message);
    }

    [Fact]
    public async Task 继承全局代理的账号出口检测使用全局MTProxy配置()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var account = NewAccount(NewProxy("global-egress"));
        account.Proxy = null;
        account.UseGlobalProxy = true;
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:Proxy:Server"] = "127.0.0.1",
                ["Telegram:Proxy:Port"] = "443",
                ["Telegram:Proxy:Secret"] = "abcdef"
            })
            .Build();
        var service = CreateProxyService(db, new RecordingClientPool(), configuration);

        var result = await service.ProbeAccountAsync(account.Id);

        Assert.False(result.Success);
        Assert.Contains("MTProxy", result.Error);
    }

    [Fact]
    public async Task 留空时保留适用凭据但切出协议后清除Secret()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = NewProxy("credential-preserve");
        proxy.Protocol = OutboundProxyProtocols.MtProto;
        proxy.Secret = "mt-secret";
        db.OutboundProxies.Add(proxy);
        await db.SaveChangesAsync();

        var service = CreateProxyService(db, new RecordingClientPool());
        await service.UpdateAsync(
            proxy.Id,
            NewInput(
                name: proxy.Name,
                kind: OutboundProxyKinds.Manual,
                protocol: OutboundProxyProtocols.MtProto,
                host: proxy.Host,
                port: proxy.Port,
                secret: string.Empty));
        Assert.Equal("mt-secret", (await db.OutboundProxies.AsNoTracking().SingleAsync()).Secret);

        await service.UpdateAsync(
            proxy.Id,
            NewInput(
                name: proxy.Name,
                kind: OutboundProxyKinds.Manual,
                protocol: OutboundProxyProtocols.Http,
                host: proxy.Host,
                port: proxy.Port,
                secret: string.Empty));
        Assert.Null((await db.OutboundProxies.AsNoTracking().SingleAsync()).Secret);
    }

    [Fact]
    public async Task WARP停用会释放所有绑定账号客户端并同步DesiredEnabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = NewProxy("warp-disable");
        proxy.Kind = OutboundProxyKinds.Warp;
        var profile = new WarpProfile
        {
            ProfileId = "profile-warp-disable",
            ContainerName = "warp-disable",
            VolumeName = "warp-disable-data",
            HostPort = 42101,
            Status = "active",
            DesiredEnabled = true,
            Proxy = proxy
        };
        var account = NewAccount(proxy);
        db.WarpProfiles.Add(profile);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var pool = new RecordingClientPool();
        var service = CreateProxyService(db, pool);
        await service.UpdateAsync(
            proxy.Id,
            NewInput(
                name: proxy.Name,
                kind: OutboundProxyKinds.Warp,
                protocol: OutboundProxyProtocols.Http,
                host: proxy.Host,
                port: proxy.Port,
                isEnabled: false));

        Assert.Contains(account.Id, pool.RemovedAccountIds);
        Assert.False((await db.WarpProfiles.AsNoTracking().SingleAsync()).DesiredEnabled);
        Assert.False((await db.OutboundProxies.AsNoTracking().SingleAsync()).IsEnabled);
    }

    [Fact]
    public async Task 客户端创建期间移除会等待账号锁并清理新客户端()
    {
        var resolver = new BlockingProxyResolver();
        var pool = new TelegramClientPool(
            new ConfigurationBuilder().Build(),
            NullLogger<TelegramClientPool>.Instance,
            new TelegramAccountUpdateHub(),
            resolver,
            new SessionPathResolver(new ConfigurationBuilder().Build()));
        var sessionPath = Path.Combine(
            Path.GetTempPath(),
            $"telegram-panel-pool-{Guid.NewGuid():N}.session");

        try
        {
            var createTask = pool.GetOrCreateClientAsync(
                9001,
                12345,
                "0123456789abcdef0123456789abcdef",
                sessionPath,
                sessionKey: "0123456789abcdef0123456789abcdef",
                phoneNumber: "8613800000000");
            await resolver.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var removeTask = pool.RemoveClientAsync(9001);
            Assert.False(removeTask.IsCompleted);
            resolver.Release();

            try
            {
                await createTask;
            }
            catch
            {
                // 不同 WTelegram 版本可能在无有效 session 时拒绝构造，
                // 这里只验证移除不会与创建并发交叉。
            }
            await removeTask;
            Assert.Null(pool.GetClient(9001));
        }
        finally
        {
            pool.Dispose();
            TryDelete(sessionPath);
        }
    }

    [Fact]
    public async Task 客户端创建期间关闭池不会把新客户端写回()
    {
        var resolver = new BlockingProxyResolver();
        var pool = new TelegramClientPool(
            new ConfigurationBuilder().Build(),
            NullLogger<TelegramClientPool>.Instance,
            new TelegramAccountUpdateHub(),
            resolver,
            new SessionPathResolver(new ConfigurationBuilder().Build()));
        var sessionPath = Path.Combine(
            Path.GetTempPath(),
            $"telegram-panel-dispose-{Guid.NewGuid():N}.session");

        try
        {
            var createTask = pool.GetOrCreateClientAsync(
                9002,
                12345,
                "0123456789abcdef0123456789abcdef",
                sessionPath,
                sessionKey: "0123456789abcdef0123456789abcdef",
                phoneNumber: "8613800000001");
            await resolver.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            pool.Dispose();
            resolver.Release();
            try
            {
                await createTask;
            }
            catch
            {
                // 关闭期间创建必须失败，具体异常取决于 WTelegram 构造阶段。
            }

            Assert.Null(pool.GetClient(9002));
        }
        finally
        {
            pool.Dispose();
            TryDelete(sessionPath);
        }
    }

    private static ProxyManagementService CreateProxyService(
        AppDbContext db,
        RecordingClientPool pool,
        IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        var probe = new ProxyEgressProbeService();
        var warp = new WarpContainerManager(
            db,
            configuration,
            probe,
            NullLogger<WarpContainerManager>.Instance);
        return new ProxyManagementService(
            db,
            pool,
            probe,
            warp,
            NullLogger<ProxyManagementService>.Instance,
            configuration);
    }

    private static IConfiguration MissingDockerConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:Warp:DockerSocketPath"] = Path.Combine(
                    Path.GetTempPath(),
                    $"telegram-panel-missing-docker-{Guid.NewGuid():N}.sock")
            })
            .Build();

    private static OutboundProxy NewProxy(string suffix) => new()
    {
        Name = $"proxy-{suffix}",
        Kind = OutboundProxyKinds.Manual,
        Protocol = OutboundProxyProtocols.Http,
        Host = "127.0.0.1",
        Port = 8080,
        IsEnabled = true,
        TestStatus = "unknown"
    };

    private static OutboundProxy NewWarpProxy(string suffix, int hostPort)
    {
        var proxy = NewProxy(suffix);
        proxy.Kind = OutboundProxyKinds.Warp;
        proxy.WarpProfile = new WarpProfile
        {
            ProfileId = $"profile-{suffix}",
            ContainerId = $"container-id-{suffix}",
            ContainerName = $"container-{suffix}",
            VolumeName = $"volume-{suffix}",
            HostPort = hostPort,
            Status = "active",
            DesiredEnabled = true,
            Proxy = proxy
        };
        return proxy;
    }

    private static Account NewAccount(OutboundProxy proxy) => new()
    {
        Phone = "8613800000000" + Random.Shared.Next(100, 999),
        UserId = Random.Shared.NextInt64(1000, 9999),
        SessionPath = "sessions/lifecycle.session",
        ApiId = 1,
        ApiHash = "hash",
        Proxy = proxy
    };

    private static OutboundProxyInput NewInput(
        string name,
        string kind,
        string protocol,
        string host,
        int port,
        string? secret = null,
        bool isEnabled = true) =>
        new(
            name,
            kind,
            protocol,
            host,
            port,
            Username: null,
            Password: null,
            Secret: secret,
            ResinPlatform: null,
            ResinAdminUrl: null,
            ResinAdminToken: null,
            IsEnabled: isEnabled,
            TestAfterSave: false);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 测试清理失败不影响断言结果。
        }
    }

    private sealed class RecordingClientPool : ITelegramClientPool
    {
        private readonly List<int> _removed = new();

        public IReadOnlyList<int> RemovedAccountIds => _removed;
        public int ActiveClientCount => 0;

        public Task<Client> GetOrCreateClientAsync(
            int accountId,
            int apiId,
            string apiHash,
            string sessionPath,
            string? sessionKey = null,
            string? phoneNumber = null,
            long? userId = null) =>
            throw new NotSupportedException();

        public Client? GetClient(int accountId) => null;

        public Task RemoveClientAsync(int accountId)
        {
            lock (_removed)
                _removed.Add(accountId);
            return Task.CompletedTask;
        }

        public Task RemoveAllClientsAsync() => Task.CompletedTask;

        public bool IsClientConnected(int accountId) => false;
    }

    private sealed class BlockingProxyResolver : IAccountProxyResolver
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AccountProxyResolution> ResolveAsync(
            int accountId,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();

        private async Task<AccountProxyResolution> WaitAsync(CancellationToken cancellationToken)
        {
            await _release.Task.WaitAsync(cancellationToken);
            return new AccountProxyResolution(null, true);
        }
    }
}
