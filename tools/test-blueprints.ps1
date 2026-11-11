#!/usr/bin/env pwsh
# tools/test-blueprints.ps1 - Automated test runner for Otter Blueprints
param(
    [string]$LibraryDir = "library"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Otter Reusable Blueprints (4 blueprints)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
if (-not (Test-Path $OtterCli)) {
    Write-Host "Building Otter.Cli..." -ForegroundColor Yellow
    dotnet build src/Otter.Cli/Otter.Cli.csproj -p:TreatWarningsAsErrors=false | Out-Null
}

$bpDir = Join-Path $LibraryDir "blueprints"
$manifests = Get-ChildItem -Path $bpDir -Recurse -Filter "otter.blueprint.json" | Sort-Object FullName

$passed = 0
$failed = 0
$failures = @()

foreach ($m in $manifests) {
    $bp = Get-Content $m.FullName -Raw | ConvertFrom-Json
    $bpBase = Split-Path -Parent $m.FullName
    Write-Host "Blueprint: $($bp.id) v$($bp.version)" -ForegroundColor Yellow

    # Run tests
    foreach ($testRel in $bp.tests) {
        $testPath = Join-Path $bpBase $testRel
        if (Test-Path $testPath) {
            $output = & dotnet $OtterCli $testPath 2>&1
            $exitCode = $LASTEXITCODE
            if ($exitCode -eq 0) {
                Write-Host "  [PASS] $testRel" -ForegroundColor Green
                $passed++
            } else {
                Write-Host "  [FAIL] $testRel (exit code $exitCode)" -ForegroundColor Red
                Write-Host "$output" -ForegroundColor DarkGray
                $failed++
                $failures += @{ Blueprint = $bp.id; File = $testRel; Output = $output }
            }
        }
    }

    # Run demo if present
    $demoPath = Join-Path $bpBase "examples/demo.ot"
    if (Test-Path $demoPath) {
        $output = & dotnet $OtterCli $demoPath 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            Write-Host "  [PASS] examples/demo.ot" -ForegroundColor Green
            $passed++
        } else {
            Write-Host "  [FAIL] examples/demo.ot (exit code $exitCode)" -ForegroundColor Red
            Write-Host "$output" -ForegroundColor DarkGray
            $failed++
            $failures += @{ Blueprint = $bp.id; File = "examples/demo.ot"; Output = $output }
        }
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Summary: $passed passed, $failed failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) { exit 1 } else { exit 0 }
