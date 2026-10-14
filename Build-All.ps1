# ==============================================================================
# Build Script: Compile, Sign, and Package Logitech T650 Bridge
# Usage: pwsh -File "Build-All.ps1" [-Version "1.1.8"]
# ==============================================================================

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path $scriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building Logitech T650 Bridge" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Build .NET Solution
Write-Host "`n[1/2] Building T650Bridge.sln..." -ForegroundColor Yellow
$slnPath = Join-Path $repoRoot "T650Bridge.sln"
dotnet build "$slnPath" -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Solution build failed!" -ForegroundColor Red
    Exit 1
}

# 2. Package WiX Installer
Write-Host "`n[2/2] Packaging MSI Installer..." -ForegroundColor Yellow
$installerScript = Join-Path $repoRoot "Installer\Build-Bridge-Installer.ps1"
$passArgs = @()
if ($Version) {
    $passArgs += @("-Version", $Version)
}

& pwsh -File "$installerScript" @passArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Installer build failed!" -ForegroundColor Red
    Exit 1
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Logitech T650 Bridge Build & Packaging Complete!" -ForegroundColor Green
Write-Host " Artifacts located in: $(Join-Path $repoRoot 'Dist')" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
