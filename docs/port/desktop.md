# Desktop windows (WPF) in `otter run`: inventory

Every desktop-window capability of otterPS 1.0.1 that a program reaches
through `otter run` (and the REPL), where it lives in otterPS and in
otter-net, how it is tested, and its status. Decision: the owner chose a
WPF host (option B of `desktop-options.md`), 2026-10-18.

**Status:** **native** (otter-net runs it itself, verified on a Windows
desktop against otterPS 1.0.1) · **not reachable** (otterPS 1.0.1 has the
code but no program can reach it from `otter run`; nothing to port).

otterPS files: `UI` = `src/Otter.UI.psm1`, `Interp` =
`src/Otter.Interpreter.psm1`, `Runtime` = `src/Otter.Runtime.psm1`,
`Library` = `src/Otter.Library.psm1`, `otter.ps1`.

## How it runs

Native AOT cannot host WPF, so desktop windows run in a second executable,
**`otter-desktop.exe`** (`src/Otter.Desktop`, `net10.0-windows`, WPF, JIT,
not trimmed). It is the same `otter` command: Otter.Cli's sources are
compiled into it (not copied), the interpreter is Otter.Runtime's, and it
installs `WpfUiProvider`, a port of `Otter.UI.psm1`, through the
`IUiProvider` interface in Otter.Runtime (`UiProvider.cs`); the AOT build
references no WPF.

- `otter run` (and `otter <file>`, `debug`, `profile`, a project's entry
  point) on Windows, for a program containing `create KIND into x`: decided
  from the parse, before anything runs or is printed (`FileMode.cs`, which
  throws `NeedsDesktop`). `Program.Desktop` starts `otter-desktop.exe` from
  otter's own folder (or `OTTER_DESKTOP`) with the same arguments; standard
  input, output and error and the console are inherited; its exit code is
  otter's. `OTTER_NET_TRACE=1` says `otter-net desktop: ...`.
- The REPL on Windows: any line may open a window, so the whole REPL runs in
  `otter-desktop.exe` when it is installed (`Repl.Start`).
- Without `otter-desktop.exe` the program goes to the PowerShell Otter as
  before, with the trace `otter-net desktop: otter-desktop.exe is missing
  next to otter, so the PowerShell Otter runs ...` (and
  `OTTER_ENGINE=native` refuses instead); the REPL refuses a line with
  `create`, as before.
