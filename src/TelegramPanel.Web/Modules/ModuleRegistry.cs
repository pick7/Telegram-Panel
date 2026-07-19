using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using TelegramPanel.Modules;

namespace TelegramPanel.Web.Modules;

public sealed class ModuleRegistry
{
    private readonly List<LoadedModule> _modules = new();

    public IReadOnlyList<LoadedModule> Modules => _modules;

    public void Add(LoadedModule module) => _modules.Add(module);

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ILogger<ModuleRegistry> logger)
    {
        foreach (var m in _modules)
        {
            try
            {
                m.Instance.MapEndpoints(endpoints, m.Context);
            }
            catch (Exception ex)
            {
                // 模块端点注册失败不应阻断主站启动，但必须留下完整异常，
                // 否则侧栏仍会显示模块、请求却会静默落到 Razor 壳页面。
                logger.LogError(ex, "模块端点注册失败：{ModuleId} {Version}", m.Id, m.Version);
            }
        }
    }
}

public sealed record LoadedModule(
    string Id,
    string Version,
    bool BuiltIn,
    ITelegramPanelModule Instance,
    ModuleHostContext Context,
    ModuleManifest Manifest,
    string? ModuleRootPath);
