using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TelegramPanel.Web.Tests")]

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// 在目标文件所在目录中暂存新 Session，并以可回滚方式替换最终文件。
/// </summary>
internal sealed class AtomicSessionFileReplacement : IDisposable
{
    private bool _applied;
    private bool _committed;
    private bool _disposed;
    private bool _targetExisted;

    private AtomicSessionFileReplacement(string targetPath)
    {
        TargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(TargetPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        StagingPath = BuildAdjacentPath(TargetPath, "staging");
    }

    public string TargetPath { get; }

    public string StagingPath { get; }

    public string? BackupPath { get; private set; }

    public Exception? CleanupError { get; private set; }

    public static AtomicSessionFileReplacement Create(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Session 目标路径不能为空", nameof(targetPath));

        return new AtomicSessionFileReplacement(targetPath);
    }

    /// <summary>
    /// 原子应用候选文件。存在旧目标时，旧内容会保留在同目录唯一备份中。
    /// </summary>
    public void Apply()
    {
        ThrowIfDisposed();
        if (_applied)
            throw new InvalidOperationException("Session 候选文件已经应用");
        if (!File.Exists(StagingPath))
            throw new FileNotFoundException("Session 候选文件不存在", StagingPath);

        _targetExisted = File.Exists(TargetPath);
        if (!_targetExisted)
        {
            File.Move(StagingPath, TargetPath);
            _applied = true;
            return;
        }

        BackupPath = BuildAdjacentPath(TargetPath, "rollback");
        try
        {
            File.Replace(StagingPath, TargetPath, BackupPath, ignoreMetadataErrors: true);
            _applied = true;
        }
        catch (Exception replaceError)
        {
            try
            {
                RestoreBackupIfPresent();
            }
            catch (Exception restoreError)
            {
                throw new IOException(
                    "Session 原子替换失败，且旧文件自动恢复失败；回滚备份已保留",
                    new AggregateException(replaceError, restoreError));
            }

            throw;
        }
    }

    /// <summary>
    /// 确认替换成功并清理旧文件备份。
    /// </summary>
    public void Commit()
    {
        ThrowIfDisposed();
        if (!_applied)
            throw new InvalidOperationException("Session 候选文件尚未应用");
        if (_committed)
            return;

        // 正式文件已经替换成功，先确认提交，再做旧备份清理。
        // 即使备份暂时被占用，也不能因为清理失败而把已入库账号回滚到旧 Session。
        _committed = true;
        if (string.IsNullOrWhiteSpace(BackupPath) || !File.Exists(BackupPath))
        {
            BackupPath = null;
            return;
        }

        try
        {
            File.Delete(BackupPath);
            BackupPath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CleanupError = ex;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_applied && !_committed)
            Rollback();

        if (File.Exists(StagingPath))
            File.Delete(StagingPath);

        _disposed = true;
    }

    private void Rollback()
    {
        if (_targetExisted)
        {
            if (string.IsNullOrWhiteSpace(BackupPath) || !File.Exists(BackupPath))
                throw new IOException("Session 替换未提交，但旧文件回滚备份不存在");

            RestoreBackupIfPresent();
        }
        else if (File.Exists(TargetPath))
        {
            File.Delete(TargetPath);
        }

        _applied = false;
    }

    private void RestoreBackupIfPresent()
    {
        if (string.IsNullOrWhiteSpace(BackupPath) || !File.Exists(BackupPath))
            return;

        if (File.Exists(TargetPath))
            File.Replace(BackupPath, TargetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(BackupPath, TargetPath);

        BackupPath = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string BuildAdjacentPath(string targetPath, string marker)
    {
        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        return Path.Combine(directory, $".{name}.{marker}-{Guid.NewGuid():N}{extension}");
    }
}
