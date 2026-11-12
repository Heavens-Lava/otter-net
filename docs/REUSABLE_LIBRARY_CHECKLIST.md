# Otter Reusable Library & Ultimate Snippets: Master Implementation Checklist

This checklist operationalizes [OTTER_REUSABLE_LIBRARY_RFC.md](../OTTER_REUSABLE_LIBRARY_RFC.md), the [Otter Blueprints Specification](../library/blueprints/), and the [Phase 3 Native Recipes & Global Discovery Specification](AI_COMPOSITION_PROTOCOL.md) into actionable, verifiable tasks across every implementation phase, ensuring strict compliance with Otter 1.0.1 grammar, dual-engine parity, CLI-first independence, and Studio IDE integration.

---

## Progress Overview

| Phase | Description | Status | Progress |
|---|---|:---:|:---:|
| **Phase 0** | Audit & Baseline Architecture | Completed | 100% |
| **Phase 1A** | Schemas & Manifest Specifications | Completed | 100% |
| **Phase 1B** | Curated Snippet Catalog & Test Runner (38 Snippets) | Completed | 100% |
| **Phase 1C** | Core Official Source Packs (5 Packs, Expanded Capabilities) | Completed | 100% |
| **Phase 1D** | Local Package Manager CLI & Lockfile Engine (PowerShell & Native C#) | Completed | 100% |
| **Phase 1E** | Otter Studio IDE Integration (Library Explorer & Blueprint Workspace) | Completed | 100% |
| **Phase 1F** | End-to-End Parity & Conformance Testing | Completed | 100% |
| **Phase 2A** | Otter Blueprints (Visual + Code, CLI-First, 4 Starter Blueprints) | Completed | 100% |
| **Phase 2B** | Online Registry & Website Specification (otterprogramming.com) | Completed | 100% |
| **Phase 3A** | Global Library Discovery (Multi-Tier Precedence & Standalone Execution) | Completed | 100% |
| **Phase 3B** | Native Recipe Engine (Search, Inspect, Explain, Plan, Apply, Verify) | Completed | 100% |
| **Phase 3C** | Intelligent Feature Discovery (Natural Language Intent & Explanations) | Completed | 100% |
| **Phase 3D** | Complete Applications (Contact Manager, Task Tracker, Web Portal, RPG) | Completed | 100% |
| **Phase 3E** | AI Composition Protocol & Educational Programming Experience | Completed | 100% |
| **Phase 3F** | Full End-to-End Verification & Dual-Engine Parity | Completed | 100% |

---

## Phase 0: Audit & Baseline Architecture

- [x] **0.1 Review canonical language constraints**
  - [x] Confirm no dot member access (`.` remains optional block terminator).
  - [x] Confirm property access via `field of object`.
  - [x] Confirm custom types via `a Type has` and instantiation via `x is a Type`.
  - [x] Confirm functions via `to name arg1 arg2 ... .`.
  - [x] Verify reserved words table in `src/Otter.Language/ReservedWords.cs`.
- [x] **0.2 Audit module import resolution in C# engine**
  - [x] Review `src/Otter.Cli/Modules.cs` (`ResolvedProgram`, `ResolveFile`, cycle detection).
  - [x] Verify case-sensitive on-disk path matching (M1) and origin remapping.
- [x] **0.3 Audit Studio IDE package management subsystem**
  - [x] Inspect `otterPS/otter-studio/js/project/package-module-engine.js` (LockfileManager, ModuleResolutionEngine, OfflineCache).
  - [x] Audit `otterPS/otter-studio/js/language/otter-language-service.js` for completion and snippet injection hooks.
  - [x] Audit `otterPS/otter-studio/js/components/editor.js` for snippet insertion commands.
  - [x] Document verified working features vs synthetic fallback rejection in Section 15.

---

## Phase 1: Local Snippets & Source Packs (No New Grammar)

### 1A. Schemas & Manifests
- [x] **1A.1 Package Manifest Schema (`library/schema/pack.schema.json`)**
  - [x] Define `schemaVersion: 1`, `name`, `publisher`, `version`, `kind`.
  - [x] Define `entry` (confined to `src/`), `license`, `platforms`, `capabilities`, `dependencies`.
  - [x] Restrict path traversal (`..` forbidden) and symlinks.
- [x] **1A.2 Snippet Definition Schema (`library/schema/snippet.schema.json`)**
  - [x] Define `id`, `title`, `kind`, `category`, `description`, `tags`.
  - [x] Define `capabilities`, `platforms`, `template`, `example`, `attribution`.
- [x] **1A.3 Blueprint Schema (`library/schema/blueprint.schema.json`)**
  - [x] Define `schemaVersion: 1`, `id`, `version`, `title`, `targets`, `requiresOtter`.
  - [x] Define typed `nodes` (inputs/outputs/capability) and `edges` (`from`/`to`).
  - [x] Define `generatedFiles`, `configuration`, `tests`, and 3-level `explanations`.
- [x] **1A.4 Recipe Schema (`library/schema/recipe.schema.json`)**
  - [x] Define `schemaVersion: 1`, `id`, `version`, `title`, `summary`, `target`, `packs`, `blueprints`, `configuration`, `steps`, `generatedFiles`, `tests`, `explanations`.
- [x] **1A.5 Schema Validation Suite (`tools/validate-schemas.ps1`)**
  - [x] JSON syntax validation across all manifests and catalogs.

### 1B. Curated Snippet Catalog (38 Canonical Snippets)
- [x] **1B.1 Canonical Catalog (`library/snippets/catalog.json`)**
  - [x] Control Flow: `cf-for-each`, `cf-count-range`, `cf-while-loop`, `cf-if-otherwise`, `cf-try-otherwise`.
  - [x] Text & Strings: `text-slugify`, `text-pad-left`, `text-split-join`, `text-replace-all`.
  - [x] Math & Numbers: `math-clamp`, `math-lerp`, `math-map-range`, `math-calc-average`.
  - [x] Collections & Lists: `list-contains-check`, `list-deduplicate`, `list-filter-items`, `list-sort-reverse`.
  - [x] Files & I/O: `file-read-safe`, `file-write-text`, `file-json-storage`, `file-csv-parse`, `file-append-log`.
  - [x] Data & Records: `data-custom-type`, `data-object-thing`, `db-sqlite-query`, `db-sqlite-transaction`.
  - [x] Game & Simulation: `game-actor-damage`, `game-cooldown-system`, `game-inventory-capacity`.
  - [x] Web & Network: `web-http-json-response`, `http-get-request`.
  - [x] Type Conversions: `conv-number-format`, `conv-bytes-hex`.
  - [x] Date & Time: `date-diff-days`, `date-add-days`.
  - [x] UI & Layout: `ui-page-layout`.
  - [x] Validation: `val-email-check`, `val-numeric-range`.
- [x] **1B.2 Snippet Documentation Reference (`library/SNIPPETS.md`)**
  - [x] Full code listings, tags, capabilities, and explanations for all 38 snippets across 15 categories.
- [x] **1B.3 Automated Snippet Validation Harness (`tools/test-snippets.ps1`)**
  - [x] Extract every runnable example and execute via native `otter.dll` (38/38 passing, 100%).

### 1C. Official Source Packs (Expanded Capabilities)
- [x] **1C.1 `otter-official/math-utils` (v1.0.0)**
  - [x] Functions: `clamp`, `lerp`, `mapRange`, `minVal`, `maxVal`, `calcAverage`.
- [x] **1C.2 `otter-official/text-utils` (v1.0.0)**
  - [x] Functions: `slugify`, `padLeft`, `padRight`, `repeatText`.
- [x] **1C.3 `otter-official/list-utils` (v1.0.0)**
  - [x] Functions: `hasItem`, `dedupeList`, `countMatches`, `joinWithPrefix`.
- [x] **1C.4 `otter-official/file-helpers` (v1.0.0)**
  - [x] Functions: `readOrFallback`, `writeSafe`, `readJsonObject`, `writeJsonObject`.
- [x] **1C.5 `otter-official/game-entity` (v1.0.0)**
  - [x] Types & functions: `a Actor has`, `a InventoryItem has`, `a Cooldown has`, `a Inventory has`.
  - [x] Methods: `applyDamage`, `restoreHealth`, `levelUp`, `createCooldown`, `isCooldownReady`, `triggerCooldown`, `createInventory`, `addItemToInventory`.
- [x] **1C.6 Pack Test Runner (`tools/test-packs.ps1`)**
  - [x] Automated batch testing of all unit tests and demos (10/10 passing, 100%).

### 1D. Local Package Manager CLI & Lockfile Engine
- [x] **1D.1 PowerShell Library CLI (`tools/otter-library.ps1`)**
  - [x] Commands: `search`, `list`, `show`, `insert`, `install`, `verify`, `uninstall`.
  - [x] Blueprint commands: `blueprint-search`, `blueprint-inspect`, `blueprint-explain`, `blueprint-plan`, `blueprint-apply`.
  - [x] Recipe commands: `recipe-search`, `recipe-inspect`, `recipe-explain`, `recipe-plan`, `recipe-apply`, `recipe-verify`.
- [x] **1D.2 Native C# CLI Subsystem (`src/Otter.Cli/LibraryCommands.cs`)**
  - [x] Commands: `otter library search|list`, `otter snippet show|insert`, `otter pack inspect|install|list|verify|uninstall`, `otter blueprint search|inspect|explain|plan|apply`, `otter recipe search|inspect|explain|plan|apply|verify`.
  - [x] Supports `--json`, `--dry-run`, `--force`, `--level`, `--explain`, and deterministic lockfile generation (`otter.lock`).
  - [x] Automated relative import calculation for generated files (`../.otter/packages/...`).
- [x] **1D.3 Hardening & Verification**
  - [x] Path traversal guards (rejecting `..` and absolute paths).
  - [x] Lockfile integrity verification (`verify`) detecting tampered files (`[CORRUPT: CHECKSUM MISMATCH]`).
  - [x] Cross-project isolation verified in separate test projects.

### 1E. Otter Studio IDE Integration
- [x] **1E.1 Studio Library Explorer (`library/studio/library-explorer.js`)**
  - [x] Synchronized to `otterPS/otter-studio/js/components/library-explorer.js`.
  - [x] Tabbed browsing across Snippets, Packs, and Blueprints.
  - [x] Real-time keyword & natural language filter, capability warning badges, and preview pane.
  - [x] Unit test suite passing 5/5 (`library/studio/library-explorer.test.mjs`).
- [x] **1E.2 Studio Blueprint Workspace (`library/studio/blueprint-workspace.js`)**
  - [x] Synchronized to `otterPS/otter-studio/js/components/blueprint-workspace.js`.
  - [x] 3-panel synchronized visual UI: Visual graph canvas, generated Otter source editor, 3-level explanation panel.
  - [x] Configuration parameter editor with real-time regeneration and customization guards.
  - [x] Mermaid diagram generator and dry-run `ApplyPlan` export.
  - [x] Unit test suite passing 5/5 (`library/studio/blueprint-workspace.test.mjs`).

### 1F. Parity & End-to-End Conformance Testing
- [x] **1F.1 Native C# Execution**: All 38 runnable snippets, 5 packs, 4 blueprints, and 4 recipes execute cleanly.
- [x] **1F.2 Dual-Engine Parity (`tools/compare.ps1`)**: 100% parity verified between C# and PowerShell reference engines.

---

## Phase 2: Otter Blueprints & Online Registry Architecture

### 2A. Otter Starter Blueprints Catalog
- [x] **2A.1 `otter-official/game-combat` (v1.0.0)**: Composes `game-entity` and `math-utils` for actor damage, health calculation, cooldowns, and defeat triggers.
- [x] **2A.2 `otter-official/console-calculator` (v1.0.0)**: Composes `math-utils` for formatted arithmetic operations with clamping and averages.
- [x] **2A.3 `otter-official/text-cleanup` (v1.0.0)**: Composes `text-utils` for automated text padding, slugification, and sanitization.
- [x] **2A.4 `otter-official/file-settings` (v1.0.0)**: Composes `file-helpers` for safe JSON-backed configuration loading with schema fallbacks.
- [x] **2A.5 Blueprint Test Harness (`tools/test-blueprints.ps1`)**: All 8 tests and demos pass cleanly (100%).

### 2B. Online Registry & Website Specification
- [x] **2B.1 Specification Document (`library/registry/REGISTRY_DESIGN.md`)**

---

## Phase 3: Native Recipes, Global Discovery & Full Applications

### 3A. Global Library Discovery
- [x] **3A.1 Multi-Tier Precedence Engine (`src/Otter.Cli/LibraryCommands.cs`)**
  - [x] Tier 1: `OTTER_LIBRARY_PATH` environment variable override.
  - [x] Tier 2: In-tree search climbing parent directories for `library/`.
  - [x] Tier 3: User-level cache directory `~/.otter/library/` (cross-platform home directory resolution).
  - [x] Tier 4: Bundled distribution beside CLI binary `AppContext.BaseDirectory/library/`.
  - [x] Tier 5: System shared paths (`/usr/local/share/otter/library`, `ProgramData/Otter/library`).
- [x] **3A.2 Diagnostics & Cross-Platform Integrity**
  - [x] Clear diagnostic error message when no library directory can be discovered.
  - [x] Works out-of-the-box in empty directories (e.g. `/tmp/test-app`) with zero environment variables set.

### 3B. Native Recipe Engine
- [x] **3B.1 CLI Commands**: `otter recipe search`, `inspect`, `explain`, `plan`, `apply`, `verify`.
- [x] **3B.2 Transitive Dependency Resolution**: Resolves Packs from Recipes and Blueprints, eliminating duplicates and cycles.
- [x] **3B.3 Side-Effect-Free Planning**: `--json` output returns full dependency plan, generated file actions, and conflict flags.
- [x] **3B.4 Transactional Apply & Rollback**: Installs packs, verifies SHA-256 hashes, writes files, records instances in `.otter/recipes/instances.json`, and rolls back on failure.
- [x] **3B.5 Conflict Guard & Idempotency**: Detects locally modified files, rejects unsafe overwrites without `--force`, and ensures repeat applications are safe and idempotent.

### 3C. Intelligent Feature Discovery
- [x] **3C.1 Intent & Keyword Scoring Engine**: Stopword removal, stem matching, synonyms map (e.g., "save" -> "file", "progress" -> "game", "loot" -> "inventory").
- [x] **3C.2 Recommendation Explanations**: `--explain` prints descriptive rationale for each recommended Snippet, Pack, Blueprint, or Recipe.

### 3D. Four Complete Functional Applications
- [x] **3D.1 Contact Manager (`otter-official/contact-manager`)**
  - [x] Create, search, edit, delete contacts with input email validation (`@` and `.`).
  - [x] Full JSON persistence across application restarts.
  - [x] Dual-engine parity verified on C# and PowerShell engines.
- [x] **3D.2 Task Tracker (`otter-official/task-tracker`)**
  - [x] Create tasks, complete tasks, delete tasks, list active and completed tasks.
  - [x] Padded table formatting and JSON file persistence across session restarts.
- [x] **3D.3 Web Portal (`otter-official/web-portal`)**
  - [x] Native HTTP server routes, navigation menu, feedback form handling, and email validation.
  - [x] Persistent JSON submissions and HTML landing page.
- [x] **3D.4 RPG Adventure (`otter-official/rpg-adventure`)**
  - [x] Composes `game-combat` blueprint, `game-entity`, `math-utils`, and `file-helpers`.
  - [x] Ability cooldown timers, inventory slots, equipment bonuses, boss loot drops, and XP level-up.

### 3E. AI Composition Protocol & Educational Experience
- [x] **3E.1 Composition Proposal Schema (`library/schema/composition-proposal.schema.json`)**: Provider-independent schema for assistant proposals.
- [x] **3E.2 AI Protocol Specification (`docs/AI_COMPOSITION_PROTOCOL.md`)**: 8-step workflow with validation and approval gates.
- [x] **3E.3 3-Level Explanations**: All 4 Blueprints and 4 Recipes support `beginner`, `intermediate`, and `advanced` levels.

### 3F. Full Automated Verification
- [x] **3F.1 Recipe Test Suite (`tools/test-recipes.ps1`)**: 16/16 dual-engine tests pass cleanly (100%).
- [x] **3F.2 Full Regression Battery**: 38 snippets + 10 pack tests + 8 blueprint tests + 16 recipe tests + 10 Studio tests pass with 0 failures.
- [x] **3F.3 12-Step Definition of Done Walkthrough**: Executed and verified from clean outside directory `/tmp/new-user-walkthrough`.

---

## 4. Phase 4 — Developer Experience, AI Integration & Distribution

### 4A. Distribution & Multi-Platform Installation
- [x] **4A.1 Automated Distribution Packaging (`tools/package-distribution.sh`)**
  - [x] Multi-platform packaging generating standalone runnable bundles (`otter-1.0.1-portable.tar.gz`).
  - [x] Universal POSIX shell launcher (`bin/otter`) and Windows launcher scripts (`bin/otter.cmd`, `bin/otter.ps1`).
  - [x] Bundles standard library, 38 snippets, 5 official packs, 4 blueprints, 4 recipes, and JSON schemas.
- [x] **4A.2 Clean-Machine Installers (`tools/install.sh`, `tools/install.ps1`)**
  - [x] POSIX installer with .NET runtime detection, global launcher symlinking, library sync, and PATH configuration.
  - [x] Windows PowerShell installer with User PATH modification and launcher script deployment.
  - [x] Automated sandbox clean-machine verification script (`tools/test-installer.sh`) passing 100% in isolated environment.
- [x] **4A.3 Environment Diagnostics (`otter doctor`)**
  - [x] Comprehensive diagnostics for OS, .NET runtime, binary location, PATH environment, package cache permissions, and standard library components.
  - [x] Human-readable format and structured JSON mode (`otter doctor --json`).

### 4B. Developer Tooling & Shared Language Service
- [x] **4B.1 Shared Language Service (`src/Otter.Language/LanguageService.cs`)**
  - [x] Single compiler-derived architecture without logic duplication across editors.
  - [x] `FormatDocument`: Enforces standard 4-space block indentation, period block closures, and trailing whitespace cleanup.
  - [x] `GetDiagnostics`: Lexer/parser error collection with exact line and column locations and actionable suggestions.
  - [x] `GetCompletions`: Cursor autocomplete for keywords, operators, user functions, types, and variables.
  - [x] `GetDefinition`: Go-to-definition resolution across local files and imported packages.
  - [x] `FindReferences`: Cross-file symbol references discovery.
  - [x] `RenameSymbol`: Safe semantic symbol renaming with syntax validation and keyword collision guards.
  - [x] `LookupDocumentation`: Instant lookup for Otter statements, keywords, and snippet catalog items.
- [x] **4B.2 Tooling CLI Commands (`src/Otter.Cli/ToolingCommands.cs`)**
  - [x] `otter format <file.ot> [--check]`
  - [x] `otter complete <file.ot> <line> <col> [--json]`
  - [x] `otter definition <file.ot> <line> <col> [--json]`
  - [x] `otter references <file.ot> <line> <col> [--json]`
  - [x] `otter rename <file.ot> <line> <col> <new-name> [--dry-run]`
  - [x] `otter doc <query> [--json]`

### 4C. AI-Assisted Library Composition
- [x] **4C.1 Deterministic Composition CLI (`src/Otter.Cli/ComposeCommand.cs`)**
  - [x] Provider-independent implementation of the AI Composition Protocol.
  - [x] `otter compose search <query>`: Natural language library query.
  - [x] `otter compose inspect <id>`: Manifest, inputs, outputs, and dependency inspection.
  - [x] `otter compose check <id>`: Transitive dependency and version compatibility check.
  - [x] `otter compose propose "<intent>"`: Generates structured proposal conforming to `composition-proposal.schema.json`.
  - [x] `otter compose preview <proposal.json>`: Line-numbered source code preview with source mappings.
  - [x] `otter compose explain <proposal.json>`: Architectural explanation and connection topology.
  - [x] `otter compose apply <proposal.json> [--yes]`: Strict user approval gate before mutating the filesystem.

### 4D. Debugging and Test Discovery
- [x] **4D.1 Universal Test Discovery (`src/Otter.Cli/ProjectCommands.cs`)**
  - [x] Extended `otter test` to discover `test.ot`, `*.test.ot`, `*_test.ot`, and `test_*.ot` in `tests/` and project root.
  - [x] Structured test runner output with distinct PASS, FAIL (syntax error), and FAIL (runtime error) classifications.
  - [x] Dual-engine verification for all official recipe test suites.

### 4E. Local-First Publishing Foundation
- [x] **4E.1 Pack Packaging & Verification (`src/Otter.Cli/PublishCommands.cs`)**
  - [x] `otter pack pack <dir>`: Manifest validation, syntax check, security scan against binary executables, unit test execution, archive generation, and SHA-256 calculation.
  - [x] `otter pack publish <archive> [--dry-run]`: ZipSlip prevention, integrity verification, dry-run security checks, and local cache publication.
- [x] **4E.2 Recipe Packaging & Verification**
  - [x] `otter recipe pack <dir>`: Template validation, unit test execution, and `.otterrecipe` archive generation.
  - [x] `otter recipe publish <archive> [--dry-run]`: Archive integrity verification and local publication.

### 4F. First-Run Guided Onboarding
- [x] **4F.1 Interactive Tutorial CLI (`src/Otter.Cli/TutorialCommand.cs`)**
  - [x] 7-step guided walkthrough demonstrating writing code, running, searching library, installing packs, applying recipes, using tooling, and debugging tests.
  - [x] `otter tutorial` (interactive) and `otter tutorial --demo` (scripted/automated).

