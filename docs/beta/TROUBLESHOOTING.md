# Otter Public Beta: Troubleshooting Guide

This guide covers common issues, error messages, and their resolutions when developing with Otter.

---

## 1. Installation & Environment Issues

### Issue: `command not found: otter`
**Cause:** The directory `~/.local/bin` (or `~/.otter/bin`) is not in your current shell's `$PATH`.  
**Resolution:**
1. Add `~/.local/bin` to your shell profile (`~/.zshrc` or `~/.bashrc`):
   ```bash
   export PATH="$HOME/.local/bin:$PATH"
   ```
2. Source your profile or restart your terminal:
   ```bash
   source ~/.zshrc
   ```
3. Run `otter doctor` to verify system health.

---

## 2. Common Syntax Errors

### Error: `Indentation must use groups of 4 spaces or tabs.`
**Cause:** A code block was indented using 2 spaces, 3 spaces, or mixed indentation.  
**Resolution:** Ensure that all indented lines use exactly 4 spaces (or 1 tab) per nesting level.

### Error: `I expected a value here.` in `has` block
**Cause:** Trying to write `list are empty` directly inside an object literal:
```otter
# Incorrect
manager has
    items are empty
.
```
**Resolution:** Initialize the empty list outside the `has` block:
```otter
# Correct
emptyList are empty
manager has
    items is emptyList
.
```

### Error: `I expected "with" and a message.`
**Cause:** Writing `fail "message"` without the keyword `with`.  
**Resolution:** Write `fail with "Your failure message"`.

### Error: `I expected the statement to end here.`
**Cause:** Mismatched argument calling syntax in multi-parameter functions.  
**Resolution:** Multi-parameter functions must separate arguments with `and`:
```otter
# Correct
result is calculateArea 10 and 20
```

---

## 3. Package & Dependency Issues

### Error: `Tampered lockfile hash detected` or `SHA-256 mismatch`
**Cause:** A file inside `.otter/packages/...` was manually modified or corrupted.  
**Resolution:**
Reinstall the affected pack cleanly:
```bash
otter pack uninstall <pack-id>
otter pack install <pack-id>
otter pack verify
```

---

## 4. Visual Studio Code & LSP Issues

### Issue: Diagnostics or autocomplete do not appear in VS Code
**Resolution:**
1. Check that the extension is enabled: `code --list-extensions | grep otter`.
2. Confirm the global `otter` binary is discoverable:
   ```bash
   which otter
   ```
3. Check the **Output** panel in VS Code (`View -> Output`) and select **Otter Language Server** from the dropdown to inspect logs.
4. Restart the language server: Press `Cmd+Shift+P` -> `Otter: Restart Language Server`.

---

## 5. Desktop GUI Execution

### Issue: `Otter Runtime Error: WPF may not be available on this system.`
**Cause:** `otter run` on macOS or Linux encountered a script declaring desktop UI elements (`create window into app`). Direct native WPF windows only run on Windows.  
**Resolution:**
On macOS and Linux, compile and run desktop GUI programs via `otter web` or `otter desktop`:
```bash
otter web src/tasks_gui.ot -NoOpen
otter desktop src/tasks_gui.ot
```
This compiles the application to a self-contained HTML/JS bundle and serves it locally in an application window.
