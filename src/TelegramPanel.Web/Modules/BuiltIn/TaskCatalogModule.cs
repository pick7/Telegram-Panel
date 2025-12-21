using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Modules;
using MudBlazor;

namespace TelegramPanel.Web.Modules.BuiltIn;

public sealed class TaskCatalogModule : ITelegramPanelModule, IModuleTaskProvider
{
    public TaskCatalogModule(string version)
    {
        Manifest = new ModuleManifest
        {
            Id = "builtin.tasks",
            Name = "任务：内置批量任务",
            Version = version,
            Host = new HostCompatibility(),
            Entry = new ModuleEntryPoint { Assembly = "", Type = typeof(TaskCatalogModule).FullName ?? "" }
        };
    }

    public ModuleManifest Manifest { get; }

    public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
    {
        // 内置任务的执行由宿主 BatchTaskBackgroundService 负责；这里只提供“元数据”用于 UI 展示与创建。
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ModuleHostContext context)
    {
        // 无 endpoints
    }

    public IEnumerable<ModuleTaskDefinition> GetTasks(ModuleHostContext context)
    {
        yield return new ModuleTaskDefinition
        {
            Category = "bot",
            TaskType = BatchTaskTypes.Invite,
            DisplayName = "批量邀请",
            Description = "对所选频道批量邀请用户名列表（Bot/执行账号需具备权限）。",
            Icon = Icons.Material.Filled.PersonAdd,
            CreateRoute = "/batch/invite",
            Order = 10
        };

        yield return new ModuleTaskDefinition
        {
            Category = "bot",
            TaskType = BatchTaskTypes.SetAdmin,
            DisplayName = "设置管理员",
            Description = "对所选频道批量设置管理员（Bot/执行账号需具备权限）。",
            Icon = Icons.Material.Filled.AdminPanelSettings,
            CreateRoute = "/batch/admins",
            Order = 20
        };

        yield return new ModuleTaskDefinition
        {
            Category = "system",
            TaskType = BatchTaskTypes.ExternalApiKick,
            DisplayName = "外部 API：踢人/封禁",
            Description = "由外部接口触发并记录到任务中心（一般无需手动创建）。",
            Icon = Icons.Material.Filled.Link,
            Order = 1000
        };
    }
}
