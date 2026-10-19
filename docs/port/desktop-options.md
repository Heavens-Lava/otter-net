# Desktop UI in otter-net: what it needs, and the options

**Decided (2026-10-18): option B, a WPF host.** The project owner approved
implementing Otter 1.0.1's desktop windows natively with WPF. They run in
`otter-desktop.exe` (`src/Otter.Desktop`), which the native `otter` starts
on Windows, before anything runs, for a program that creates a window (and
for the REPL); without it the PowerShell Otter runs the program as before.
What it covers, where, and how it is verified against otterPS on a Windows
desktop: `docs/port/desktop.md`. The rest of this page is the analysis the
decision was made from, as it stood on 2026-10-17.

What that meant precisely (2026-10-17, before the decision): in otterPS only
`create KIND into x` reaches WPF when a program runs
(`New-OtterUiResourceValue`; `put`, `show`, `when`, property access and the
`x is a ...` object forms all need a resource that only `create` makes, and
the declarative `page`/`card` blocks only run their inner statements in
`otter run`). So:

- `otter check` of any UI program is native everywhere.
- `otter run` of a program without `create` is native everywhere (state,
  derived values, watchers and every refusal included).
- `otter run` of a program with `create`: on Windows it was handed to the
  PowerShell Otter before it started (decided from the parse, `FileMode.cs`;
  the REPL refused such a line) - now it runs in `otter-desktop.exe`; on
  macOS and Linux it runs natively and fails at the first `create` exactly as
  otterPS does there ("Otter could not start its UI provider. WPF may not be
  available on this system.").
- `otter desktop` (the browser window, not WPF) is native.

In otterPS 1.0.1's corpus 11 programs contain `create` (examples greeter,
hello-app, calculator, ui-counter, ui-input, v1/contacts, v1/tasks,
v1/file-browser, benchmarks/ui/build_window, projects/my-cli-tool,
OtterWorkspace/src/ui/shell.ot).

The web target (`otter web`) is native: see `docs/port/ui.md`.

## What "desktop UI" is in otterPS 1.0.1

Two different things carry the name.

### 1. Windows run by the interpreter (`otter run app.ot`)

`src/Otter.UI.psm1`, called from `src/Otter.Interpreter.psm1`. WPF, through
`Add-Type -AssemblyName PresentationFramework`, on Windows PowerShell 5.1.

