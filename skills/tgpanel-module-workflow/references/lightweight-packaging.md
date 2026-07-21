# TGPanel 轻量化打包指南

## 1. 目标

- 输出可安装模块包：`artifacts/modules/<moduleId>-<version>.tpm`
- 默认优先轻量化，减少重复依赖与包体积

## 2. 推荐命令

```powershell
powershell tools/package-module.ps1 -Project "<模块.csproj相对路径>" -Manifest "<manifest.json相对路径>"
```

多数 TGPanel 仓库中，默认行为已经偏向轻量化。

## 3. 参数策略

- 默认：不传 `-Full`
- `-Full`：保留全部发布产物，体积更大，仅兼容性需要时使用
- 私有模块仓库脚本可能额外支持：`-NoDocker`、`-KeepTemp`

## 4. 轻量化剔除核心思路

轻量化通常会剔除由宿主共享加载或宿主内置的程序集，例如：

- `Microsoft.AspNetCore.*.dll`
- `Microsoft.Extensions.*.dll`
- `Microsoft.JSInterop*.dll`
- `TelegramPanel.*.dll`
- `MudBlazor*.dll`
- `Microsoft.EntityFrameworkCore*.dll`
- `Microsoft.Data.Sqlite*.dll`
- `SQLitePCLRaw*.dll`
- `WTelegramClient*.dll`
- `SixLabors.ImageSharp*.dll`
- `PhoneNumbers*.dll`

并可能剔除：

- `runtimes/`（尤其 SQLite 多平台 native 体积较大）
- `wwwroot/_content/MudBlazor`（旧 Razor/MudBlazor 静态资源由宿主提供）

新模块如果使用自带静态 Vue 页，必须保留模块自己的：

- `lib/wwwroot/settings.html`
- `lib/wwwroot/assets/**`

模块编译时可以引用 `TelegramPanel.Core` 里的账号服务。运行时仍应使用宿主版本，
不要因为引用了账号代理相关类型就把 `TelegramPanel.Core.dll`、`WTelegramClient.dll`
或代理实现依赖重新打入模块包。

## 5. 产物校验

1. 解压 `.tpm` 后根目录应包含：`manifest.json`、`lib/`
2. `lib/` 必须包含 `entry.assembly` 对应 DLL
3. 若模块提供静态 Vue 页，确认 `lib/wwwroot/settings.html` 和 `lib/wwwroot/assets/vue.esm-browser.prod.js` 存在
4. 若启动时报类型加载问题，先排查是否重复携带边界程序集
