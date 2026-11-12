#!/usr/bin/env pwsh
# tools/test-dap.ps1 - Automated DAP Protocol Conformance Test Runner
param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PythonBin = (Get-Command python3 -ErrorAction SilentlyContinue).Source
if (-not $PythonBin) {
    $PythonBin = (Get-Command python -ErrorAction SilentlyContinue).Source
}

if (-not $PythonBin) {
    Write-Error "Python 3 is required to run the automated DAP protocol test suite."
    exit 1
}

& $PythonBin "$ScriptDir/test-dap.py"
exit $LASTEXITCODE
