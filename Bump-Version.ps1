# ==============================================================================
# Bump Version Script: Logitech T650 Bridge
# Usage: pwsh -File "Bump-Version.ps1" -NewVersion "1.1.7"
# ==============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$NewVersion
)

$ErrorActionPreference = "Stop"

if ($NewVersion -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "[Error] Version must follow Semantic Versioning 'MAJOR.MINOR.PATCH' (e.g. 1.1.7)!" -ForegroundColor Red
    Exit 1
}

$fourPartVersion = "$NewVersion.0"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path $scriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Bumping Logitech T650 Bridge -> $NewVersion ($fourPartVersion)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Update src\T650Bridge.csproj
$csprojPath = Join-Path $repoRoot "src\T650Bridge.csproj"
if (Test-Path $csprojPath) {
    Write-Host "`n[1/3] Updating $csprojPath..." -ForegroundColor Yellow
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, '<Version>[\d\.]+</Version>', "<Version>$NewVersion</Version>")
    $csprojContent = [regex]::Replace($csprojContent, '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$fourPartVersion</AssemblyVersion>")
    $csprojContent = [regex]::Replace($csprojContent, '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$fourPartVersion</FileVersion>")
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding utf8
    Write-Host "  -> Updated to Version $NewVersion ($fourPartVersion)" -ForegroundColor Green
}

# 2. Update Installer\T650Bridge.wxs
$wxsPath = Join-Path $repoRoot "Installer\T650Bridge.wxs"
if (Test-Path $wxsPath) {
    Write-Host "`n[2/3] Updating $wxsPath..." -ForegroundColor Yellow
    $wxsContent = Get-Content $wxsPath -Raw
    $wxsContent = [regex]::Replace($wxsContent, '(?<!Installer)Version="[\d\.]+"', "Version=""$fourPartVersion""")
    Set-Content -Path $wxsPath -Value $wxsContent -Encoding utf8
    Write-Host "  -> Updated to Version $fourPartVersion" -ForegroundColor Green
}

# 3. Update Installer\T650Bridge-Admin.wxs
$adminWxsPath = Join-Path $repoRoot "Installer\T650Bridge-Admin.wxs"
if (Test-Path $adminWxsPath) {
    Write-Host "`n[3/3] Updating $adminWxsPath..." -ForegroundColor Yellow
    $adminWxsContent = Get-Content $adminWxsPath -Raw
    $adminWxsContent = [regex]::Replace($adminWxsContent, '(?<!Installer)Version="[\d\.]+"', "Version=""$fourPartVersion""")
    Set-Content -Path $adminWxsPath -Value $adminWxsContent -Encoding utf8
    Write-Host "  -> Updated to Version $fourPartVersion" -ForegroundColor Green
}

# 4. Rebuild & Re-package
Write-Host "`n>>> Rebuilding and packaging Logitech T650 Bridge v$NewVersion..." -ForegroundColor Cyan
$buildScript = Join-Path $repoRoot "Build-All.ps1"
& pwsh -File "$buildScript" -Version "$NewVersion"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Build-All failed!" -ForegroundColor Red
    Exit 1
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Logitech T650 Bridge version bump to $NewVersion complete!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
