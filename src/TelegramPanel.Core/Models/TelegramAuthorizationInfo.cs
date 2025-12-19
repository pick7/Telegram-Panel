namespace TelegramPanel.Core.Models;

/// <summary>
/// 在线设备 / 会话信息（来自 account.getAuthorizations）
/// </summary>
public record TelegramAuthorizationInfo(
    long Hash,
    bool Current,
    int ApiId,
    string? AppName,
    string? AppVersion,
    string? DeviceModel,
    string? Platform,
    string? SystemVersion,
    string? Ip,
    string? Country,
    string? Region,
    DateTime? CreatedAtUtc,
    DateTime? LastActiveAtUtc
)
{
    public string Title
    {
        get
        {
            var app = string.IsNullOrWhiteSpace(AppName) ? "UnknownApp" : AppName;
            var device = string.IsNullOrWhiteSpace(DeviceModel) ? "UnknownDevice" : DeviceModel;
            return $"{app} - {device}";
        }
    }
}

