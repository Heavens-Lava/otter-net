#!/usr/bin/env pwsh
# tools/otter-library.ps1 - Otter Reusable Library CLI tool (RFC Phase 1)
param(
    [Parameter(Position = 0)][string]$Action = "help",
    [Parameter(Position = 1)][string]$Query = "",
    [Parameter(Position = 2)][string]$Target = "",
    [string]$CatalogPath = "library/snippets/catalog.json",
    [string]$PacksDir = "library/packs/otter-official"
)

$ErrorActionPreference = "Stop"

function Show-Help {
    Write-Host "Otter Reusable Library (RFC Phase 1)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  pwsh tools/otter-library.ps1 search <query>          Search snippets and packs (natural language or keywords)"
    Write-Host "  pwsh tools/otter-library.ps1 list [category]         List all curated snippets"
    Write-Host "  pwsh tools/otter-library.ps1 show <id>               Display full details and template for a snippet"
    Write-Host "  pwsh tools/otter-library.ps1 insert <id> [file]      Insert snippet code into target file or stdout"
    Write-Host "  pwsh tools/otter-library.ps1 install <pack> [dir]    Install pack locally into .otter/packages and update otter.lock"
    Write-Host "  pwsh tools/otter-library.ps1 verify [dir]            Verify integrity of installed packages against otter.lock"
    Write-Host "  pwsh tools/otter-library.ps1 uninstall <pack> [dir]  Remove installed pack and prune otter.lock entry"
    Write-Host "  pwsh tools/otter-library.ps1 blueprint-search <q>    Search blueprints by keywords"
    Write-Host "  pwsh tools/otter-library.ps1 blueprint-inspect <id>  Inspect blueprint manifest, nodes, and edges"
    Write-Host "  pwsh tools/otter-library.ps1 blueprint-explain <id>  Show 3-level explanation for blueprint"
    Write-Host "  pwsh tools/otter-library.ps1 blueprint-plan <id>     Generate dry-run apply plan"
    Write-Host "  pwsh tools/otter-library.ps1 blueprint-apply <id>    Install dependencies and generate project source"
    Write-Host "  pwsh tools/otter-library.ps1 test                    Run full snippet, pack, and blueprint test suites"
    Write-Host ""
}

function Load-Catalog {
    if (-not (Test-Path $CatalogPath)) {
        throw "Snippet catalog not found at $CatalogPath"
    }
    return (Get-Content $CatalogPath -Raw | ConvertFrom-Json).snippets
}

function Assert-SafePath([string]$path, [string]$label) {
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "Security Error: $label path cannot be empty."
    }
    if ($path.Contains("..") -or $path.StartsWith("/") -or $path.StartsWith("\") -or $path.Contains(":") -or $path.Contains("~")) {
        throw "Security Error: $label path '$path' contains illegal traversal tokens ('..', absolute root, or colon)."
    }
}

function Load-Packs {
    $packs = @()
    if (Test-Path $PacksDir) {
        foreach ($d in (Get-ChildItem -Directory $PacksDir)) {
            $mPath = Join-Path $d.FullName "otter.pack.json"
            if (Test-Path $mPath) {
                $m = Get-Content $mPath -Raw | ConvertFrom-Json
                # Validate traversal safety
                Assert-SafePath $m.name "Package name"
                Assert-SafePath $m.publisher "Publisher name"
                Assert-SafePath $m.entry "Package entry point"
                if ($m.examples) {
                    foreach ($ex in $m.examples) { Assert-SafePath $ex "Example path" }
                }

                $m | Add-Member -NotePropertyName "Dir" -NotePropertyValue $d.FullName -Force
                $packs += $m
            }
        }
    }
    return $packs
}

