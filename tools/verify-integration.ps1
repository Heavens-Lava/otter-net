#!/usr/bin/env pwsh
# tools/verify-integration.ps1 - Milestone 5 End-to-End Integration Verification Runner
param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PythonBin = (Get-Command python3 -ErrorAction SilentlyContinue).Source
if (-not $PythonBin) {
    $PythonBin = (Get-Command python -ErrorAction SilentlyContinue).Source
}

if (-not $PythonBin) {
    Write-Error "Python 3 is required to run the automated integration verification suite."
    exit 1
}

& $PythonBin "$ScriptDir/verify-integration.py"
exit $LASTEXITCODE
