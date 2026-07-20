using TelegramPanel.Core.Services.Telegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AtomicSessionFileReplacementTests
{
    [Fact]
    public void 验证前失败会保留旧文件并清理候选文件()
    {
        using var scope = new TempDirectory();
        var target = scope.PathFor("100.session");
        File.WriteAllText(target, "old-session");

        string stagingPath;
        using (var replacement = AtomicSessionFileReplacement.Create(target))
        {
            stagingPath = replacement.StagingPath;
            File.WriteAllText(stagingPath, "invalid-candidate");
        }

        Assert.Equal("old-session", File.ReadAllText(target));
        Assert.False(File.Exists(stagingPath));
        Assert.Single(Directory.EnumerateFiles(scope.Path));
    }

    [Fact]
    public void 应用后验证失败会恢复旧文件并清理回滚备份()
    {
        using var scope = new TempDirectory();
        var target = scope.PathFor("200.session");
        File.WriteAllText(target, "old-session");

        string? backupPath;
        string stagingPath;
        using (var replacement = AtomicSessionFileReplacement.Create(target))
        {
            stagingPath = replacement.StagingPath;
            File.WriteAllText(stagingPath, "new-session");
            replacement.Apply();
            backupPath = replacement.BackupPath;

            Assert.Equal("new-session", File.ReadAllText(target));
            Assert.True(File.Exists(backupPath));
        }

        Assert.Equal("old-session", File.ReadAllText(target));
        Assert.False(File.Exists(stagingPath));
        Assert.False(File.Exists(backupPath));
        Assert.Single(Directory.EnumerateFiles(scope.Path));
    }

    [Fact]
    public void 提交替换会保留新文件并清理回滚备份()
    {
        using var scope = new TempDirectory();
        var target = scope.PathFor("300.session");
        File.WriteAllText(target, "old-session");

        string? backupPath;
        string stagingPath;
        using (var replacement = AtomicSessionFileReplacement.Create(target))
        {
            stagingPath = replacement.StagingPath;
            File.WriteAllText(stagingPath, "new-session");
            replacement.Apply();
            backupPath = replacement.BackupPath;
            replacement.Commit();
        }

        Assert.Equal("new-session", File.ReadAllText(target));
        Assert.False(File.Exists(stagingPath));
        Assert.False(File.Exists(backupPath));
        Assert.Single(Directory.EnumerateFiles(scope.Path));
    }

    [Fact]
    public void 未提交的Guid临时Session会在失败后删除()
    {
        using var scope = new TempDirectory();
        var target = scope.PathFor($"{Guid.NewGuid():N}.session");

        string stagingPath;
        using (var replacement = AtomicSessionFileReplacement.Create(target))
        {
            stagingPath = replacement.StagingPath;
            File.WriteAllText(stagingPath, "temporary-session");
            replacement.Apply();
            Assert.True(File.Exists(target));
        }

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(stagingPath));
        Assert.Empty(Directory.EnumerateFiles(scope.Path));
    }

    [Fact]
    public void 新Session提交后不会遗留候选或备份文件()
    {
        using var scope = new TempDirectory();
        var target = scope.PathFor("400.session");

        string stagingPath;
        using (var replacement = AtomicSessionFileReplacement.Create(target))
        {
            stagingPath = replacement.StagingPath;
            File.WriteAllText(stagingPath, "new-session");
            replacement.Apply();
            replacement.Commit();
        }

        Assert.Equal("new-session", File.ReadAllText(target));
        Assert.False(File.Exists(stagingPath));
        Assert.Single(Directory.EnumerateFiles(scope.Path));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "telegram-panel-session-file-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathFor(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
