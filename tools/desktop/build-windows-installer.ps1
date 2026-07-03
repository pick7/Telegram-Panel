param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifactsRoot = Join-Path $repoRoot "artifacts\desktop"
$webOut = Join-Path $artifactsRoot "web"
$appOut = Join-Path $artifactsRoot "app"
$payloadOut = Join-Path $artifactsRoot "payload"
$payloadZip = Join-Path $artifactsRoot "payload.zip"
$setupOut = Join-Path $artifactsRoot "setup"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code: $LASTEXITCODE"
    }
}

Write-Host "Cleaning desktop build directory..."
if (Test-Path $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $webOut, $appOut, $payloadOut, $setupOut | Out-Null

Write-Host "Publishing web service..."
Invoke-Checked {
    dotnet publish (Join-Path $repoRoot "src\TelegramPanel.Web\TelegramPanel.Web.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $webOut `
        /p:PublishSingleFile=false
}

Write-Host "Publishing desktop shell..."
Invoke-Checked {
    dotnet publish (Join-Path $repoRoot "src\TelegramPanel.Desktop\TelegramPanel.Desktop.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $appOut `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true
}

Write-Host "Assembling installer payload..."
Copy-Item -Path (Join-Path $appOut "*") -Destination $payloadOut -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $payloadOut "web") | Out-Null
Copy-Item -Path (Join-Path $webOut "*") -Destination (Join-Path $payloadOut "web") -Recurse -Force

Write-Host "Compressing payload..."
Compress-Archive -Path (Join-Path $payloadOut "*") -DestinationPath $payloadZip -Force
if (!(Test-Path $payloadZip)) {
    throw "payload.zip was not created: $payloadZip"
}

Write-Host "Publishing installer..."
Invoke-Checked {
    dotnet publish (Join-Path $repoRoot "src\TelegramPanel.Setup\TelegramPanel.Setup.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $setupOut `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:SetupPayloadZip="$payloadZip"
}

$finalSetup = Join-Path $artifactsRoot "TelegramPanel.Setup.exe"
Copy-Item -LiteralPath (Join-Path $setupOut "TelegramPanel.Setup.exe") -Destination $finalSetup -Force

Write-Host ""
Write-Host "Build completed:"
Write-Host "  Installer: $finalSetup"
Write-Host "  Portable payload: $payloadOut"
