# Otter Visual Studio Code Extension Guide

The official Otter VS Code extension (`heavens-lava.otter-lang`) provides a complete, modern development environment for Otter 1.0.1.

---

## Features

- **Syntax Highlighting & Grammar:** Full semantic tokenization for all Otter keywords, built-in types, operators, and strings.
- **Language Server Protocol (LSP):**
  - Instant diagnostics with helpful suggestions
  - Intelligent context-aware code completion
  - Hover documentation for functions and variables
  - Go to Definition (`F12`)
  - Find All References (`Shift+F12`)
  - Cross-file Symbol Renaming (`F2`)
  - Document Formatting (`Shift+Alt+F`)
  - Workspace Symbol Search (`Ctrl+T` / `Cmd+T`)
- **Debug Adapter Protocol (DAP):**
  - Line breakpoints and conditional pause points
  - Step Over (`F10`), Step Into (`F11`), and Step Out (`Shift+F11`) with accurate call stack tracking
  - Live local variable and object inspection
  - Call stack exploration across nested function invocations

---

## 1. Installation

Install via the command line:
```bash
code --install-extension editors/vscode/otter-lang-1.0.1.vsix
```

Confirm that the extension is active:
```bash
code --list-extensions | grep otter
```
Expected output:
```text
heavens-lava.otter-lang
```

---

## 2. Configuration Settings

Open VS Code Settings (`Cmd+,` or `Ctrl+,`) and search for `otter`:

| Setting | Default | Description |
|---------|---------|-------------|
| `otter.executablePath` | `"otter"` | Path to the `otter` CLI executable. |
| `otter.lsp.enabled` | `true` | Enables the native Language Server. |
| `otter.dap.enabled` | `true` | Enables the native Debug Adapter. |
| `otter.format.onSave` | `false` | Automatically formats `.ot` files when saving. |

---

## 3. Debugging an Otter Program

1. Open your project folder in VS Code.
2. Open any `.ot` file (e.g. `src/main.ot`).
3. Click in the left margin next to any line number to set a **Breakpoint** (red dot).
4. Press **`F5`** or open the Run & Debug panel (`Ctrl+Shift+D` / `Cmd+Shift+D`).
5. Choose **"Otter: Launch Program"**.

The debugger will launch the native Otter interpreter, pause at your breakpoint, and display:
- **Variables panel:** Local scope variables, thing/object properties, and loop iterators.
- **Call Stack:** Function frames (`<main>`, `calculateArea`, etc.).
- **Debug Console:** Program stdout output and evaluation events.

### Example `launch.json`
To customize launch settings, create `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "otter",
      "request": "launch",
      "name": "Debug Current Otter File",
      "program": "${file}",
      "stopOnEntry": false
    }
  ]
}
```

---

## 4. Keyboard Shortcuts Cheatsheet

| Action | Shortcut (macOS) | Shortcut (Windows/Linux) |
|--------|------------------|--------------------------|
| Trigger Completion | `Cmd+Space` | `Ctrl+Space` |
| Hover Info | `Cmd+Hover` | `Ctrl+Hover` |
| Go to Definition | `F12` | `F12` |
| Find References | `Shift+F12` | `Shift+F12` |
| Rename Symbol | `F2` | `F2` |
| Format File | `Shift+Option+F` | `Shift+Alt+F` |
| Start Debugging | `F5` | `F5` |
| Step Over | `F10` | `F10` |
| Step Into | `F11` | `F11` |
| Step Out | `Shift+F11` | `Shift+F11` |
