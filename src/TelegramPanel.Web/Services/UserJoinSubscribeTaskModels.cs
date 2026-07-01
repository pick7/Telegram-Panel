using System.Text.Json.Serialization;

namespace TelegramPanel.Web.Services;

public sealed class UserJoinSubscribeTaskConfig
{
    [JsonPropertyName("accountIds")]
    public List<int> AccountIds { get; set; } = new();

    [JsonPropertyName("AccountIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? LegacyAccountIds { get; set; }

    [JsonPropertyName("links")]
    public List<string> Links { get; set; } = new();

    [JsonPropertyName("Links")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyLinks { get; set; }

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = UserJoinSubscribeOperations.Join;

    [JsonPropertyName("treatNoBotSuffixAsBot")]
    public bool TreatNoBotSuffixAsBot { get; set; }

    [JsonPropertyName("delayMs")]
    public int DelayMs { get; set; } = 2000;

    [JsonPropertyName("DelayMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyDelayMs { get; set; }

    [JsonPropertyName("canceled")]
    public bool Canceled { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("failures")]
    public List<UserJoinSubscribeTaskFailure> Failures { get; set; } = new();
}

public static class UserJoinSubscribeOperations
{
    public const string Join = "join";
    public const string Leave = "leave";
}

public sealed class UserJoinSubscribeTaskFailure
{
    [JsonPropertyName("accountId")]
    public int AccountId { get; set; }

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
