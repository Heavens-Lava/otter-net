# tools/install.ps1 - Windows PowerShell Installer for Otter Language CLI & Standard Library
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $HOME ".otter")
)

$ErrorActionPreference = "Stop"
$OtterVersion = "1.0.1"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Installing Otter Programming Language v$OtterVersion" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target directory: $InstallDir"

# 1. Verify .NET Runtime
Write-Host -NoNewline "Checking for .NET 10.0+ runtime... "
$dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "FAILED" -ForegroundColor Red
    Write-Host "Error: .NET runtime not found on PATH." -ForegroundColor Red
    Write-Host "Please install .NET 10 SDK or Runtime from https://dotnet.microsoft.com" -ForegroundColor Yellow
    exit 1
}
$dotnetVer = & dotnet --version
Write-Host "found ($dotnetVer)" -ForegroundColor Green

# 2. Prepare Directories
$BinDir = Join-Path $InstallDir "bin"
$LibDir = Join-Path $InstallDir "library"
$PkgDir = Join-Path $InstallDir "packages"

New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
New-Item -ItemType Directory -Path $LibDir -Force | Out-Null
New-Item -ItemType Directory -Path $PkgDir -Force | Out-Null

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

# 3. Publish and Copy Files
Write-Host "Publishing Otter.Cli Release binaries..." -ForegroundColor Yellow
& dotnet publish (Join-Path $RepoRoot "src/Otter.Cli/Otter.Cli.csproj") -c Release -p:PublishAot=false -p:UseAppHost=true -p:TreatWarningsAsErrors=false -p:NoWarn=NU1903 -o $BinDir | Out-Null

Write-Host "Bundling standard library..." -ForegroundColor Yellow
Copy-Item -Path (Join-Path $RepoRoot "library\*") -Destination $LibDir -Recurse -Force
Copy-Item -Path (Join-Path $RepoRoot "library\*") -Destination (Join-Path $BinDir "library") -Recurse -Force

# 4. Generate Windows Launchers
$cmdLauncher = Join-Path $BinDir "otter.cmd"
@"
@echo off
setlocal
set "BIN_DIR=%~dp0"
for %%I in ("%BIN_DIR%..") do set "OTTER_HOME=%%~fI"

if not defined OTTER_LIBRARY_PATH (
    if exist "%OTTER_HOME%\library" set "OTTER_LIBRARY_PATH=%OTTER_HOME%\library"
    if exist "%BIN_DIR%library" set "OTTER_LIBRARY_PATH=%BIN_DIR%library"
)

dotnet "%BIN_DIR%otter.dll" %*
"@ | Set-Content -Path $cmdLauncher -Encoding ASCII

$psLauncher = Join-Path $BinDir "otter.ps1"
@"
`$BinDir = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$OtterHome = Split-Path -Parent `$BinDir
if (-not `$env:OTTER_LIBRARY_PATH) {
    if (Test-Path (Join-Path `$OtterHome "library")) {
        `$env:OTTER_LIBRARY_PATH = Join-Path `$OtterHome "library"
    } elseif (Test-Path (Join-Path `$BinDir "library")) {
        `$env:OTTER_LIBRARY_PATH = Join-Path `$BinDir "library"
    }
}
& dotnet (Join-Path `$BinDir "otter.dll") `$args
"@ | Set-Content -Path $psLauncher -Encoding ASCII

# 5. Check User PATH
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$BinDir*") {
    Write-Host "Adding $BinDir to User PATH..." -ForegroundColor Yellow
    [Environment]::SetEnvironmentVariable("Path", "$userPath;$BinDir", "User")
}

# 6. Verify Installation
Write-Host ""
Write-Host "Verifying Otter installation..." -ForegroundColor Cyan
& (Join-Path $BinDir "otter.cmd") --version
& (Join-Path $BinDir "otter.cmd") doctor

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Otter v$OtterVersion Installation Succeeded!" -ForegroundColor Green
Write-Host " Binary: $cmdLauncher" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
