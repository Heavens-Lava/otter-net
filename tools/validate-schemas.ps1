#!/usr/bin/env pwsh
# tools/validate-schemas.ps1 - Automated schema validation for manifests and catalogs
param(
    [string]$LibraryDir = "library"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Validating Otter Reusable Library JSON Schemas" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$passed = 0
$failed = 0

# 1. Validate Schema files are valid JSON
$schemas = Get-ChildItem -Path (Join-Path $LibraryDir "schema") -Filter "*.json"
foreach ($s in $schemas) {
    try {
        $null = Get-Content $s.FullName -Raw | ConvertFrom-Json
        Write-Host "  [PASS] Schema: $($s.Name)" -ForegroundColor Green
        $passed++
    } catch {
        Write-Host "  [FAIL] Schema: $($s.Name) - $($_.Exception.Message)" -ForegroundColor Red
        $failed++
    }
}

# 2. Validate Snippet Catalog
$catalogFile = Join-Path $LibraryDir "snippets/catalog.json"
try {
    $catalog = Get-Content $catalogFile -Raw | ConvertFrom-Json
    if (-not $catalog.snippets -or $catalog.snippets.Count -eq 0) {
        throw "Catalog contains no snippets."
    }
    # Check required fields
    foreach ($snip in $catalog.snippets) {
        if (-not $snip.id -or -not $snip.title -or -not $snip.template -or -not $snip.example) {
            throw "Snippet missing required fields: $($snip.id)"
        }
    }
    Write-Host "  [PASS] Catalog: catalog.json ($($catalog.snippets.Count) snippets verified)" -ForegroundColor Green
    $passed++
} catch {
    Write-Host "  [FAIL] Catalog: catalog.json - $($_.Exception.Message)" -ForegroundColor Red
    $failed++
}

# 3. Validate all Pack Manifests
$manifests = Get-ChildItem -Path (Join-Path $LibraryDir "packs") -Recurse -Filter "otter.pack.json"
foreach ($m in $manifests) {
    try {
        $pack = Get-Content $m.FullName -Raw | ConvertFrom-Json
        if (-not $pack.name -or -not $pack.publisher -or -not $pack.version -or -not $pack.entry) {
            throw "Manifest missing required fields."
        }
        if ($pack.entry.Contains("..") -or $pack.entry.StartsWith("/") -or $pack.entry.StartsWith("\")) {
            throw "Manifest entry point contains illegal traversal characters."
        }
        $entryFullPath = Join-Path (Split-Path -Parent $m.FullName) $pack.entry
        if (-not (Test-Path $entryFullPath)) {
            throw "Manifest entry file does not exist on disk: $($pack.entry)"
        }
        Write-Host "  [PASS] Manifest: $($pack.publisher)/$($pack.name) v$($pack.version)" -ForegroundColor Green
        $passed++
    } catch {
        Write-Host "  [FAIL] Manifest: $($m.FullName) - $($_.Exception.Message)" -ForegroundColor Red
        $failed++
    }
}

# 4. Validate all Blueprint Manifests
$bpDir = Join-Path $LibraryDir "blueprints"
if (Test-Path $bpDir) {
    $bpManifests = Get-ChildItem -Path $bpDir -Recurse -Filter "otter.blueprint.json"
    foreach ($bpm in $bpManifests) {
        try {
            $bp = Get-Content $bpm.FullName -Raw | ConvertFrom-Json
            if (-not $bp.id -or -not $bp.version -or -not $bp.title -or -not $bp.nodes -or -not $bp.edges -or -not $bp.generatedFiles) {
                throw "Blueprint manifest missing required fields."
            }
            # Check unique node IDs
            $nodeIds = @{}
            foreach ($n in $bp.nodes) {
                if ($nodeIds.ContainsKey($n.id)) {
                    throw "Duplicate node ID: $($n.id)"
                }
                $nodeIds[$n.id] = $true
            }
            # Check edge endpoints
            foreach ($e in $bp.edges) {
                $fromNode = $e.from.Split('.')[0]
                $toNode = $e.to.Split('.')[0]
                if (-not $nodeIds.ContainsKey($fromNode)) {
                    throw "Edge references unknown from node: $fromNode"
                }
                if (-not $nodeIds.ContainsKey($toNode)) {
                    throw "Edge references unknown to node: $toNode"
                }
            }
            # Check templates
            $bpBase = Split-Path -Parent $bpm.FullName
            foreach ($gf in $bp.generatedFiles) {
                if ($gf.template.Contains("..") -or $gf.path.Contains("..")) {
                    throw "Illegal path traversal in generatedFiles."
                }
                $tmplPath = Join-Path $bpBase $gf.template
                if (-not (Test-Path $tmplPath)) {
                    throw "Blueprint template missing on disk: $($gf.template)"
                }
            }
            Write-Host "  [PASS] Blueprint: $($bp.id) v$($bp.version)" -ForegroundColor Green
            $passed++
        } catch {
            Write-Host "  [FAIL] Blueprint: $($bpm.FullName) - $($_.Exception.Message)" -ForegroundColor Red
            $failed++
        }
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Summary: $passed passed, $failed failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failed -gt 0) { exit 1 } else { exit 0 }
