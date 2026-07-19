namespace TelegramPanel.Web.Services;

public sealed class AdminAuthOptions
{
    public bool Enabled { get; set; }
    public string InitialUsername { get; set; } = "tgpanel";
    public string InitialPassword { get; set; } = "tgpanel123";
    public string CredentialsPath { get; set; } = "admin_auth.json";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(InitialUsername) && !string.IsNullOrWhiteSpace(InitialPassword);
}
