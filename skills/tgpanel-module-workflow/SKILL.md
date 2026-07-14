---
name: tgpanel-module-workflow
description: 在 Telegram-Panel 或 Telegram-Panel-PrivateModules 代码库中，流程化创建、改造与排错模块（任务/API/Vue UI）并执行轻量化打包。用户提到“写模块”“扩展任务中心”“扩展 API 管理”“扩展模块页面”“Vue 模块页面”“manifest.json”“package-module.ps1”“轻量化 .tpm”“Slim/SlimHost/Full”时使用。
---

# TGPanel Module Workflow

## 目标

- 先对齐宿主模块规范，再实现代码，避免运行时加载失败。
- 新模块默认使用 Vue/静态页或宿主 Vue 原生页，不再默认创建 Razor/Blazor 页面。
- 默认产出轻量化 `.tpm`，仅在兼容性需要时切换 Full 包。
- 保留可复现命令、关键校验结果、风险说明。

## 流程决策

1. 判断任务类型。
   - 新增模块：执行“步骤 1 -> 步骤 2 -> 步骤 3 -> 步骤 4”。
   - 修改已有模块：执行“步骤 1 -> 步骤 2（仅改动范围）-> 步骤 3 -> 步骤 4”。
   - 仅打包：执行“步骤 1（最小检查）-> 步骤 3 -> 步骤 4”。
2. 优先使用仓库现有脚本与文档，不重复造轮子。
3. 只在必须时读取额外文件，控制上下文体积。

## 步骤 1：收集上下文

1. 优先读取以下文件。
   - `docs/developer/modules.md`
   - `upstream/Telegram-Panel/docs/developer/modules.md`
   - `tools/package-module.ps1`
2. 定位目标模块。
   - 使用 `rg --files` 查找 `manifest.json` 与 `*.csproj`。
   - 确认模块根目录直接包含 `manifest.json + *.csproj`。
3. 对齐宿主加载边界。
   - 读取 `upstream/Telegram-Panel/src/TelegramPanel.Web/Modules/ModuleLoadContext.cs`。
   - 记录宿主共享程序集规则，作为轻量化剔除依据。

## 步骤 2：实现模块

1. 以 `assets/module-template/` 为起点创建或修复模块骨架；模板默认是普通 `Microsoft.NET.Sdk` + `wwwroot/settings.html` 静态 Vue 页。
2. 保证 `manifest.json` 与代码一致。
   - `entry.assembly` 必须等于实际入口 DLL。
   - `entry.type` 必须等于入口类型全名。
   - `id` 保持稳定，`version` 按语义化版本递增。
3. 入口类实现 `ITelegramPanelModule`，并按需实现扩展接口。
   - 任务扩展：`IModuleTaskProvider` / `IModuleTaskHandler`
   - API 扩展：`IModuleApiProvider`
   - UI 扩展：`IModuleUiProvider`
4. 新模块页面默认二选一。
   - 宿主 Vue 原生页：页面写在宿主 `frontend/src/views/extensions/`，模块只提供 `/api/panel/extensions/{module-slug}` 管理接口。
   - 模块自带静态 Vue 页：模块在 `wwwroot/` 放 `settings.html` 与 JS/CSS，自己映射 `/ext/{moduleId}/settings` 与 `/ext/{moduleId}/assets/{file}`；`GetPages()` 返回空。
5. 管理接口默认放在 `/api/panel/extensions/{module-slug}`，优先提供一个聚合 `GET ""`，减少页面首次加载请求数。
6. 只有旧模块、临时过渡页面，或确实需要复用 Blazor 组件时，才使用 Razor 兼容页；此时页面组件必须声明 `ModuleId` 与 `PageKey` 参数。
7. 若能力属于常驻监听或后台服务，优先 `HostedService`，不要误塞到批量任务队列。
8. 若新增 API 端点，显式选择鉴权策略（`AllowAnonymous` 或 `RequireAuthorization`）。

## 步骤 3：编译与轻量化打包

1. 在仓库根目录执行打包。
1.1 只要本次任务改动了任一模块相关文件（例如 `manifest.json`、`*.csproj`、`*.cs`、`wwwroot/**`、`*.html`、`*.js`、`*.css`，以及旧模块的 `*.razor`），必须在交付前默认执行一次 `.tpm` 打包，不等待用户再次要求“编译”。
1.2 若一次任务改动多个模块，则每个被改动模块都要分别打包并产出对应 `.tpm`。
2. 优先运行本技能脚本（自动补全 `Project/Manifest`）：

```powershell
powershell -ExecutionPolicy Bypass -File "skills/tgpanel-module-workflow/scripts/package-module-lite.ps1" -ModuleDir "模块源码/你的模块目录"
```

3. 直接调用仓库脚本也可：

```powershell
powershell tools/package-module.ps1 -Project "<模块.csproj相对路径>" -Manifest "<manifest.json相对路径>"
```

4. 轻量化策略。
   - 默认不要加 `-Full`。
   - 仅在目标宿主不保证内置依赖一致时，才考虑 `-Full`。
   - 优先遵循仓库脚本默认行为（通常已偏向轻量化）。

## 步骤 4：校验产物

1. 确认产物存在：`artifacts/modules/<moduleId>-<version>.tpm`。
2. 解包检查结构。
   - 根目录必须有 `manifest.json`
   - 必须有 `lib/<entry assembly>.dll`
3. 抽样检查轻量化结果。
   - 重点确认未误删入口程序集。
   - 重点确认宿主共享程序集未被重复打包（或数量可接受）。

## 步骤 5：交付说明

1. 列出改动文件与核心行为变化。
2. 列出执行命令与关键输出路径。
3. 说明未完成项、风险点、后续建议。

## 输出约束（TPM 构建请求）

当用户明确要求“帮我编译/构建 tpm 模块”时，最终回复必须遵循以下规则：

1. 只输出已构建 `.tpm` 的绝对路径，纯文本一行。
2. 不使用 Markdown 链接，不使用任何超链接格式。
3. 不附加解释、提示、摘要、命令、日志、前后缀话术。
4. 若构建失败，只输出一行失败原因（纯文本），不要附加其它内容。

当用户并未显式要求编译，但本次任务已改动模块文件时，仍必须默认执行打包；最终回复至少包含每个产物的绝对路径（纯文本），避免遗漏交付产物。

## 常见故障处理

1. `TypeLoadException` / 组件参数绑定异常。
   - 先检查是否把 `Microsoft.Extensions.*`、`Microsoft.AspNetCore.*`、`TelegramPanel.*` 等边界程序集错误携带进模块包。
2. Vue 模块页面 404/白屏。
   - 先检查 `/ext/{moduleId}/settings` 是否由模块 endpoint 返回。
   - 再检查 `/ext/{moduleId}/assets/{file}` 是否能返回 Vue/JS/CSS 静态资源。
   - 如果 Vue 页面请求 `/api/panel/extensions/{module-slug}` 返回 404，说明模块管理端 API 没补齐或 slug 不一致。
3. 旧 Razor 页面 500。
   - 仅兼容模式下检查页面组件是否声明 `ModuleId` 与 `PageKey` 参数。
4. 打包后体积异常大。
   - 先检查是否误用 `-Full`。
   - 再检查 `publish/` 历史产物是否被当作内容打进包内。

## 资源索引

- 规则清单：`references/module-development-checklist.md`
- 轻量化打包：`references/lightweight-packaging.md`
- 代码模板：`assets/module-template/`
- 自动打包脚本：`scripts/package-module-lite.ps1`
