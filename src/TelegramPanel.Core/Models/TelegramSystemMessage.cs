namespace TelegramPanel.Core.Models;

/// <summary>
/// Telegram 系统通知（常用于接收登录验证码，来自 777000 对话）
/// </summary>
public record TelegramSystemMessage(
    int Id,
    DateTime? DateUtc,
    string Text
);

