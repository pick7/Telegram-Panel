param(
    [Parameter(Mandatory = $false)]
    [string]$ModuleDir,

    [Parameter(Mandatory = $false)]
    [string]$Project,

    [Parameter(Mandatory = $false)]
    [string]$Manifest,

    [Parameter(Mandatory = $false)]
    [string]$OutDir = "artifacts/modules",

    [Parameter(Mandatory = $false)]
    [switch]$Full,

    [Parameter(Mandatory = $false)]
    [switch]$NoDocker,

    [Parameter(Mandatory = $false)]
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Base,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $basePath = (Resolve-Path $Base).Path
    $targetPath = (Resolve-Path $Target).Path

    if ([System.IO.Path].GetMethods().Name -contains "GetRelativePath")
    {
        return [System.IO.Path]::GetRelativePath($basePath, $targetPath).Replace('\\', '/')
    }

    $baseUri = New-Object System.Uri(($basePath.TrimEnd('\') + '\'))
    $targetUri = New-Object System.Uri($targetPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('\\', '/')
}

function Resolve-SingleFile {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$DisplayName
    )

    $items = Get-ChildItem -Path $Directory -Filter $Pattern -File
    if ($items.Count -eq 0)
    {
        throw "$DisplayName 不存在：$Directory"
    }
    if ($items.Count -gt 1)
    {
        $names = $items | ForEach-Object { $_.Name }
        throw "$DisplayName 不唯一：$Directory -> $($names -join ', ')"
    }

    return $items[0].FullName
}

$repoRoot = (Resolve-Path ".").Path
$packScript = Join-Path $repoRoot "tools/package-module.ps1"
if (-not (Test-Path -Path $packScript))
{
    throw "未找到打包脚本：tools/package-module.ps1。请在仓库根目录执行本脚本。"
}

if ([string]::IsNullOrWhiteSpace($Project) -or [string]::IsNullOrWhiteSpace($Manifest))
{
    if ([string]::IsNullOrWhiteSpace($ModuleDir))
    {
        throw "请至少提供 -ModuleDir，或同时提供 -Project 与 -Manifest。"
    }

    $modulePath = (Resolve-Path $ModuleDir).Path

    if ([string]::IsNullOrWhiteSpace($Project))
    {
        $projectPath = Resolve-SingleFile -Directory $modulePath -Pattern "*.csproj" -DisplayName "模块 csproj"
        $Project = Get-RepoRelativePath -Base $repoRoot -Target $projectPath
    }

    if ([string]::IsNullOrWhiteSpace($Manifest))
    {
        $manifestPath = Join-Path $modulePath "manifest.json"
        if (-not (Test-Path -Path $manifestPath))
        {
            throw "manifest.json 不存在：$modulePath"
        }
        $Manifest = Get-RepoRelativePath -Base $repoRoot -Target $manifestPath
    }
}

$projectAbs = if ([System.IO.Path]::IsPathRooted($Project)) { $Project } else { Join-Path $repoRoot $Project }
$manifestAbs = if ([System.IO.Path]::IsPathRooted($Manifest)) { $Manifest } else { Join-Path $repoRoot $Manifest }

if (-not (Test-Path -Path $projectAbs)) { throw "Project 不存在：$Project" }
if (-not (Test-Path -Path $manifestAbs)) { throw "Manifest 不存在：$Manifest" }

$Project = Get-RepoRelativePath -Base $repoRoot -Target (Resolve-Path $projectAbs).Path
$Manifest = Get-RepoRelativePath -Base $repoRoot -Target (Resolve-Path $manifestAbs).Path

$manifestObj = Get-Content -Path $manifestAbs -Raw | ConvertFrom-Json
if ($null -eq $manifestObj.id -or [string]::IsNullOrWhiteSpace([string]$manifestObj.id))
{
    throw "manifest.json 缺少 id"
}
if ($null -eq $manifestObj.version -or [string]::IsNullOrWhiteSpace([string]$manifestObj.version))
{
    throw "manifest.json 缺少 version"
}

$packText = Get-Content -Path $packScript -Raw
$supportNoDocker = $packText.Contains('[switch]$NoDocker')
$supportKeepTemp = $packText.Contains('[switch]$KeepTemp')

$args = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $packScript,
    "-Project", $Project,
    "-Manifest", $Manifest,
    "-OutDir", $OutDir
)

if ($Full) { $args += "-Full" }

if ($NoDocker)
{
    if ($supportNoDocker) { $args += "-NoDocker" }
    else { Write-Warning "当前仓库打包脚本不支持 -NoDocker，已忽略。" }
}

if ($KeepTemp)
{
    if ($supportKeepTemp) { $args += "-KeepTemp" }
    else { Write-Warning "当前仓库打包脚本不支持 -KeepTemp，已忽略。" }
}

Write-Host "Project : $Project" -ForegroundColor Cyan
Write-Host "Manifest: $Manifest" -ForegroundColor Cyan
Write-Host "OutDir  : $OutDir" -ForegroundColor Cyan

& powershell @args
if ($LASTEXITCODE -ne 0)
{
    throw "打包失败（退出码：$LASTEXITCODE）"
}

$outFile = Join-Path $repoRoot (Join-Path $OutDir ("{0}-{1}.tpm" -f $manifestObj.id, $manifestObj.version))
if (Test-Path -Path $outFile)
{
    Write-Host "产物: $outFile" -ForegroundColor Green
}
else
{
    Write-Warning "打包命令执行完成，但未在预期路径发现产物：$outFile"
}
