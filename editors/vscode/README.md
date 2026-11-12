# Otter Programming Language Extension for VS Code

Official extension for the **Otter Programming Language** (Otter 1.0.1).

## Features

- **Rich Syntax Highlighting**: Full `.ot` TextMate grammar covering statements, declarations, literals, operators, and types.
- **Language Server Protocol (LSP)**:
  - Real-time compiler diagnostics and error suggestions as you type.
  - Contextual autocomplete for functions, variables, keywords, and types.
  - Hover documentation with usage signatures, descriptions, and examples.
  - Go to Definition for user functions, types, and imports.
  - Find All References across the document.
  - Safe Symbol Renaming with syntax validation.
  - Document Formatting (`Format Document` or `Shift+Alt+F`).
- **Visual Step-Through Debugging (DAP)**:
  - Interactive debugging powered by Otter's native Debug Adapter Protocol.
  - Set and toggle breakpoints.
  - Step Over, Step In, Continue, and Pause.
  - Inspect Local variables and call stack in the VS Code Run & Debug panel.
  - Interactive console output in Debug Console.
- **Project Tools & Commands**:
  - `Otter: Run Current File`
  - `Otter: Run Project Tests`
  - `Otter: Search Reusable Library` (Browse 38+ snippets, 5 official packs, 4 blueprints)
  - `Otter: Preview and Apply Application Recipe` (Guided interactive generation)
  - `Otter: Run Doctor Diagnostics`

## Requirements

The extension requires the native Otter CLI (`otter`) installed.
If `otter` is not in your global `PATH`, configure its path in VS Code Settings:
`otter.executablePath`: `/path/to/otter`
