#!/usr/bin/env pwsh
# tools/test-snippets.ps1 - Automated verification of all Otter Reusable Snippets
param(
    [string]$CatalogPath = "library/snippets/catalog.json",
    [string]$OtterCli = "src/Otter.Cli/bin/Debug/net10.0/otter.dll"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $CatalogPath)) {
    Write-Error "Catalog file not found: $CatalogPath"
    exit 1
}

$raw = Get-Content $CatalogPath -Raw | ConvertFrom-Json
$snippets = $raw.snippets

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Otter Reusable Snippet Catalog ($($snippets.Count) snippets)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$tempDir = Join-Path "artifacts" "test-snippets-run"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Path $tempDir | Out-Null

$passed = 0
$failed = 0
$failures = @()

foreach ($snippet in $snippets) {
    $id = $snippet.id
    $title = $snippet.title
    $code = $snippet.example
    
    # If it is a web server snippet, start as an Otter Web project, send a test request, verify, and terminate
    if ($id -eq "web-http-json-response") {
        $serverAppDir = Join-Path "artifacts" "test-server-snippet-app"
        if (Test-Path $serverAppDir) { Remove-Item -Recurse -Force $serverAppDir }
        New-Item -ItemType Directory -Path $serverAppDir | Out-Null
        @{
            name = "test-server-snippet"
            version = "1.0.0"
            target = "server"
            entryPoint = "main.ot"
            web = @{ runtime = "otter-web-0.1" }
        } | ConvertTo-Json | Set-Content (Join-Path $serverAppDir "otter.json") -Encoding utf8NoBOM
        Set-Content (Join-Path $serverAppDir "main.ot") -Value $code -Encoding utf8NoBOM

        $proc = Start-Process -FilePath "dotnet" -ArgumentList "$OtterCli run $serverAppDir" -PassThru -NoNewWindow
        Start-Sleep -Seconds 2
        try {
            $raw = curl -s http://127.0.0.1:18090/health
            $resp = $raw | ConvertFrom-Json
            if ($resp.status -eq "healthy") {
                Write-Host "  [PASS] $id - $title (HTTP live verified)" -ForegroundColor Green
                $passed++
            } else {
                Write-Host "  [FAIL] $id - $title (invalid payload)" -ForegroundColor Red
                $failed++
            }
        } catch {
            Write-Host "  [FAIL] $id - $title (HTTP request failed)" -ForegroundColor Red
            $failed++
        } finally {
            if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
            Remove-Item -Recurse -Force $serverAppDir
        }
        continue
    }

    $otFile = Join-Path $tempDir "$id.ot"
    Set-Content -Path $otFile -Value $code -Encoding utf8NoBOM

    $output = & dotnet $OtterCli $otFile 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host "  [PASS] $id - $title" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "  [FAIL] $id - $title (exit code $exitCode)" -ForegroundColor Red
        Write-Host "$output" -ForegroundColor DarkGray
        $failed++
        $failures += @{ Id = $id; Title = $title; Output = $output }
    }
}

if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Summary: $passed passed, $failed failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
}
exit 0
