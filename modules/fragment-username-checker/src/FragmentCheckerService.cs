using Microsoft.Extensions.Logging;

namespace FragmentUsernameChecker.Services;

/// <summary>
/// Fragment.com 用户名可用性查询服务。
/// </summary>
public class FragmentCheckerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FragmentCheckerService> _logger;

    public FragmentCheckerService(HttpClient httpClient, ILogger<FragmentCheckerService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 检查用户名是否可注册。
    /// </summary>
    /// <param name="username">要检查的用户名（不含 @）</param>
    /// <returns>true 表示可注册（Unavailable），false 表示已被占用</returns>
    public async Task<UsernameCheckResult> CheckUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        try
        {
            username = username.Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(username))
                return new UsernameCheckResult(username, false, "用户名为空");

            var url = $"https://fragment.com/?query={Uri.EscapeDataString(username)}";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");

            var response = await _httpClient.GetStringAsync(url, cancellationToken);

            // 按当前模块约定处理：Fragment 页面出现 Unavailable 即表示该用户名未注册，可尝试分配给频道。
            if (response.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("用户名 {Username} 未注册（Unavailable）", username);
                return new UsernameCheckResult(username, true, "未注册");
            }

            if (response.Contains("Taken", StringComparison.OrdinalIgnoreCase)
                || response.Contains("On Auction", StringComparison.OrdinalIgnoreCase)
                || response.Contains("On Sale", StringComparison.OrdinalIgnoreCase)
                || response.Contains("Not Available", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("用户名 {Username} 已被占用", username);
                return new UsernameCheckResult(username, false, "已被占用");
            }

            _logger.LogWarning("用户名 {Username} 状态未知", username);
            return new UsernameCheckResult(username, false, "状态未知");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查用户名 {Username} 时出错", username);
            return new UsernameCheckResult(username, false, $"查询失败: {ex.Message}");
        }
    }

    public async Task<List<UsernameCheckResult>> CheckUsernamesAsync(
        IEnumerable<string> usernames,
        int delayMs = 1000,
        CancellationToken cancellationToken = default)
    {
        var results = new List<UsernameCheckResult>();

        foreach (var username in usernames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await CheckUsernameAsync(username, cancellationToken);
            results.Add(result);

            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken);
        }

        return results;
    }
}

public record UsernameCheckResult(
    string Username,
    bool IsAvailable,
    string Status
);
