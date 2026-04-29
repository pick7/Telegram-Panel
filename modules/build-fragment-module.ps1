#!/usr/bin/env pwsh
# Fragment 用户名检测模块打包脚本

param(
    [switch]$Full
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "fragment-username-checker"
$projectFile = Join-Path $projectDir "FragmentUsernameChecker.csproj"
$manifestFile = Join-Path $projectDir "manifest.json"

if (-not (Test-Path $projectFile)) {
    Write-Error "找不到项目文件: $projectFile"
    exit 1
}

if (-not (Test-Path $manifestFile)) {
    Write-Error "找不到 manifest.json: $manifestFile"
    exit 1
}

Write-Host "正在打包 Fragment 用户名检测模块..." -ForegroundColor Cyan

# 调用项目的打包脚本
$packageScript = Join-Path $scriptDir "..\tools\package-module.ps1"
if (-not (Test-Path $packageScript)) {
    Write-Error "找不到打包脚本: $packageScript"
    exit 1
}

if ($Full) {
    & $packageScript -Project $projectFile -Manifest $manifestFile -Full
} else {
    & $packageScript -Project $projectFile -Manifest $manifestFile
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "\n打包成功！" -ForegroundColor Green
    Write-Host "模块包位于: artifacts/modules/" -ForegroundColor Yellow
    Write-Host "\n安装步骤:" -ForegroundColor Cyan
    Write-Host "1. 将 .tpm 文件上传到面板的"模块管理"页面" -ForegroundColor White
    Write-Host "2. 安装并启用模块" -ForegroundColor White
    Write-Host "3. 重启服务使模块生效" -ForegroundColor White
} else {
    Write-Error "打包失败"
}