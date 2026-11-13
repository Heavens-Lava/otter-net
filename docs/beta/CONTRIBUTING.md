# Contributing to Otter

Thank you for your interest in contributing to Otter! We welcome bug reports, test cases, standard library packs, documentation improvements, and runtime optimizations.

---

## 1. Guiding Principles & Non-Negotiables

- **Preserve Canonical 1.0.1 Grammar:** The Otter 1.0.1 syntax is frozen. Do not introduce unauthorized keywords, dot syntax, or breaking grammar changes without RFC approval.
- **Cross-Platform Compatibility:** All native C# runtime features must build and test identically across Windows, macOS, and Linux.
- **No Protocol Leakage:** Tools implementing LSP or DAP must never write stray diagnostic or debug text to `stdout`. All communication must strictly adhere to Content-Length framed JSON-RPC / DAP specifications.
- **Offline First:** Otter tools must remain fully operational without internet access.

---

## 2. Setting Up the Development Environment

### Prerequisites
- .NET 10 SDK (for compiling C# sources)
- PowerShell 7 (`pwsh`)
- Python 3.8+ (for protocol and reliability test suites)
- Git

### Building the Project
```bash
git clone https://github.com/Heavens-Lava/otter-net.git
cd otter-net

# Restore and build the CLI and Runtime
dotnet build src/Otter.Cli/Otter.Cli.csproj
```

### Running the Test Suites
Before submitting any pull request, ensure all test suites pass:
```bash
# Language Server Protocol tests (35 checks)
pwsh tools/test-lsp.ps1

# Debug Adapter Protocol tests (28 checks)
pwsh tools/test-dap.ps1

# Automated Reliability & Fuzzing suite (28 checks)
pwsh tools/test-reliability.ps1

# Standard library test suites (72 checks)
pwsh tools/test-snippets.ps1
pwsh tools/test-packs.ps1
pwsh tools/test-blueprints.ps1
pwsh tools/test-recipes.ps1
```

---

## 3. Submitting Issues and Pull Requests

1. **Issue Reports:** When reporting a defect, include:
   - Operating system and architecture (`uname -a` or `[System.Environment]::OSVersion`)
   - Exact Otter version (`otter --version`)
   - A minimal reproducible `.ot` example
   - Expected vs actual output
2. **Pull Requests:**
   - Commit messages should be concise and descriptive.
   - Reference the corresponding GitHub issue number.
   - Include regression tests in `tests/` or in `tools/test-reliability.py`.
