namespace TelegramPanel.Data.Entities;

/// <summary>
/// WARP 容器及代理配置实体。
/// </summary>
public class WarpProfile
{
    public int Id { get; set; }
    public string ProfileId { get; set; } = null!;
    public string? RequestId { get; set; }
    public int? OutboundProxyId { get; set; }
    public string ContainerName { get; set; } = null!;
    public string? ContainerId { get; set; }
    public string VolumeName { get; set; } = null!;
    public int HostPort { get; set; }
    public string Status { get; set; } = "pending";
    public bool DesiredEnabled { get; set; } = true;
    public string? EgressIp { get; set; }
    public string? Country { get; set; }
    public string? WarpStatus { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastRecoveryAttemptAtUtc { get; set; }
    public DateTime? LastRecoveredAtUtc { get; set; }
    public int RecoveryCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // 导航属性
    public OutboundProxy? Proxy { get; set; }
}
