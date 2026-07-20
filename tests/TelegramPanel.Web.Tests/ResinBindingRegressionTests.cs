using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

public sealed class ResinBindingRegressionTests
{
    [Fact]
    public async Task 重复绑定同一Resin代理不会释放当前粘性Lease()
    {
        await using var resin = new ResinControlPlaneStub();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = new OutboundProxy
        {
            Name = "resin-sticky",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8080,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        var account = new Account
        {
            Phone = "8613800000100",
            UserId = 10001,
            SessionPath = "sessions/resin-sticky.session",
            ApiId = 1,
            ApiHash = "hash",
            Proxy = proxy
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var service = CreateProxyService(db);
        var result = await service.BindAccountsAsync(
            new[] { account.Id },
            new AccountProxyBindingInput("existing", proxy.Id));

        Assert.Equal(1, result.Success);
        Assert.Equal(proxy.Id, result.Items.Single().ProxyId);
        Assert.Equal(proxy.Id, await db.Accounts
            .Where(x => x.Id == account.Id)
            .Select(x => x.ProxyId)
            .SingleAsync());
        Assert.Empty(resin.Requests);
    }

    [Fact]
    public async Task Resin释放失败时代理切换保留原绑定且允许重试()
    {
        await using var resin = new ResinControlPlaneStub(HttpStatusCode.InternalServerError);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var resinProxy = new OutboundProxy
        {
            Name = "resin-retry",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8080,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        var targetProxy = new OutboundProxy
        {
            Name = "manual-target",
            Kind = OutboundProxyKinds.Manual,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8081,
            IsEnabled = true,
            TestStatus = "unknown"
        };
        var account = new Account
        {
            Phone = "8613800000101",
            UserId = 10002,
            SessionPath = "sessions/resin-retry.session",
            ApiId = 1,
            ApiHash = "hash",
            Proxy = resinProxy,
            UseGlobalProxy = true
        };
        db.AddRange(account, targetProxy);
        await db.SaveChangesAsync();

        var service = CreateProxyService(db);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BindAccountsAsync(
                new[] { account.Id },
                new AccountProxyBindingInput("existing", targetProxy.Id)));

        Assert.Contains("Resin Lease", error.Message);
        var unchanged = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(resinProxy.Id, unchanged.ProxyId);
        Assert.True(unchanged.UseGlobalProxy);

        resin.LeaseDeleteStatus = HttpStatusCode.OK;
        var result = await service.BindAccountsAsync(
            new[] { account.Id },
            new AccountProxyBindingInput("existing", targetProxy.Id));

        Assert.Equal(1, result.Success);
        var switched = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(targetProxy.Id, switched.ProxyId);
        Assert.False(switched.UseGlobalProxy);
        Assert.Equal(2, resin.Requests.Count(x =>
            x.StartsWith("DELETE /api/v1/platforms/default-id/leases/tg_account_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Resin释放失败时删除账号会保留账号和代理绑定()
    {
        await using var resin = new ResinControlPlaneStub(HttpStatusCode.ServiceUnavailable);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = new OutboundProxy
        {
            Name = "resin-delete-failure",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8080,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        var account = new Account
        {
            Phone = "8613800000102",
            UserId = 10003,
            SessionPath = "sessions/resin-delete-failure.session",
            ApiId = 1,
            ApiHash = "hash",
            Proxy = proxy,
            UseGlobalProxy = true
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var pool = new NoopClientPool();
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

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accountService.DeleteAccountAsync(account.Id));

        Assert.Contains("Resin Lease", error.Message);
        var preserved = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(proxy.Id, preserved.ProxyId);
        Assert.True(preserved.UseGlobalProxy);
        Assert.True(await db.OutboundProxies.AnyAsync(x => x.Id == proxy.Id));
    }

    [Fact]
    public async Task Resin释放失败时废号清理不会先删除Session文件()
    {
        await using var resin = new ResinControlPlaneStub(HttpStatusCode.ServiceUnavailable);
        var sessionPath = Path.Combine(Path.GetTempPath(), $"resin-purge-{Guid.NewGuid():N}.session");
        await File.WriteAllTextAsync(sessionPath, "session-data");
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var proxy = new OutboundProxy
            {
                Name = "resin-purge-failure",
                Kind = OutboundProxyKinds.Resin,
                Protocol = OutboundProxyProtocols.Http,
                Host = "127.0.0.1",
                Port = 8080,
                ResinPlatform = "Default",
                ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
                ResinAdminToken = "admin-token",
                IsEnabled = true,
                TestStatus = "unknown"
            };
            var account = new Account
            {
                Phone = "8613800000103",
                UserId = 10004,
                SessionPath = sessionPath,
                ApiId = 1,
                ApiHash = "hash",
                Proxy = proxy
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            var pool = new NoopClientPool();
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

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                accountService.PurgeAccountAsync(account.Id));

            Assert.True(File.Exists(sessionPath));
            Assert.Equal(proxy.Id, (await db.Accounts.AsNoTracking().SingleAsync()).ProxyId);
        }
        finally
        {
            if (File.Exists(sessionPath))
                File.Delete(sessionPath);
        }
    }

    [Fact]
    public async Task Resin平台已删除时释放Lease按幂等成功处理()
    {
        await using var resin = new ResinControlPlaneStub { IncludePlatform = false };
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "resin-missing-platform",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8080,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        var account = new Account
        {
            Phone = "8613800000104",
            UserId = 10005,
            SessionPath = "sessions/resin-missing-platform.session",
            ApiId = 1,
            ApiHash = "hash",
            Proxy = proxy
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var result = await CreateProxyService(db).BindAccountsAsync(
            new[] { account.Id },
            new AccountProxyBindingInput("direct"));

        Assert.Equal(1, result.Success);
        Assert.Null((await db.Accounts.AsNoTracking().SingleAsync()).ProxyId);
        Assert.DoesNotContain(resin.Requests, x => x.StartsWith("DELETE ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 导入阶段的Resin临时粘性Lease会按稳定身份回收()
    {
        await using var resin = new ResinControlPlaneStub();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "resin-import",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8080,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        db.OutboundProxies.Add(proxy);
        await db.SaveChangesAsync();

        await CreateProxyService(db).ReleaseImportResinLeaseBestEffortAsync(
            proxy.Id,
            "tg_import_0123456789abcdef");

        Assert.Contains(
            "DELETE /api/v1/platforms/default-id/leases/tg_import_0123456789abcdef HTTP/1.1",
            resin.Requests);
    }

    [Fact]
    public async Task Resin代理出口检测后会回收探测Lease()
    {
        await using var resin = new ResinControlPlaneStub();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "resin-probe",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 1,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        db.OutboundProxies.Add(proxy);
        await db.SaveChangesAsync();

        await CreateProxyService(db).TestAsync(proxy.Id);

        Assert.Contains(
            $"DELETE /api/v1/platforms/default-id/leases/telegram_panel_probe_{proxy.Id} HTTP/1.1",
            resin.Requests);
    }

    [Fact]
    public async Task 编辑Resin身份时释放失败会保留旧控制面凭据()
    {
        await using var resin = new ResinControlPlaneStub(HttpStatusCode.ServiceUnavailable);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "resin-edit-failure",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 8080,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "old-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        var account = new Account
        {
            Phone = "8613800000199",
            UserId = 10199,
            SessionPath = "sessions/resin-edit-failure.session",
            ApiId = 1,
            ApiHash = "hash",
            Proxy = proxy
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var service = CreateProxyService(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                proxy.Id,
                new OutboundProxyInput(
                    proxy.Name,
                    OutboundProxyKinds.Resin,
                    OutboundProxyProtocols.Http,
                    proxy.Host,
                    proxy.Port,
                    proxy.Username,
                    Password: null,
                    Secret: null,
                    ResinPlatform: "NewPlatform",
                    ResinAdminUrl: "http://127.0.0.1:65534/",
                    ResinAdminToken: "new-token",
                    IsEnabled: true,
                    TestAfterSave: false)));

        Assert.Contains("Resin Lease", error.Message);
        var preserved = await db.OutboundProxies.AsNoTracking().SingleAsync();
        Assert.Equal("Default", preserved.ResinPlatform);
        Assert.Equal(resin.BaseAddress.AbsoluteUri, preserved.ResinAdminUrl);
        Assert.Equal("old-token", preserved.ResinAdminToken);
        Assert.Equal(proxy.Id, (await db.Accounts.AsNoTracking().SingleAsync()).ProxyId);
    }

    [Fact]
    public async Task Resin平台查询响应超过限制时会安全失败()
    {
        await using var resin = new ResinControlPlaneStub
        {
            PlatformContentLengthOverride = 1024 * 1024 + 1
        };
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "resin-oversized-control-response",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "127.0.0.1",
            Port = 1,
            ResinPlatform = "Default",
            ResinAdminUrl = resin.BaseAddress.AbsoluteUri,
            ResinAdminToken = "admin-token",
            IsEnabled = true,
            TestStatus = "unknown"
        };
        db.OutboundProxies.Add(proxy);
        await db.SaveChangesAsync();

        await CreateProxyService(db).TestAsync(proxy.Id);

        Assert.Equal("fail", proxy.TestStatus);
        Assert.Contains("超过 1024KB 限制", proxy.LastError);
    }

    private static ProxyManagementService CreateProxyService(
        AppDbContext db,
        ITelegramClientPool? pool = null)
    {
        pool ??= new NoopClientPool();
        var probe = new ProxyEgressProbeService();
        var warp = new WarpContainerManager(
            db,
            new ConfigurationBuilder().Build(),
            probe,
            NullLogger<WarpContainerManager>.Instance);
        return new ProxyManagementService(
            db,
            pool,
            probe,
            warp,
            NullLogger<ProxyManagementService>.Instance);
    }

    private sealed class NoopClientPool : ITelegramClientPool
    {
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

        public Task RemoveClientAsync(int accountId) => Task.CompletedTask;

        public Task RemoveAllClientsAsync() => Task.CompletedTask;

        public bool IsClientConnected(int accountId) => false;
    }

    private sealed class ResinControlPlaneStub : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly Task _serveTask;

        public ResinControlPlaneStub(
            HttpStatusCode leaseDeleteStatus = HttpStatusCode.OK)
        {
            LeaseDeleteStatus = leaseDeleteStatus;
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseAddress = new Uri($"http://127.0.0.1:{port}/");
            _serveTask = ServeAsync();
        }

        public Uri BaseAddress { get; }

        public HttpStatusCode LeaseDeleteStatus { get; set; }

        public bool IncludePlatform { get; set; } = true;

        public long? PlatformContentLengthOverride { get; set; }

        public IReadOnlyCollection<string> Requests => _requests.ToArray();

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _serveTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await HandleAsync(client, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_stop.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            _requests.Enqueue(requestLine);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
            {
            }

            var body = requestLine.StartsWith("GET /api/v1/platforms", StringComparison.Ordinal)
                ? IncludePlatform
                    ? "{\"items\":[{\"id\":\"default-id\",\"name\":\"Default\"}]}"
                    : "{\"items\":[]}"
                : "{}";
            var status = requestLine.StartsWith("DELETE /api/v1/platforms/", StringComparison.Ordinal)
                ? LeaseDeleteStatus
                : HttpStatusCode.OK;
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var contentLength = requestLine.StartsWith("GET /api/v1/platforms", StringComparison.Ordinal)
                ? PlatformContentLengthOverride ?? bodyBytes.Length
                : bodyBytes.Length;
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: application/json\r\nContent-Length: {contentLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }
    }
}
