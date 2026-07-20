using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramPanel.Data;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ScheduledTaskNameMigrationTests
{
    [Fact]
    public async Task 从上一版升级时为旧计划任务回填名称并保留配置()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        await migrator.MigrateAsync("20260720220320_AddAccountProxyRoutingMode");
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ScheduledTasks"
                ("TaskType", "Status", "Total", "ConfigJson", "CronExpression",
                 "CreatedAt", "UpdatedAt")
            VALUES
                ('account_auto_sync', 'enabled', 25, '{{"batchSize":25}}', '0 9 * * *',
                 '2026-07-20 00:00:00', '2026-07-20 00:00:00');
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var task = await db.ScheduledTasks.AsNoTracking().SingleAsync();
        Assert.Equal("account_auto_sync", task.Name);
        Assert.Equal("account_auto_sync", task.TaskType);
        Assert.Equal("0 9 * * *", task.CronExpression);
        Assert.Equal("{\"batchSize\":25}", task.ConfigJson);
    }
}
