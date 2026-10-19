# Builds the native otter executable for this machine into artifacts/<rid>/.
# Native AOT needs the platform linker: on Windows, Visual Studio's (found
# through vswhere.exe, which is not always on PATH); on macOS, Xcode's
# command line tools; on Linux (and a Chromebook's Linux), clang and
# zlib1g-dev. The default runtime is this machine's: win-x64, win-arm64,
# osx-arm64, osx-x64, linux-x64 or linux-arm64.
param([string]$Runtime = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Runtime) {
    $os = if ([System.IO.Path]::DirectorySeparatorChar -eq '\') { 'win' }
          elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) { 'osx' }
          else { 'linux' }
    $Runtime = "$os-" + ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())
}
$installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
if ((Test-Path $installer) -and -not ($env:PATH -split ';' | Where-Object { $_ -eq $installer })) { $env:PATH = "$installer;$env:PATH" }
& dotnet publish (Join-Path $root 'src/Otter.Cli/Otter.Cli.csproj') -c Release -r $Runtime -o (Join-Path $root "artifacts/$Runtime") --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Get-Item (Join-Path $root "artifacts/$Runtime/otter*") | Where-Object { $_.Extension -in '.exe', '' } | ForEach-Object { "$($_.FullName) ($([int]($_.Length / 1KB)) KB)" }
