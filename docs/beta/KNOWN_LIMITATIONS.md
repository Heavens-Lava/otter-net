# Otter 1.0.1 Public Beta: Known Limitations

To maintain full transparency with developers, this document details intentional architectural constraints, current platform differences, and planned post-1.0 enhancements.

---

## 1. Syntax & Grammar (Frozen Otter 1.0.1)

1. **No Dot Notation (`object.property`):**
   - Otter 1.0.1 uses `property of object` exclusively.
   - Deep nested access (`name of actor of player`) inverts left-to-right reading order. Dot notation is reserved for potential post-1.0 grammar revisions.
2. **List Initialization Inside `has` Blocks:**
   - `items are empty` is a standalone statement and cannot be nested directly inside an object literal. Empty collections must be bound to a variable first and assigned via `prop is var`.
3. **Multi-Parameter Invocation:**
   - Function calls with multiple arguments strictly require `and` separators: `func a and b and c`. Commas or whitespace-only argument separation are not supported.

---

## 2. Platform & GUI Differences

1. **WPF Desktop Windows (Windows Only):**
   - Native XAML/WPF windows rendered via `otter run` are only supported on Microsoft Windows.
   - On macOS and Linux, graphical desktop applications must be built and run via `otter web` or `otter desktop`, which renders through Chromium/Edge or default browser web views.
2. **Native Win32 Shell Notifications:**
   - The `notify "Title" with "Message"` statement uses the Win32 shell notification tray on Windows. On non-Windows platforms, it falls back to console output.

---

## 3. Package Management & Web Tools

1. **Offline Source Packs:**
   - The current package ecosystem distributes source packs from the local/offline repository bundle. External npm-like remote cloud registry publishing is in planning and not enabled in the offline beta.
2. **Web Assets Serving:**
   - `otter serve` provides API endpoints and live data exchange, but does not include an arbitrary MIME-typed static asset file server. Static assets are bundled into `dist/` at compile time by `otter build`.

---

## 4. Visual Studio Code & Tooling

1. **Cross-file References on Unsaved Buffers:**
   - `findReferences` across files scans documents saved on disk. In-memory unsaved edits in unopened files are not indexed until written to disk.
2. **Semantic Highlighting:**
   - Syntax coloring is currently driven by TextMate grammars. Full semantic highlighting via LSP tokens is scheduled for a future update.
