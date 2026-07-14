using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramPanel.Modules;

namespace {{ROOT_NAMESPACE}};

public sealed class {{MODULE_CLASS}} : ITelegramPanelModule, IModuleUiProvider
{
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "{{MODULE_ID}}",
        Name = "{{MODULE_NAME}}",
        Version = "{{VERSION}}",
        Host = new HostCompatibility { Min = "{{HOST_MIN}}", Max = "{{HOST_MAX}}" },
        Entry = new ModuleEntryPoint
        {
            Assembly = "{{ASSEMBLY_NAME}}.dll",
            Type = typeof({{MODULE_CLASS}}).FullName!
        }
    };

    public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
    {
        // 在这里注册模块服务。
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ModuleHostContext context)
    {
        var authEnabled = endpoints.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .GetValue("AdminAuth:Enabled", true);

        var page = endpoints.MapGet("/ext/{{MODULE_ID}}/settings", GetSettingsPageAsync);
        var assets = endpoints.MapGet("/ext/{{MODULE_ID}}/assets/{file}", GetAssetAsync);
        var api = endpoints.MapGroup("/api/panel/extensions/{{MODULE_API_SLUG}}");

        if (authEnabled)
        {
            page.RequireAuthorization();
            assets.RequireAuthorization();
            api.RequireAuthorization();
        }

        api.MapGet("", GetStateAsync);
    }

    public IEnumerable<ModuleNavItem> GetNavItems(ModuleHostContext context)
    {
        yield return new ModuleNavItem
        {
            Title = "{{MODULE_NAME}}",
            Href = "/ext/{{MODULE_ID}}/settings",
            Icon = "Extension",
            Group = "扩展模块",
            Order = 100
        };
    }

    public IEnumerable<ModulePageDefinition> GetPages(ModuleHostContext context)
        => Array.Empty<ModulePageDefinition>();

    private static async Task<IResult> GetSettingsPageAsync(HttpContext http)
    {
        var path = Path.Combine(GetWwwrootPath(), "settings.html");
        if (!File.Exists(path))
            return Results.NotFound();

        var html = await File.ReadAllTextAsync(path, http.RequestAborted);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> GetAssetAsync(string file, HttpContext http)
    {
        file = Path.GetFileName((file ?? string.Empty).Trim());
        if (file.Length == 0)
            return Results.NotFound();

        var path = Path.Combine(GetWwwrootPath(), "assets", file);
        if (!File.Exists(path))
            return Results.NotFound();

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        var bytes = await File.ReadAllBytesAsync(path, http.RequestAborted);
        return Results.File(bytes, GetContentType(file));
    }

    private static IResult GetStateAsync()
    {
        return Results.Ok(new
        {
            ok = true,
            moduleId = "{{MODULE_ID}}",
            name = "{{MODULE_NAME}}",
            version = "{{VERSION}}"
        });
    }

    private static string GetWwwrootPath()
    {
        var baseDir = Path.GetDirectoryName(typeof({{MODULE_CLASS}}).Assembly.Location)
            ?? AppContext.BaseDirectory;
        return Path.Combine(baseDir, "wwwroot");
    }

    private static string GetContentType(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        return ext switch
        {
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
