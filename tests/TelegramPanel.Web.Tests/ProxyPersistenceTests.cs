using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ProxyPersistenceTests
{
    [Fact]
    public async Task 已绑定账号的代理不能被数据库静默删除()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        int proxyId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var proxy = NewProxy("bound");
            db.OutboundProxies.Add(proxy);
            db.Accounts.Add(new Account
            {
                Phone = "8613800000000",
                UserId = 42,
                SessionPath = "sessions/test.session",
                ApiId = 1,
                ApiHash = "hash",
                Proxy = proxy
            });
            await db.SaveChangesAsync();
            proxyId = proxy.Id;
        }

        await using (var db = new AppDbContext(options))
        {
            var proxy = await db.OutboundProxies.SingleAsync(x => x.Id == proxyId);
            db.OutboundProxies.Remove(proxy);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task 删除未绑定代理时WARP历史记录保留并清空外键()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        int proxyId;
        int profileId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var proxy = NewProxy("warp");
            proxy.Kind = "warp";
            var profile = new WarpProfile
            {
                ProfileId = "profile-1",
                ContainerName = "warp-1",
                VolumeName = "warp-data-1",
                HostPort = 42080,
                Status = "active",
                Proxy = proxy
            };
            db.WarpProfiles.Add(profile);
            await db.SaveChangesAsync();
            proxyId = proxy.Id;
            profileId = profile.Id;
        }

        await using (var db = new AppDbContext(options))
        {
            var proxy = await db.OutboundProxies.SingleAsync(x => x.Id == proxyId);
            db.OutboundProxies.Remove(proxy);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var profile = await db.WarpProfiles.AsNoTracking().SingleAsync(x => x.Id == profileId);
            Assert.Null(profile.OutboundProxyId);
        }
    }

    private static OutboundProxy NewProxy(string suffix) => new()
    {
        Name = $"proxy-{suffix}",
        Kind = "manual",
        Protocol = "http",
        Host = "127.0.0.1",
        Port = 8080,
        IsEnabled = true,
        TestStatus = "unknown"
    };
}
