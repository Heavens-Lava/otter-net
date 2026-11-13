# tools/test-mutations.ps1 - Cross-platform wrapper for mutation testing suite
[CmdletBinding()]
param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Python = Get-Command python3 -ErrorAction SilentlyContinue
if (-not $Python) {
    $Python = Get-Command python -ErrorAction SilentlyContinue
}
if (-not $Python) {
    Write-Error "Python 3 is required to run test-mutations.py."
    exit 1
}

& $Python.Source "$ScriptDir/test-mutations.py"
exit $LASTEXITCODE
