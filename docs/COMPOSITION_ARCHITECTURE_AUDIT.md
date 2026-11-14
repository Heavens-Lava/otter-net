# Otter Phase 10: Composition Engine Source Audit & Architecture Gap Report

**Repository:** `Heavens-Lava/otter-net`  
**Branch:** `feature/trustworthy-composition`  
**Date:** 2026-11-14  

---

## 1. Implemented Capabilities (What Exists)

- **CLI Surface (`src/Otter.Cli/ComposeCommand.cs`):**
  - Subcommands: `search`, `inspect`, `check`, `propose`, `preview`, `explain`, `apply`.
  - Schema: Provider-independent schema in `library/schema/composition-proposal.schema.json`.
  - Validation: Virtual import resolver (`ResolveVirtualImports`) validating syntax against local pack sources via `LanguageService.GetDiagnostics`.
- **Reusable Library Catalog:**
  - 38 Snippets in `library/snippets/catalog.json` with capabilities, platforms, categories, examples.
  - 5 Official Packs in `library/packs/otter-official/` with manifests, versions, capabilities, tags.
  - 4 Visual Blueprints in `library/blueprints/otter-official/` with node/edge topological contracts (`inputs`, `outputs`, `packs`).
  - 4 Recipes in `library/recipes/otter-official/` with multi-step workflows, configurations, and templates.

---

## 2. Identified Deficiencies & Vulnerabilities

### A. Non-Transactional & Unsafe `compose apply`
1. **Unchecked Path Traversal:**
   `Apply()` accepts any `path` from the proposal JSON and performs `Path.Combine(targetDir, relPath)` without verifying that the canonical target path remains strictly confined within the project directory.
2. **Destructive Overwrites Without Backup:**
   Existing files in the destination directory are overwritten directly with `File.WriteAllText`. There is no backup of existing files and no rollback if subsequent steps fail.
3. **Swallowed Pack Installation Failures & False Success Reporting:**
   `LibraryCommands.RunPack("pack", "install", ...)` return codes are completely ignored; exceptions are swallowed via an empty `catch { }`. If a pack fails to install or a package is missing, the command prints `[SUCCESS]` and returns exit code 0.
4. **No Post-Apply Verification:**
   The applied project is never validated against `otter check` or lockfile integrity.

### B. Naive Hard-Coded Selection in `compose propose`
1. **Keyword-Only Recipe Selection:**
   A simple cascading `if/else` checks for words like "game", "web", or "contact". Any query not matching these three keywords unconditionally defaults to `otter-official/task-tracker`.
2. **No Capability or Semantic Scoring:**
   The engine does not evaluate declared component `capabilities`, `inputs`, `outputs`, or `tags`.
3. **No Recognition of Unsupported Requests:**
   If a user asks for "GPU neural network training" or "Audio synthesizer DSP", the engine blindly generates a `task-tracker` proposal instead of honestly reporting missing capabilities.
4. **Schema ID Format Violation:**
   `proposalId` is generated as `$"prop-{RandomNumberGenerator.GetHexString(6).ToLowerInvariant()}"` (6 hex chars), whereas `composition-proposal.schema.json` requires `^prop-[a-f0-9]{8,16}$` (8 to 16 hex chars).
5. **Hard-coded Non-existent Snippets:**
   Line 403 references `game-actor-init`, which does not exist in the snippet catalog (the real snippet is `game-actor-damage`).

---

## 3. Architecture for Phase 10 Improvements

```
+---------------------------------------------------------------------------------+
|                                Otter CLI                                        |
+---------------------------------------------------------------------------------+
|                                                                                 |
|  1. Metadata-Driven Discovery Engine                                            |
|     - Loads all local Snippets, Packs, Blueprints, and Recipes                  |
|     - Multi-factor capability & tag scoring                                     |
|     - Unambiguous rejection of unsupported requests with honest explanations    |
|                                                                                 |
|  2. Structured Composition Planner                                              |
|     - Adheres strictly to composition-proposal.schema.json                      |
|     - Documents component selection rationale, input/output connections         |
|     - Warns of Otter 1.0.1 variable scope collision considerations              |
|                                                                                 |
|  3. Staged Transactional Apply Pipeline                                         |
|     [Stage 1: Validation]  Verify schema, component IDs, and path containment   |
|     [Stage 2: Conflict]    Detect pre-existing files and compute diff preview   |
|     [Stage 3: Approval]    Require explicit user confirmation (or --yes)        |
|     [Stage 4: Backups]     Snapshot any pre-existing files                      |
|     [Stage 5: Packages]    Install required packs; abort on non-zero exit code  |
|     [Stage 6: Generation]  Write generated templates                            |
|     [Stage 7: Verify]      Run syntax and dependency checks                     |
|     [Rollback Handler]     On any failure, delete created files & restore state |
|                                                                                 |
+---------------------------------------------------------------------------------+
```
