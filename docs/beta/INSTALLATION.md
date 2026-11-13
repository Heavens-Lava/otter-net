# Otter Public Beta: Installation Guide

Welcome to the Otter Public Beta! Otter is a modern, human-readable programming language built with Native AOT for zero-dependency, instant-startup execution on Windows, macOS, and Linux.

---

## System Requirements

- **macOS:** macOS 12 (Monterey) or later (Apple Silicon arm64 or Intel x64).
- **Linux:** Ubuntu 20.04+, Debian 11+, Fedora 36+, or any modern glibc 2.31+ distribution.
- **Windows:** Windows 10 (1809+) or Windows 11 (x64 or ARM64).
- **Disk Space:** ~50 MB for the Otter runtime and standard library.
- **Dependencies:** None. Otter distributes as a self-contained Native AOT executable.

---

## 1. Automated Installation (Recommended)

### macOS and Linux
Run the installation script in your terminal:
```bash
curl -fsSL https://get.otter-lang.org/install.sh | bash
```
Or from a local clone of the repository:
```bash
bash tools/install.sh
```

The installer:
1. Installs the native `otter` binary to `~/.otter/bin/otter`.
2. Bundles the complete offline standard library into `~/.otter/library`.
3. Creates a symlink in `~/.local/bin/otter` (automatically added to your `$PATH`).

### Windows
Run PowerShell as Administrator or standard user:
```powershell
pwsh tools/install.ps1
```
The installer copies Otter to `%USERPROFILE%\.otter\bin` and adds it to your user `PATH`.

---

## 2. Verifying Installation

Open a fresh terminal window and verify that Otter runs globally:
```bash
otter --version
```
Expected output:
```text
Otter 1.0.1
```

Run the built-in system diagnostics tool:
```bash
otter doctor
```
`otter doctor` verifies:
- Global CLI availability
- Standard library catalog and bundles
- LSP and DAP servers
- Compiler dependencies
- PowerShell coexistence (if installed)

---

## 3. Installing the VS Code Extension

Otter includes a first-class Visual Studio Code extension with native LSP autocomplete, diagnostics, go-to-definition, and DAP visual debugging.

### From Command Line
```bash
code --install-extension editors/vscode/otter-lang-1.0.1.vsix
```

### From VS Code UI
1. Open Visual Studio Code.
2. Open the Extensions sidebar (`Ctrl+Shift+X` or `Cmd+Shift+X`).
3. Click the `...` menu in the top-right corner of the Extensions panel.
4. Select **Install from VSIX...**
5. Choose `editors/vscode/otter-lang-1.0.1.vsix`.

---

## 4. Uninstalling Otter

To completely remove Otter and its standard library:
```bash
# macOS and Linux
rm -rf ~/.otter
rm -f ~/.local/bin/otter

# Windows (PowerShell)
Remove-Item -Recurse -Force "$HOME\.otter"
```
To remove the VS Code extension:
```bash
code --uninstall-extension heavens-lava.otter-lang
```
