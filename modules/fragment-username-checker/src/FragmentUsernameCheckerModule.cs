using FragmentUsernameChecker.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using TelegramPanel.Modules;

namespace FragmentUsernameChecker;

/// <summary>
/// Fragment 用户名监控与抢注模块。
/// </summary>
public sealed class FragmentUsernameCheckerModule : ITelegramPanelModule, IModuleUiProvider, IModuleTaskProvider
{
    private const string ModuleVersion = "1.2.3";

    public FragmentUsernameCheckerModule()
    {
        Manifest = new ModuleManifest
        {
            Id = "fragment-username-checker",
            Name = "Fragment 用户名监控",
            Version = ModuleVersion,
            Host = new HostCompatibility { Min = "1.0.0" },
            Entry = new ModuleEntryPoint
            {
                Assembly = "FragmentUsernameChecker.dll",
                Type = typeof(FragmentUsernameCheckerModule).FullName ?? string.Empty
            }
        };
    }

    public ModuleManifest Manifest { get; }

    public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
    {
        services.AddHttpClient<FragmentCheckerService>();
        services.AddScoped<IModuleTaskHandler, FragmentUsernameTaskHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ModuleHostContext context)
    {
    }

    public IEnumerable<ModuleNavItem> GetNavItems(ModuleHostContext context)
    {
        yield return new ModuleNavItem
        {
            Title = "Fragment 用户名",
            Href = "/ext/fragment-username-checker/main",
            Icon = Icons.Material.Filled.Timer,
            Group = "工具",
            Order = 100
        };
    }

    public IEnumerable<ModulePageDefinition> GetPages(ModuleHostContext context)
    {
        yield return new ModulePageDefinition
        {
            Key = "main",
            Title = "Fragment 用户名监控",
            Icon = Icons.Material.Filled.Timer,
            Group = "工具",
            Order = 10,
            ComponentType = typeof(Pages.FragmentUsernamePage).AssemblyQualifiedName ?? string.Empty
        };
    }

    public IEnumerable<ModuleTaskDefinition> GetTasks(ModuleHostContext context)
    {
        yield return new ModuleTaskDefinition
        {
            Category = "channel",
            TaskType = FragmentUsernameTaskHandler.TaskType,
            DisplayName = "Fragment 用户名监控",
            Description = "定时监控 Fragment 用户名，一旦显示未注册，就从私密频道池挑选频道切换为公开用户名",
            Icon = Icons.Material.Filled.Timer,
            CreateRoute = "/ext/fragment-username-checker/main",
            TaskCenter = new ModuleTaskCenterCapabilities
            {
                CanPause = true,
                CanResume = true,
                CanRerun = true
            },
            Order = 150
        };
    }
}