switch ($Action.ToLowerInvariant()) {
    "search" {
        if ([string]::IsNullOrWhiteSpace($Query)) {
            Write-Host "Please specify a search query (e.g. 'for loop', 'enemy damage', 'clamp', 'sql query')." -ForegroundColor Yellow
            exit 1
        }
        $snippets = Load-Catalog
        $packs = Load-Packs

        $terms = $Query.ToLowerInvariant().Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)

        Write-Host "Search Results for '$Query':" -ForegroundColor Cyan
        Write-Host "----------------------------------------------------------"

        $matchCount = 0

        # Search Packs
        foreach ($p in $packs) {
            $score = 0
            $searchText = "$($p.name) $($p.description) $($p.tags -join ' ')".ToLowerInvariant()
            foreach ($t in $terms) {
                if ($p.name.ToLowerInvariant() -eq $t) { $score += 10 }
                elseif ($searchText.Contains($t)) { $score += 2 }
            }
            if ($score -gt 0) {
                $matchCount++
                Write-Host "  [PACK] $($p.publisher)/$($p.name) v$($p.version)" -ForegroundColor Yellow
                Write-Host "         $($p.description)"
                Write-Host "         License: $($p.license) | Platforms: $($p.platforms -join ', ') | Capabilities: $(if ($p.capabilities.Count) { $p.capabilities -join ', ' } else { 'none' })" -ForegroundColor DarkGray
                Write-Host "         Install: pwsh tools/otter-library.ps1 install $($p.name)" -ForegroundColor Green
                Write-Host ""
            }
        }

        # Search Snippets
        foreach ($s in $snippets) {
            $score = 0
            $searchText = "$($s.id) $($s.title) $($s.category) $($s.description) $($s.tags -join ' ')".ToLowerInvariant()
            foreach ($t in $terms) {
                if ($s.id.ToLowerInvariant() -eq $t) { $score += 10 }
                elseif ($s.title.ToLowerInvariant().Contains($t)) { $score += 5 }
                elseif ($searchText.Contains($t)) { $score += 2 }
            }
            if ($score -gt 0) {
                $matchCount++
                Write-Host "  [SNIPPET] $($s.title) (id: $($s.id))" -ForegroundColor Cyan
                Write-Host "            Category: $($s.category) | Capabilities: $(if ($s.capabilities.Count) { $s.capabilities -join ', ' } else { 'none' })" -ForegroundColor DarkGray
                Write-Host "            $($s.description)"
                Write-Host "            Insert: pwsh tools/otter-library.ps1 insert $($s.id)" -ForegroundColor Green
                Write-Host ""
            }
        }

        if ($matchCount -eq 0) {
            Write-Host "No snippets or packs matched '$Query'." -ForegroundColor DarkYellow
        }
    }

    "list" {
        $snippets = Load-Catalog
        $categories = $snippets | Group-Object Category | Sort-Object Name

        Write-Host "Otter Curated Reusable Snippets ($($snippets.Count) total):" -ForegroundColor Cyan
        Write-Host "=========================================================="
        foreach ($cat in $categories) {
            if ($Query -and -not ($cat.Name.ToLowerInvariant().Contains($Query.ToLowerInvariant()))) {
                continue
            }
            Write-Host " [$($cat.Name)]" -ForegroundColor Yellow
            foreach ($s in $cat.Group) {
                Write-Host "   - $($s.id.PadRight(24)) $($s.title)"
            }
            Write-Host ""
        }
    }

    "show" {
        if (-not $Query) {
            Write-Host "Usage: pwsh tools/otter-library.ps1 show <snippet-id>" -ForegroundColor Red
            exit 1
        }
        $snippets = Load-Catalog
        $found = $snippets | Where-Object { $_.id -eq $Query }
        if (-not $found) {
            Write-Host "Snippet '$Query' not found." -ForegroundColor Red
            exit 1
        }

        Write-Host "==========================================================" -ForegroundColor Cyan
        Write-Host " $($found.title) [$($found.id)]" -ForegroundColor Yellow
        Write-Host "==========================================================" -ForegroundColor Cyan
        Write-Host "Category:     $($found.category)"
        Write-Host "Description:  $($found.description)"
        Write-Host "Tags:         $($found.tags -join ', ')"
        Write-Host "Capabilities: $(if ($found.capabilities.Count) { $found.capabilities -join ', ' } else { 'none' })"
        Write-Host "Platforms:    $($found.platforms -join ', ')"
        Write-Host "Attribution:  $($found.attribution)"
        Write-Host ""
        Write-Host "--- Template ---" -ForegroundColor Green
        Write-Host $found.template
        Write-Host ""
        Write-Host "--- Executable Example ---" -ForegroundColor Green
        Write-Host $found.example
        Write-Host "==========================================================" -ForegroundColor Cyan
    }

    "insert" {
        if (-not $Query) {
            Write-Host "Usage: pwsh tools/otter-library.ps1 insert <snippet-id> [target-file.ot]" -ForegroundColor Red
            exit 1
        }
        $snippets = Load-Catalog
        $found = $snippets | Where-Object { $_.id -eq $Query }
        if (-not $found) {
            Write-Host "Snippet '$Query' not found." -ForegroundColor Red
            exit 1
        }

        $code = $found.template
        if ($Target) {
            Add-Content -Path $Target -Value "`n# --- Snippet: $($found.title) ---`n$code`n" -Encoding utf8NoBOM
            Write-Host "Inserted snippet '$($found.id)' into $Target" -ForegroundColor Green
        } else {
            Write-Output $code
        }
    }

    "install" {
        if (-not $Query) {
            Write-Host "Usage: pwsh tools/otter-library.ps1 install <pack-name> [project-dir]" -ForegroundColor Red
            exit 1
        }
        $packs = Load-Packs
        $found = $packs | Where-Object { $_.name -eq $Query -or "$($_.publisher)/$($_.name)" -eq $Query }
        if (-not $found) {
            Write-Host "Pack '$Query' not found in local catalog." -ForegroundColor Red
            exit 1
        }

        $projectDir = if ($Target) { $Target } else { "." }
        $installTarget = Join-Path $projectDir ".otter/packages/$($found.publisher)/$($found.name)/$($found.version)"
        $entrySource = Join-Path $found.Dir $found.entry
        $sourceDigest = (Get-FileHash -Path $entrySource -Algorithm SHA256).Hash

        # Check existing lockfile for idempotency
        $lockPath = Join-Path $projectDir "otter.lock"
        $lockData = @{
            lockfileVersion = 1
            packages = @{}
        }
        if (Test-Path $lockPath) {
            try { $lockData = Get-Content $lockPath -Raw | ConvertFrom-Json -AsHashtable } catch { }
        }
        if (-not $lockData["packages"]) { $lockData["packages"] = @{} }

        $pkgKey = "$($found.publisher)/$($found.name)"
        if ($lockData["packages"].ContainsKey($pkgKey) -and (Test-Path $installTarget)) {
            $existing = $lockData["packages"][$pkgKey]
            if ($existing["version"] -eq $found.version -and $existing["sha256"] -eq $sourceDigest) {
                Write-Host "Package '$pkgKey' v$($found.version) is already installed and matches checksum. Up-to-date." -ForegroundColor Green
                return
            }
        }

        Write-Host "Installing $($found.publisher)/$($found.name) v$($found.version) into $installTarget..." -ForegroundColor Cyan
        
        if (Test-Path $installTarget) { Remove-Item -Recurse -Force $installTarget }
        New-Item -ItemType Directory -Path $installTarget -Force | Out-Null
        Copy-Item -Path "$($found.Dir)/*" -Destination $installTarget -Recurse -Force

        # Entry digest verification
        $entryFile = Join-Path $installTarget $found.entry
        $digest = (Get-FileHash -Path $entryFile -Algorithm SHA256).Hash

        $lockData["packages"][$pkgKey] = @{
            version = $found.version
            entry = $found.entry
            sha256 = $digest
            installedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
        }

        $lockData | ConvertTo-Json -Depth 5 | Set-Content $lockPath -Encoding utf8NoBOM
        Write-Host "Package installed successfully. Updated $lockPath." -ForegroundColor Green
        Write-Host "To use in your Otter source: use `".otter/packages/$($found.publisher)/$($found.name)/$($found.version)/$($found.entry)`"" -ForegroundColor Yellow
    }

    "verify" {
        $projectDir = if ($Query) { $Query } else { "." }
        $lockPath = Join-Path $projectDir "otter.lock"
        if (-not (Test-Path $lockPath)) {
            Write-Host "No otter.lock found in $projectDir." -ForegroundColor Yellow
            exit 0
        }

        $lockData = Get-Content $lockPath -Raw | ConvertFrom-Json -AsHashtable
        Write-Host "Verifying package integrity from $lockPath..." -ForegroundColor Cyan
        $allPassed = $true

        if ($lockData["packages"]) {
            foreach ($pkgName in $lockData["packages"].Keys) {
                $info = $lockData["packages"][$pkgName]
                $entryPath = Join-Path $projectDir ".otter/packages/$pkgName/$($info["version"])/$($info["entry"])"

                if (-not (Test-Path $entryPath)) {
                    Write-Host "  [MISSING] $pkgName ($entryPath not found)" -ForegroundColor Red
                    $allPassed = $false
                    continue
                }

                $currentHash = (Get-FileHash -Path $entryPath -Algorithm SHA256).Hash
                if ($currentHash -eq $info["sha256"]) {
                    Write-Host "  [VALID]   $pkgName v$($info["version"])" -ForegroundColor Green
                } else {
                    Write-Host "  [CORRUPT] $pkgName (hash mismatch: expected $($info["sha256"]), got $currentHash)" -ForegroundColor Red
                    $allPassed = $false
                }
            }
        }

        if (-not $allPassed) {
            Write-Host "Integrity verification failed!" -ForegroundColor Red
            exit 1
        } else {
            Write-Host "All installed packages verified successfully." -ForegroundColor Green
        }
    }

    "uninstall" {
        if (-not $Query) {
            Write-Host "Usage: pwsh tools/otter-library.ps1 uninstall <pack-name> [project-dir]" -ForegroundColor Red
            exit 1
        }
        $projectDir = if ($Target) { $Target } else { "." }
        $lockPath = Join-Path $projectDir "otter.lock"

        if (-not (Test-Path $lockPath)) {
            Write-Host "No otter.lock found in $projectDir." -ForegroundColor Red
            exit 1
        }

        $lockData = Get-Content $lockPath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $lockData["packages"]) {
            Write-Host "No packages listed in $lockPath." -ForegroundColor Yellow
            exit 0
        }

        $matchKey = $null
        foreach ($k in $lockData["packages"].Keys) {
            if ($k -eq $Query -or $k.EndsWith("/$Query")) {
                $matchKey = $k
                break
            }
        }

        if (-not $matchKey) {
            Write-Host "Package '$Query' is not installed in $projectDir." -ForegroundColor Red
            exit 1
        }

        $pkgFullName = $matchKey
        $pkgDir = Join-Path $projectDir ".otter/packages/$pkgFullName"

        if (Test-Path $pkgDir) {
            Remove-Item -Recurse -Force $pkgDir
            Write-Host "Removed package directory $pkgDir." -ForegroundColor Cyan
        }

        # Remove from lockfile
        $lockData["packages"].Remove($pkgFullName)
        $lockData | ConvertTo-Json -Depth 5 | Set-Content $lockPath -Encoding utf8NoBOM
        Write-Host "Uninstalled $pkgFullName and updated $lockPath." -ForegroundColor Green
    }

    "blueprint-search" {
        $OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
        & dotnet $OtterCli blueprint search $Query
    }

    "blueprint-inspect" {
        $OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
        & dotnet $OtterCli blueprint inspect $Query
    }

    "blueprint-explain" {
        $OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
        $lvl = if ($Target) { $Target } else { "intermediate" }
        & dotnet $OtterCli blueprint explain $Query --level $lvl
    }

    "blueprint-plan" {
        $OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
        $p = if ($Target) { $Target } else { "." }
        & dotnet $OtterCli blueprint plan $Query --project $p
    }

    "blueprint-apply" {
        $OtterCli = Join-Path "src" "Otter.Cli" "bin" "Debug" "net10.0" "otter.dll"
        $p = if ($Target) { $Target } else { "." }
        & dotnet $OtterCli blueprint apply $Query --project $p
    }

    "test" {
        Write-Host "Running all validation suites..." -ForegroundColor Cyan
        & pwsh tools/test-snippets.ps1
        & pwsh tools/test-packs.ps1
        & pwsh tools/test-blueprints.ps1
    }

    default {
        Show-Help
    }
}
