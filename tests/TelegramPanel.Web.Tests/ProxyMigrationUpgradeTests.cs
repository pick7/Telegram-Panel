using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramPanel.Data;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ProxyMigrationUpgradeTests
{
    [Fact]
    public async Task 从上一版升级代理迁移时保留账号数据()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260316000000_AddAccountRemark");
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Accounts"
                ("Phone", "UserId", "SessionPath", "ApiId", "ApiHash", "IsActive", "CreatedAt", "LastSyncAt")
            VALUES
                ('8613800000000', 10001, 'sessions/upgrade.session', 1, 'hash', 1,
                 '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var account = await db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal("8613800000000", account.Phone);
        Assert.Null(account.ProxyId);
        Assert.True(account.UseGlobalProxy);
        Assert.Empty(await db.OutboundProxies.AsNoTracking().ToListAsync());
    }
}
