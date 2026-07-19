namespace TelegramPanel.Web.Modules;

/// <summary>
/// 解析模块原生页面路由，供后台导航和 Vue 宿主统一使用。
/// </summary>
internal static class ExtensionModuleRoute
{
    public static bool TryParse(string? href, out string moduleId, out string pageKey)
    {
        moduleId = string.Empty;
        pageKey = string.Empty;

        var value = (href ?? string.Empty).Trim();
        if (value.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase))
            value = value[3..];

        var queryIndex = value.IndexOf('?');
        var fragmentIndex = value.IndexOf('#');
        var suffixIndex = queryIndex < 0
            ? fragmentIndex
            : fragmentIndex < 0
                ? queryIndex
                : Math.Min(queryIndex, fragmentIndex);
        if (suffixIndex >= 0)
            value = value[..suffixIndex];

        var parts = value.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !string.Equals(parts[0], "ext", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            moduleId = Uri.UnescapeDataString(parts[1]);
            pageKey = Uri.UnescapeDataString(parts[2]);
        }
        catch (UriFormatException)
        {
            moduleId = string.Empty;
            pageKey = string.Empty;
            return false;
        }

        return !string.IsNullOrWhiteSpace(moduleId) && !string.IsNullOrWhiteSpace(pageKey);
    }

    public static bool Matches(string? href, string moduleId, string pageKey)
        => TryParse(href, out var routeModuleId, out var routePageKey)
           && string.Equals(routeModuleId, moduleId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(routePageKey, pageKey, StringComparison.OrdinalIgnoreCase);

    public static string BuildVuePath(string moduleId, string pageKey)
        => $"/ui/ext/{Uri.EscapeDataString(moduleId)}/{Uri.EscapeDataString(pageKey)}";
}