- Off Windows nothing changes: otterPS cannot start WPF there either, and
  otter-net gives its error at the first `create` ("Otter could not start
  its UI provider. WPF may not be available on this system.").
- The program runs on a single-threaded-apartment thread with the large
  stack the native otter uses; `show` is WPF's modal `ShowDialog()`, whose
  dispatcher loop runs the handlers on that thread, as otterPS runs WPF on
  PowerShell's own thread. Nothing else runs while a window is open: the
  console event loop (watchers, sockets, HTTP, command jobs) runs after the
  last statement, as in otterPS.
- The host runs with real culture data (WPF needs it) and the invariant
  culture as the current culture, as the native otter.

Packaging: `otter-desktop.exe` is framework-dependent (it needs the .NET 10
Desktop Runtime) as built in CI; a release can publish it self-contained
(about 60-100 MB more). `tools/publish.ps1` builds it beside `otter.exe` on
Windows.

## Capabilities

| Capability | otterPS | otter-net | Tests (`tests/desktop`) | Status |
|---|---|---|---|---|
| Hand-over decided before anything runs (Windows) | `otter.ps1` (one process) | `FileMode.cs`, `Program.Desktop`, `Repl.Start` | every test (the harness requires the `desktop:` trace); `desktop.yml` "Without otter-desktop" | native |
| `create KIND into x` (D44): window, button, text, text box, row, column, scroll, card, heading, panel, grid, primary/secondary/danger button, with otterPS's defaults (left-aligned controls, a scroll that shows its bar only when needed, the card's colours, corner, padding and shadow, the heading's font, the variant buttons' colours) | UI `$script:OtterWpfKinds`, `New-OtterUiResourceValue`; Interp `CreateUiResource` | `WpfUiProvider.Create`; `Interpreter.Ui.cs` | kinds_all, properties | native |
| Unknown kind: "I don't know how to create a KIND yet." with the known kinds; kinds ignore capitals and keep the program's spelling in messages | UI `New-OtterUiResourceValue` | `WpfUiProvider.Create` | errors (1); tests/compat ui_run_create_unknown | native |
| A resource as a value: prints and is named "a button"; true; equal to any other resource and to the text "OtterUiResource" (PowerShell's `[string]` of the class) | Runtime `Format-OtterValue`, `Test-OtterTruthy`, `Test-OtterEqual`; Interp `Get-OtterTypeName` | `UiResource` (`UiProvider.cs`) | errors (34-41), window_greeter | native |
| Reading a property (D45): text/title/width/height/background/foreground/spacing/padding/placeholder/spread/align; a colour as `#AARRGGBB`; unset text gone; `full` | UI `Get-OtterUiProperty`; Interp `PropertyAccess` | `WpfUiProvider.GetProperty`; `Interpreter.cs` | properties | native |
| Setting a property (D45, D48): `text of b is ...`, `b has ...`, `the text of the b is ...`; text through `say`'s formatting; numbers checked (not a number, negative) before WPF; colours through WPF's BrushConverter; `width/height full`; wrappers and placeholders follow | UI `Set-OtterUiProperty`, `Assert-OtterUiNumber`, `Assert-OtterUiColor`; Interp `Assign`, `ObjectDef`, `Set-OtterTarget` | `WpfUiProvider.SetProperty`; `Interpreter.cs`, `Interpreter.Ui.cs` | properties, errors (2-10, 29-33), layout | native |
| Unknown property: "A KIND has no property called "x"." with the kind's properties | UI `Get-OtterUiPropertyMapping` | `WpfUiProvider.PropertyMapping` | errors (2, 3, 33) | native |
| PowerShell's own failures that reach the user: a card's `round` (no native property), a value WPF refuses (`Infinity`), a change handler failing while the program sets a text box's text (`Exception setting "Text": ...`) | PowerShell member access | `WpfUiProvider.WriteNative`, `SetNative`, `PowerShellError` | errors (29, 31), sync_handler_error | native |
| Spacing (D48): every child, now and later, gets the margin | UI `Set-/Get-OtterUiSpacing`, `Add-OtterUiChild` | `SetSpacing`, `Put` | layout, properties | native |
| Padding (D53): a Border around a window's content or around a row/column, moved into the parent | UI `Set-/Get-OtterUiPadding`, `Get-OtterUiContainerPanel` | `SetPadding`, `ContainerPanel` | layout, properties, example_contacts | native |
| Alignment (D54): one `align` property, the word picks the axis; controls align their content; rows/columns align their group (measured once) or their children; conflicts with an earlier align or with spread | UI `Get-OtterUiAlignAxis`, `Set-OtterUiMainAxisAlign`, `Set-OtterUiProperty` | `SetAlign`, `MainAxisAlign` | layout, errors (9-14) | native |
| Spread (D54) | UI `Update-OtterUiSpread` | `UpdateSpread` | layout, example_contacts | native |
| Placeholder: a watermark while the text box is empty, kept in step with typing and colours | UI `Set-OtterUiPlaceholder`, `Update-OtterUiPlaceholderWatermark`, `New-OtterUiWatermarkBrush` | `SetPlaceholder`, `UpdatePlaceholderWatermark`, `NewWatermarkBrush` | placeholder, example_contacts, example_tasks | native |
| Scroll (D53): one child; a second, or a child that is already elsewhere, is an Otter error | UI `Add-OtterUiScrollChild` | `Put` | layout, errors (17, 18), example_file_browser | native |
| `put a, b in c` (D47): windows, rows, columns; one parent; anything else "I can only put things in a window, row, column, or scroll, not a KIND." | UI `Add-OtterUiChild`; Interp `PutIn` | `Put`; `Interpreter.Ui.cs` | layout, errors (15, 16, 23, 24, 27, 28, 32) | native |
| `when x is EVENT` (D46): clicked/click on buttons (Click) and cards (PreviewMouseDown), changed on text boxes, closed on windows; unknown events; kinds with no events | UI `$script:OtterUiEvents`, `Add-OtterUiEventHandler`; Interp `When` | `AddEventHandler`; `Jobs.cs` (`WhenStmt`), `Interpreter.Ui.cs` `AddUiHandler` | events, errors (20, 21, 26) | native |
| Handlers keep their loop pass's names (D130), a handler set up while another runs keeps that run's names (D133), the body runs in the scope it was set up in | Interp `New-OtterHandlerCaptureEnvironment`, `Enter-/Exit-OtterHandlerRun` | `AddUiHandler` (with `EventLoop.cs`'s existing D130/D133 machinery) | handlers_capture, example_contacts | native |
| Drag and drop refused for windows | Interp `When` | `Jobs.cs` | errors (22) | native |
| `show x` (D47): modal; only a window; a closed window cannot be shown again | UI `Show-OtterUiResource`; Interp `Show` | `Show` | lifecycle, errors (19, 25) | native |
| A failure inside a handler while a window is shown: the window's message loop stops and otterPS reports "This window has already been closed, so it can't be shown again." at the `show` (catchable with `try`) | UI `Show-OtterUiResource` (`catch`) | `Show` | handler_error, handler_error_try | native |
| Nested windows (a window shown from a handler) | WPF | WPF | lifecycle | native |
| State, derived values, watchers (D56) driving a window | Runtime `OtterSignal`, `OtterDerived`; Interp `StateDef`, `DeriveDef`, `Watch` | `Reactivity.cs`, `Interpreter.Ui.cs` (already native) | state_bindings | native |
| The window's dispatcher and command jobs: once a window exists, a cancelled job's cancel handlers run at once, each failure ignored | Runtime `Set-/Get-OtterUiDispatcher`; Library `Invoke-OtterJobEvent` | `Jobs.cs` `CancelJob`, `IUiProvider.OnDispatcherThread` | job_cancel | native |
| The REPL with windows | `otter.ps1` `Start-OtterRepl` | `Repl.Start` (in otter-desktop) | repl_window | native |
| otterPS's UI examples | `examples/greeter.ot`, `hello-app.ot`, `calculator.ot`, `ui-counter.ot`, `ui-input.ot`, `v1/tasks.ot`, `v1/contacts.ot`, `v1/file-browser.ot` | - | example_* (copied unchanged) | native |
| Declarative WPF rendering and animation (`ConvertTo-OtterDeclarativeElementWpf`, `ConvertTo-OtterWpfWindow`, `Show-OtterDeclarativeAppWpf`, `Add-OtterUiAnimationWpf`) | UI | - | - | not reachable: no caller in otterPS 1.0.1 (`otter run` of a `page` runs only its statements; SPEC-QUESTIONS UI2-5) |
| File dialogs, notifications (`choose file`, `notify`) | Library (WinForms) | system area (`docs/port/system.md`) | - | not WPF windows |

## Tests and verification

`tests/desktop/*.ot` with `<name>.steps` (and `<name>.ot.input`,
`<name>.args`). `tests/desktop/Harness` (`desktop-harness`, `net10.0-windows`,
UI Automation) runs each program with otterPS 1.0.1 (`powershell -File
otter.ps1 run X.ot`) and with otter-net's `otter.exe` (with
`OTTER_ENGINE=native`, so a PowerShell fallback fails), each in a fresh
copy of the folder, drives the windows (`window`, `snap`, `click`, `type`,
`focus`, `keys`, `mouse`, `close`, `windows`, `wait`, `exit`) and compares:
the automation tree after each step (control types, names, text, values,
enabled and off-screen states, positions and sizes inside the window, scroll
state), the pixels of each snapshot's client area, standard output byte for
byte, standard error, and the exit code. `.github/workflows/desktop.yml`
runs it on `windows-latest` and writes the table to the job summary.

What the harness leaves out, because it is not something a user sees and
differs between the two WPFs (.NET Framework 4.8 under Windows PowerShell
5.1, .NET 10 in otter-desktop):

- hidden scroll bars (off screen, no rectangle) of text boxes and scrolls,
  which .NET Framework's automation tree lists and .NET 10's does not;
- pixels whose colours differ by 1 or 2 of 255 (seen: 3 pixels of a bold
  heading's anti-aliased edge); they are counted in the table's notes;
- where Windows places a window (every window is moved to the top left of
  the screen before it is observed; positions inside it are compared).

Verified: desktop.yml run 39191260698 (26 of 26 programs identical, every step reached its control); the
otterPS-observed quirks are listed as SPEC-QUESTIONS D4-1 to D4-5.

High DPI: desktop.yml also runs every test at a 150 % display scale
(1920x1080, set without signing out by `tools/set-display-scale.ps1`; the
monitor is at 144 dpi): identical, 26 of 26. A session that starts at 150 %
(system DPI 144) needs a sign-in, which hosted runners do not offer, and is
not verified (SPEC-QUESTIONS W5-6).

Other languages: under German formats (decimal comma) the tests are
identical (desktop.yml runs the error programs so). On a German display
language otterPS shows PowerShell's and WPF's German texts where otter-net
shows the English ones (SPEC-QUESTIONS W5-5).

See `tests/desktop/README.md` for the step language.
