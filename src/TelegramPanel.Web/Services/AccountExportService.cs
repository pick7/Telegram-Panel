using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Web.Services;

public class AccountExportService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountExportService> _logger;
    private readonly ITelegramClientPool _clientPool;

    public AccountExportService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<AccountExportService> logger,
        ITelegramClientPool clientPool)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
        _clientPool = clientPool;
    }

    public async Task<byte[]> BuildAccountsZipAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var readme = zip.CreateEntry("README.txt", CompressionLevel.Fastest);
            await using (var readmeStream = readme.Open())
            await using (var writer = new StreamWriter(readmeStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteLineAsync("Telegram Panel 账号导出包");
                await writer.WriteLineAsync("结构：每个账号一个子文件夹，包含 .json + .session；如保存了二级密码，则额外包含 2fa.txt。");
                await writer.WriteLineAsync("导入：面板 -> 账号 -> 导入账号 -> 压缩包导入（Zip）。");
                await writer.WriteLineAsync($"导出时间(UTC)：{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            }

            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var phone = (account.Phone ?? string.Empty).Trim();
                var safeFolder = BuildSafeFolderName(phone, account.Id);

                // 文件夹占位（部分 zip 工具更友好）
                _ = zip.CreateEntry($"{safeFolder}/");

                try
                {
                    var sessionPath = ResolveSessionPath(account);
                    if (!File.Exists(sessionPath))
                    {
                        _logger.LogWarning("Session file missing for account {AccountId}: {Path}", account.Id, sessionPath);
                        var warn = zip.CreateEntry($"{safeFolder}/WARN.txt", CompressionLevel.Fastest);
                        await using var warnStream = warn.Open();
                        await using var warnWriter = new StreamWriter(warnStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        await warnWriter.WriteLineAsync($"未找到 session 文件：{sessionPath}");
                    }
                    else
                    {
                        // 若 session 被本进程的 Telegram 客户端锁定，先移除客户端释放文件锁，然后重试读取
                        await CopySessionWithRetryAsync(zip, safeFolder, sessionPath, account.Id, cancellationToken);
                    }

                    var jsonEntry = zip.CreateEntry($"{safeFolder}/{safeFolder}.json", CompressionLevel.Fastest);
                    await using (var jsonStream = jsonEntry.Open())
                    await using (var writer = new StreamWriter(jsonStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    {
                        // 兼容当前 Zip 导入逻辑：json 里需要 api_id/api_hash/phone
                        var json = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            phone = phone,
                            user_id = account.UserId > 0 ? (long?)account.UserId : null,
                            username = account.Username,
                            first_name = account.Nickname,
                            api_id = account.ApiId,
                            api_hash = account.ApiHash,
                            exported_at_utc = DateTime.UtcNow
                        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                        await writer.WriteAsync(json);
                    }

                    var twoFactorPassword = (account.TwoFactorPassword ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(twoFactorPassword))
                    {
                        var twoFaEntry = zip.CreateEntry($"{safeFolder}/2fa.txt", CompressionLevel.Fastest);
                        await using var twoFaStream = twoFaEntry.Open();
                        await using var twoFaWriter = new StreamWriter(twoFaStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        await twoFaWriter.WriteAsync(twoFactorPassword);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to export account {AccountId}", account.Id);
                    var err = zip.CreateEntry($"{safeFolder}/ERROR.txt", CompressionLevel.Fastest);
                    await using var errStream = err.Open();
                    await using var errWriter = new StreamWriter(errStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    await errWriter.WriteLineAsync($"导出该账号时发生异常：{ex.Message}");
                }
            }
        }

        return ms.ToArray();
    }

    private async Task CopySessionWithRetryAsync(
        ZipArchive zip,
        string safeFolder,
        string sessionPath,
        int accountId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = zip.CreateEntry($"{safeFolder}/{safeFolder}.session", CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(sessionPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await fileStream.CopyToAsync(entryStream, cancellationToken);
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Session file is locked (attempt {Attempt}/{Max}) for account {AccountId}: {Path}", attempt, maxAttempts, accountId, sessionPath);

                // 尝试释放本进程内的文件锁（WTelegramClient 会锁 session）
                try
                {
                    await _clientPool.RemoveClientAsync(accountId);
                }
                catch (Exception removeEx)
                {
                    _logger.LogDebug(removeEx, "Failed to remove client for account {AccountId} while exporting", accountId);
                }

                await Task.Delay(200, cancellationToken);
            }
        }

        // 最终失败：写出说明文件，不抛出导致整个导出失败
        var warn = zip.CreateEntry($"{safeFolder}/WARN.txt", CompressionLevel.Fastest);
        await using var warnStream = warn.Open();
        await using var warnWriter = new StreamWriter(warnStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await warnWriter.WriteLineAsync($"session 文件被占用无法读取：{sessionPath}");
        await warnWriter.WriteLineAsync("建议：先在面板停止该账号的 Telegram 客户端（或重启应用）后再导出。");
    }

    private string ResolveSessionPath(Account account)
    {
        var sessionPath = account.SessionPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionPath))
        {
            var sessionsPath = _configuration["Telegram:SessionsPath"] ?? "sessions";
            var phoneDigits = NormalizePhone(account.Phone);
            return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, sessionsPath, $"{phoneDigits}.session"));
        }

        if (Path.IsPathRooted(sessionPath))
            return Path.GetFullPath(sessionPath);

        // SessionPath 通常存的是相对路径（例如 sessions/<phone>.session），以 ContentRoot 作为基准更稳定
        var combined = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, sessionPath));
        if (File.Exists(combined))
            return combined;

        return Path.GetFullPath(sessionPath);
    }

    private static string BuildSafeFolderName(string phone, int accountId)
    {
        var digits = NormalizePhone(phone);
        if (!string.IsNullOrWhiteSpace(digits))
            return digits;
        return $"account_{accountId}";
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = new char[phone.Length];
        var count = 0;
        foreach (var ch in phone)
        {
            if (ch >= '0' && ch <= '9')
                digits[count++] = ch;
        }
        return count == 0 ? string.Empty : new string(digits, 0, count);
    }
}
