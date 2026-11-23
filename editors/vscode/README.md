# Otter Programming Language Extension for VS Code

Official VS Code extension for the **Otter Programming Language** (Otter 1.0.1), powered by `otter-net`'s native Language Server Protocol (`lsp`) and Debug Adapter Protocol (`dap`).

## Features

- **Rich Syntax Highlighting**: Full `.ot` TextMate grammar covering statements, declarations, literals, operators, and types.
- **Language Server Protocol (`otter-net lsp` / `otter lsp`)**:
  - Real-time compiler diagnostics and error suggestions as you type (including imported `use "..."` function resolution).
  - Non-breaking outer-scope mutation warnings (`OTTER_OUTER_SCOPE_MUTATION`) for Otter 1.0.1 dynamic scoping safety.
  - Schema v2 Pack contract awareness: typed autocomplete, rich hover documentation (Pack ID, version, `variablePrefix`, side effects, error mode), and go-to-definition into imported packs.
  - Find All References, safe symbol renaming, workspace symbols, and document formatting (`Shift+Alt+F`).
- **Visual Step-Through Debugging (`otter-net dap` / `otter dap`)**:
  - Breakpoints, Step Over, Step In, Continue, Pause, local variable inspection, and call stack view.
- **Project Tools & Library Explorer Commands**:
  - `Otter: Run Current File`
  - `Otter: Run Project Tests`
  - `Otter: Search Reusable Library` (Snippets, Schema v2 Packs, and Application Recipes)
  - `Otter: Preview and Apply Application Recipe`
  - `Otter: Run Doctor Diagnostics`
  - `Otter: Restart Language Server`

## Requirements

This extension requires the native **otter-net** CLI (`otter-net` or `otter`).
When `otter.executablePath` is left empty, the extension automatically checks for `otter-net` on `PATH` and falls back to `otter`. To point to a specific binary, configure VS Code Settings:

```json
{
  "otter.executablePath": "/path/to/otter-net"
}
```

## Building, Packaging, and Verifying Locally

```bash
cd editors/vscode
npm ci
npm run typecheck
npm run compile
npm run package
npm run verify
```

Install the generated VSIX in VS Code:

```bash
code --install-extension editors/vscode/otter-lang-1.0.1.vsix
```
