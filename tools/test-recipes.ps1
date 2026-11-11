#!/usr/bin/env pwsh
# tools/test-recipes.ps1 - Automated test runner for Otter Application Recipes
param(
    [string]$LibraryDir = "library"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Otter Application Recipes (4 recipes, dual-engine)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$OtterCli = Join-Path "src" "Otter.Cli" "bin" "Release" "net10.0" "otter.dll"
if (-not (Test-Path $OtterCli)) {
    $OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
}

$OtterPs1 = Join-Path ".." "otterPS" "otter.ps1"

$recipesDir = Join-Path $LibraryDir "recipes"
$manifests = Get-ChildItem -Path $recipesDir -Recurse -Filter "otter.recipe.json" | Sort-Object FullName

$passed = 0
$failed = 0
$failures = @()

foreach ($m in $manifests) {
    $recipe = Get-Content $m.FullName -Raw | ConvertFrom-Json
    $recipeBase = Split-Path -Parent $m.FullName
    Write-Host "Recipe: $($recipe.id) v$($recipe.version)" -ForegroundColor Yellow

    # 1. Run tests with native C# CLI
    foreach ($testRel in $recipe.tests) {
        $testPath = Join-Path $recipeBase $testRel
        if (Test-Path $testPath) {
            $output = & dotnet $OtterCli run $testPath 2>&1
            $exitCode = $LASTEXITCODE
            if ($exitCode -eq 0) {
                Write-Host "  [C# PASS] $testRel" -ForegroundColor Green
                $passed++
            } else {
                Write-Host "  [C# FAIL] $testRel (exit code $exitCode)" -ForegroundColor Red
                Write-Host "$output" -ForegroundColor DarkGray
                $failed++
                $failures += @{ Recipe = $recipe.id; Engine = "C#"; File = $testRel; Output = $output }
            }

            # 2. Run tests with PowerShell reference engine if available
            if (Test-Path $OtterPs1) {
                $psOutput = & pwsh -File $OtterPs1 run $testPath 2>&1
                $psExit = $LASTEXITCODE
                if ($psExit -eq 0) {
                    Write-Host "  [PS PASS] $testRel" -ForegroundColor Green
                    $passed++
                } else {
                    Write-Host "  [PS FAIL] $testRel (exit code $psExit)" -ForegroundColor Red
                    Write-Host "$psOutput" -ForegroundColor DarkGray
                    $failed++
                    $failures += @{ Recipe = $recipe.id; Engine = "PS"; File = $testRel; Output = $psOutput }
                }
            }
        }
    }

    # 3. Run demo with both engines
    $demoPath = Join-Path $recipeBase "examples/demo.ot"
    if (Test-Path $demoPath) {
        $output = & dotnet $OtterCli run $demoPath 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            Write-Host "  [C# PASS] examples/demo.ot" -ForegroundColor Green
            $passed++
        } else {
            Write-Host "  [C# FAIL] examples/demo.ot (exit code $exitCode)" -ForegroundColor Red
            Write-Host "$output" -ForegroundColor DarkGray
            $failed++
            $failures += @{ Recipe = $recipe.id; Engine = "C#"; File = "examples/demo.ot"; Output = $output }
        }

        if (Test-Path $OtterPs1) {
            $psOutput = & pwsh -File $OtterPs1 run $demoPath 2>&1
            $psExit = $LASTEXITCODE
            if ($psExit -eq 0) {
                Write-Host "  [PS PASS] examples/demo.ot" -ForegroundColor Green
                $passed++
            } else {
                Write-Host "  [PS FAIL] examples/demo.ot (exit code $psExit)" -ForegroundColor Red
                Write-Host "$psOutput" -ForegroundColor DarkGray
                $failed++
                $failures += @{ Recipe = $recipe.id; Engine = "PS"; File = "examples/demo.ot"; Output = $psOutput }
            }
        }
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Summary: $passed passed, $failed failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) { exit 1 } else { exit 0 }
