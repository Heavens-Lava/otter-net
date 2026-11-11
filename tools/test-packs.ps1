#!/usr/bin/env pwsh
# tools/test-packs.ps1 - Automated verification of all Otter Reusable Source Packs
param(
    [string]$PacksDir = "library/packs/otter-official",
    [string]$OtterCli = "src/Otter.Cli/bin/Debug/net10.0/otter.dll"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PacksDir)) {
    Write-Error "Packs directory not found: $PacksDir"
    exit 1
}

$packDirs = Get-ChildItem -Directory -Path $PacksDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Otter Reusable Source Packs ($($packDirs.Count) packs)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

foreach ($pack in $packDirs) {
    $manifestPath = Join-Path $pack.FullName "otter.pack.json"
    if (-not (Test-Path $manifestPath)) {
        Write-Warning "Skipping $($pack.Name): missing otter.pack.json"
        continue
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    Write-Host "Pack: $($manifest.publisher)/$($manifest.name) v$($manifest.version)" -ForegroundColor Yellow

    # Test suite
    $testFile = Join-Path $pack.FullName "tests/test.ot"
    if (Test-Path $testFile) {
        $output = & dotnet $OtterCli $testFile 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  [PASS] tests/test.ot" -ForegroundColor Green
            $passed++
        } else {
            Write-Host "  [FAIL] tests/test.ot (exit code $LASTEXITCODE)" -ForegroundColor Red
            Write-Host "$output" -ForegroundColor DarkGray
            $failed++
        }
    }

    # Demo
    $demoFile = Join-Path $pack.FullName "examples/demo.ot"
    if (Test-Path $demoFile) {
        $output = & dotnet $OtterCli $demoFile 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  [PASS] examples/demo.ot" -ForegroundColor Green
            $passed++
        } else {
            Write-Host "  [FAIL] examples/demo.ot (exit code $LASTEXITCODE)" -ForegroundColor Red
            Write-Host "$output" -ForegroundColor DarkGray
            $failed++
        }
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Summary: $passed passed, $failed failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
}
exit 0