| Piece | otterPS | What a port must reproduce |
|---|---|---|
| Resources (D44) | `New-OtterUiResourceValue`, `$script:OtterWpfKinds` | `create KIND into x` makes a real control *now*: window (`Window`), button (`Button`), text (`TextBlock`), text box (`TextBox`), row / column (`StackPanel`), scroll (`ScrollViewer`), card, heading, panel, grid, primary / secondary / danger button (styled `Border`s and `Button`s). Unknown kind: `I don't know how to create a KIND yet.` with the known kinds as the suggestion. |
| Properties (D45, D48, D53, D54) | `$script:OtterUiProperties`, `Get-/Set-OtterUiProperty`, `Set-OtterUiSpacing`, `Set-OtterUiPadding`, `Set-OtterUiPlaceholder`, `Update-OtterUiSpread` | Per kind: text, title, width, height, background, foreground, spacing, padding, align (one property, the word picks the axis), spread, placeholder (a watermark brush) ... with Otter's own type checks and error texts before WPF is touched (a number for width; a colour name or `#rrggbb`). |
| Events (D46) | `$script:OtterUiEvents`, `Add-OtterUiEventHandler` | `when x is clicked / changed / closed ...` mapped to CLR events; the D130/D133 capture rules (a handler set up in a loop pass or handler run keeps that pass's names). Drag and drop is refused for windows (`... is not supported for windows yet. It works in web applications.`). |
| Containment (D47) | `Add-OtterUiChild`, `Get-OtterUiContainerPanel` | A resource has one parent; a second `put` is an Otter error. Windows hold children in a hidden vertical panel. |
| Show (D47) | `Show-OtterUiResource` | Modal `ShowDialog()`: the program waits until the window closes; a closed window cannot be shown again (an Otter error, not WPF's). The WPF dispatcher is the message loop that runs the handlers. |
| Declarative UI (D56) | `ConvertTo-OtterDeclarativeElementWpf`, `ConvertTo-OtterWpfWindow`, `Show-OtterDeclarativeAppWpf` | `page`/`card`/`button` blocks with layout, properties, events. |
| Animation (D56) | `Add-OtterUiAnimationWpf` | hover / press / enter / leave storyboards. |
| State (D56) | `OtterSignal`, `OtterDerived` in `Otter.Runtime.psm1` | `state`, `derive`, `when x changes`; not WPF, but they are what the windows react to. |
| Not on Windows | `Initialize-OtterWpfProvider` | `Otter could not start its UI provider. WPF may not be available on this system.` |

Output a user sees: the window itself (pixels, fonts, focus, keyboard),
plus anything the handlers `say` on the console.

### 2. `otter desktop app.ot`

`src/Otter.Desktop.psm1` `Start-OtterDesktopApplication`, Windows only
(elsewhere: `Otter: desktop windows are only available on Windows. Build this
program as a web application instead: otter web <file.ot>`, exit 1). It is
**not WPF**: it compiles the program with the web compiler (now native in
otter-net), starts an authenticated loopback `HttpListener` bridge (files,
terminal commands, clipboard, environment, notifications, and WinForms file
dialogs), and opens the page in an Edge or Chrome `--app=` window, serving
bridge requests until the window closes.

`otter studio` (Otter Studio) is out of scope for this port.

## Why otter-net cannot simply do it

otter-net's `otter` is a Native AOT executable. WPF (and WinForms) cannot be
compiled with Native AOT: they depend on runtime code generation and
reflection that the AOT compiler removes. So the WPF provider cannot live
inside `otter`.

## Options

### A. Keep handing desktop programs to the PowerShell Otter (today)

`otter run` detects any UI statement before running and passes the program,
unchanged, to `otter.ps1`.

- **Behaviour:** identical, by construction.
- **Cost:** none now. PowerShell stays a dependency for every desktop
  program, with its 1-4 second start; desktop programs never get faster.
- **Risk:** none; this is how otter-net works for every unported area.

### B. A separate, non-AOT WPF host process (`otter-desktop.exe`)

A small .NET 10 WPF application (JIT, Windows only) that references the same
`Otter.Language` and `Otter.Runtime` libraries (they are AOT-compatible, and
therefore also run under JIT) plus a C# port of `Otter.UI.psm1`. `otter run`
starts it for UI programs, passing the program and its standard input and
output through.

- **Behaviour:** the same WPF controls, so the same look, layout, fonts and
  message loop. The rules a port must match are listed above; the
  interpreter also needs its UI statements (`create`, `when`, `put`, `show`,
  `state`/`derive`/`when ... changes`, declarative elements), which otter-net
  does not run yet.
- **Cost:** a second executable to build, sign and ship (the antivirus point
  in ARCHITECTURE.md applies again). Either framework-dependent (users need
  the .NET 10 Desktop Runtime) or self-contained (roughly 60-100 MB more in
  the install). Windows-only CI for it. Effort: a C# port of about 2,000
  lines of `Otter.UI.psm1` plus the interpreter's UI cases.
- **Risk:** low for behaviour; moderate for packaging.

### C. Another UI toolkit inside or beside `otter`

| Toolkit | AOT | What would change for users |
|---|---|---|
| Avalonia | mostly (XAML compiled; some reflection paths need care) | Different rendering and fonts from WPF; layout rules close but not identical (StackPanel, ScrollViewer, alignment defaults); window chrome differs; would also *add* desktop windows on macOS and Linux, which otterPS refuses today - a feature change that needs a SPEC decision. NuGet dependency. |
| WinForms | no | Not AOT either; different controls and look. No gain over B. |
| Win32 through `LibraryImport` | yes | Every control drawn by the OS's classic controls: very different look; a large amount of hand-written code to get WPF's layout behaviour (StackPanel spacing, alignment, scroll). |
| A browser window over the web compiler (what `otter desktop` already does) | yes | The page's HTML/CSS look instead of WPF's; and the program would run as JavaScript in the page rather than in the interpreter, so `say`, timing, errors and console input behave like the web target, not like `otter run`. A different product, not a port. |

### Recommendation (2026-10-17) and decision (2026-10-18)

The recommendation was to keep **A** as the default and to choose **B** if
desktop speed mattered: it is the only option that keeps the WPF behaviour
exactly. Options in **C** each change what users see and need a SPEC
decision first. **The owner chose B**; it is implemented
(`docs/port/desktop.md`). Of the declarative/animation rows above, only the
resource model is reachable from `otter run` in otterPS 1.0.1 (SPEC-QUESTIONS
UI2-5), so that is what the host ports.

`otter desktop` (the browser-window form) is separate from this choice and
is now native (`src/Otter.Cli/DesktopCommand.cs`). Its WinForms file dialogs
are not needed natively: no page otter-net compiles calls them (`choose
file` and friends are not compiled by the web compiler here, so such a
program falls back whole).
