using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class GlobalProxyEgressTests
{
    [Fact]
    public async Task 全局代理账号检测使用Telegram实际代理而不是面板直连()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Accounts.Add(new Account
        {
            Phone = "8613800000099",
            UserId = 90099,
            SessionPath = "sessions/global-proxy.session",
            ApiId = 1,
            ApiHash = "hash",
            UseGlobalProxy = true
        });
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:Proxy:Server"] = "mtproxy.example.com",
                ["Telegram:Proxy:Port"] = "443",
                ["Telegram:Proxy:Secret"] = "ee0123456789abcdef0123456789abcdef"
            })
            .Build();
        var probe = new ProxyEgressProbeService();
        var warp = new WarpContainerManager(
            db,
            configuration,
            probe,
            NullLogger<WarpContainerManager>.Instance);
        var service = new ProxyManagementService(
            db,
            new EmptyClientPool(),
            probe,
            warp,
            NullLogger<ProxyManagementService>.Instance,
            configuration);

        var result = await service.ProbeAccountAsync(1);

        Assert.False(result.Success);
        Assert.Contains("MTProxy", result.Error);
    }

    private sealed class EmptyClientPool : ITelegramClientPool
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
}
