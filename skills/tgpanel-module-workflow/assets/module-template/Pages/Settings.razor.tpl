@using Microsoft.AspNetCore.Components

<h3>{{MODULE_NAME}} 设置（旧 Razor 兼容页）</h3>
<p>新模块默认使用 wwwroot/settings.html 静态 Vue 页。只有旧模块兼容场景才使用此 Razor 模板。</p>

@code {
    [Parameter] public string ModuleId { get; set; } = "";
    [Parameter] public string PageKey { get; set; } = "";
}
