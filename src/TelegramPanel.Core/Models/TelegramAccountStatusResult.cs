using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Models;

/// <summary>
/// 账号状态检测结果（用于诊断账号是否可正常连通 Telegram）
/// </summary>
public record TelegramAccountStatusResult(
    bool Ok,
    string Summary,
    string? Details,
    DateTime CheckedAtUtc,
    TelegramAccountProfile? Profile = null
);

/// <summary>
/// Telegram 账号资料快照
/// </summary>
public record TelegramAccountProfile(
    long UserId,
    string? Phone,
    string? Username,
    string? FirstName,
    string? LastName,
    bool IsDeleted,
    bool IsScam,
    bool IsFake,
    bool IsRestricted,
    bool IsVerified,
    bool IsPremium
)
{
    public string DisplayName
    {
        get
        {
            var full = $"{FirstName} {LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(full))
                return full;
            if (!string.IsNullOrWhiteSpace(Username))
                return $"@{Username}";
            if (!string.IsNullOrWhiteSpace(Phone))
                return Phone;
            return UserId.ToString();
        }
    }

    public void ApplyTo(Account account)
    {
        if (account.UserId <= 0 && UserId > 0)
            account.UserId = UserId;

        if (!string.IsNullOrWhiteSpace(Username))
            account.Username = Username;

        var nickname = DisplayName;
        if (!string.IsNullOrWhiteSpace(nickname))
            account.Nickname = nickname;
    }
}

