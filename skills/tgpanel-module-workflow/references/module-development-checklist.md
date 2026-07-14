# TGPanel 模块开发检查清单

## 1. 模块结构

- 模块根目录必须直接包含：`manifest.json`、`*.csproj`
- 新模块页面默认包含：`wwwroot/settings.html`、`wwwroot/assets/**`
- 按需包含：`Services/`、`Models/`、`Pages/`（仅旧 Razor 兼容页）
- 若有历史发布目录（如 `publish/`），在 `.csproj` 里排除，避免递归打包

## 2. manifest 必填项

- `id`：稳定唯一标识，发布后不要随意变更
- `version`：语义化版本，发布新包递增
- `host.min/max`：宿主兼容区间
- `entry.assembly`：入口程序集文件名
- `entry.type`：入口类型全名

## 3. 入口类与扩展点

- 入口类必须实现 `ITelegramPanelModule`
- 按需实现：
  - `IModuleTaskProvider` / `IModuleTaskHandler`
  - `IModuleApiProvider`
  - `IModuleUiProvider`

## 4. UI 页面硬性要求

- 新模块默认不要创建 Razor/Blazor 页面。
- 宿主 Vue 原生页：模块提供 `/api/panel/extensions/{module-slug}` 管理接口，页面放在宿主 `frontend/src/views/extensions/`。
- 模块自带静态 Vue 页：模块自己映射 `/ext/{moduleId}/settings`，并提供 `/ext/{moduleId}/assets/{file}` 静态资源。
- 静态 Vue 页的 `IModuleUiProvider.GetPages()` 返回空，导航通过 `GetNavItems()` 返回 `Href = "/ext/{moduleId}/settings"`。
- 只有旧 Razor 兼容页通过 `IModuleUiProvider.GetPages` 提供组件；此时组件必须声明：
  - `[Parameter] public string ModuleId { get; set; } = "";`
  - `[Parameter] public string PageKey { get; set; } = "";`

## 5. 运行模式建议

- 一次性批处理：使用 `IModuleTaskHandler`
- 常驻监听/通知：使用 `HostedService`
- 配置入口：新模块优先 `CreateRoute = "/ext/{moduleId}/settings"` 或宿主 Vue 路由

## 6. API 安全

- 显式声明 `AllowAnonymous()` 或 `RequireAuthorization()`
- 对匿名接口自行补齐 token、限流、防缓存等控制
- 后台管理接口默认使用 `/api/panel/extensions/{module-slug}`，跟随宿主后台登录鉴权

## 7. 打包前自查

- `manifest.version` 与发布目标一致
- `entry.assembly` 与编译输出一致
- 静态 Vue 页、JS、CSS 均配置 `CopyToPublishDirectory`
- `/ext/{moduleId}/settings` 和 `/api/panel/extensions/{module-slug}` 路由一致可访问
- 目标模块目录没有临时测试文件误入包
- 打包脚本路径与参数正确
