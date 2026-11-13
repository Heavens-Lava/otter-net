# Otter Public Beta Release Checklist

This release checklist outlines all required validation gates before publishing a public beta release candidate of Otter.

---

## 1. Runtime & Protocol Integrity Gates

- [x] **Native LSP Compliance (`tools/test-lsp.py`):**
  - Initialize / Shutdown lifecycle cleanly verified.
  - Document synchronization (`didOpen`, `didChange`, `didClose`).
  - Autocomplete, Diagnostics, Hover, Definition, References, Rename, and Formatting passing 35/35 checks.
- [x] **Native DAP Compliance (`tools/test-dap.py`):**
  - Breakpoints, step-in, step-over (depth tracked), step-out, stack frames, scopes, and variable inspection passing 28/28 checks.
- [x] **No Protocol Leakage:**
  - Zero non-JSON-RPC / non-DAP characters written to stdout.
- [x] **Cross-Platform Verification:**
  - Automated builds verified on macOS, Linux, and Windows.
  - Zero platform-specific path separator crashes (`/` vs `\`).

---

## 2. Real-World Applications & Usability

- [x] **Application A (Personal Finance Tracker):**
  - Standalone execution from `examples/finance-tracker`.
  - JSON persistence, search, categories, and aggregations passing all tests.
- [x] **Application B (Desktop Task Manager):**
  - Standalone execution from `examples/task-manager`.
  - CRUD, status filters, full-text search, and dual CLI/GUI interface passing all tests.
- [x] **Application C (Small RPG Game):**
  - Standalone execution from `examples/rpg-game`.
  - Official pack reuse (`otter-official/game-entity`), turn-based combat, equipment, and leveling passing all tests.
- [x] **External Run Verification:**
  - All three applications confirmed runnable outside the compiler source repository using the global `otter` binary.

---

## 3. Automated Reliability & Stress Testing

- [x] **Parser Fuzz Testing (`tools/test-reliability.py`):**
  - 10 structured syntax fuzz variations rejected with exit code 2 and structured error messages.
  - Random byte mutation fuzzing completes without unhandled exceptions or memory faults.
- [x] **Package Integrity & Lockfile Verification:**
  - Pack installation and lockfile pinning confirmed.
  - Tampered package SHA-256 hashes detected cleanly by `otter pack verify`.
- [x] **Filesystem Permission & Path Robustness:**
  - Non-existent files and folder inputs handled with user-friendly diagnostics.
- [x] **LSP Burst Stress:**
  - Rapid-fire document edits (30+ in sub-second bursts) maintain responsiveness and do not deadlock.
- [x] **DAP Edge Cases:**
  - Out-of-range line breakpoints and instant disconnect requests handled cleanly.
- [x] **Large-Project Performance:**
  - 1,200+ line project parsed in < 0.080s and executed in < 0.100s.

---

## 4. Standard Library Quality & Coexistence

- [x] **Snippets Catalog:** All 38 snippets verified passing in `tools/test-snippets.ps1`.
- [x] **Source Packs:** All 5 official packs passing in `tools/test-packs.ps1`.
- [x] **Blueprints:** All 4 blueprints passing in `tools/test-blueprints.ps1`.
- [x] **Application Recipes:** All 4 recipes passing dual-engine C# and PowerShell tests in `tools/test-recipes.ps1`.
- [x] **Total Library Checks:** 72 passing checks across all suites.

---

## 5. Documentation & Release Candidate

- [x] `INSTALLATION.md` completed.
- [x] `GETTING_STARTED.md` completed.
- [x] `LANGUAGE_REFERENCE.md` completed.
- [x] `STDLIB_REFERENCE.md` completed.
- [x] `VSCODE_GUIDE.md` completed.
- [x] `EXAMPLE_GALLERY.md` completed.
- [x] `TROUBLESHOOTING.md` completed.
- [x] `KNOWN_LIMITATIONS.md` completed.
- [x] `CONTRIBUTING.md` completed.
- [x] `RELEASE_CHECKLIST.md` completed.
- [ ] Explicit user authorization to publish packages, extensions, or release tags.
