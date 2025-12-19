using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Web.Services;

public class AccountExportService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountExportService> _logger;

    public AccountExportService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<AccountExportService> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
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
                await writer.WriteLineAsync("结构：每个账号一个子文件夹，包含一个 .json + 一个 .session。");
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

                var sessionPath = ResolveSessionPath(account);
                if (!File.Exists(sessionPath))
                {
                    _logger.LogWarning("Session file missing for account {AccountId}: {Path}", account.Id, sessionPath);
                }
                else
                {
                    var entry = zip.CreateEntry($"{safeFolder}/{safeFolder}.session", CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var fileStream = new FileStream(sessionPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await fileStream.CopyToAsync(entryStream, cancellationToken);
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
            }
        }

        return ms.ToArray();
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
