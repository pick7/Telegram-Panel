using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TelegramPanel.Web.Modules;

public static class ModuleApplicationExtensions
{
    public static void MapInstalledModules(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<ModuleRegistry>();
        registry.MapEndpoints(app, app.Services.GetRequiredService<ILogger<ModuleRegistry>>());
    }
}
