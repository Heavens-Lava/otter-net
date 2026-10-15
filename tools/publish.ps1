# Builds the native otter executable for this machine into artifacts\<rid>\.
# Native AOT needs the platform linker; on Windows that is Visual Studio's,
# found through vswhere.exe, which is not always on PATH.
param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
if ((Test-Path $installer) -and -not ($env:PATH -split ';' | Where-Object { $_ -eq $installer })) { $env:PATH = "$installer;$env:PATH" }
& dotnet publish (Join-Path $root 'src\Otter.Cli\Otter.Cli.csproj') -c Release -r $Runtime -o (Join-Path $root "artifacts\$Runtime") --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Get-Item (Join-Path $root "artifacts\$Runtime\otter*") | Where-Object { $_.Extension -in '.exe', '' } | ForEach-Object { "$($_.FullName) ($([int]($_.Length / 1KB)) KB)" }
