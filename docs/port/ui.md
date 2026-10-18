# UI port inventory (web and desktop)

Every user-interface capability of otterPS 1.0.1 and where it stands in
otter-net. Updated 2026-10-17.

**Status:** **native** (otter-net does it itself) · **partial** · **missing**
(handed to the PowerShell Otter before anything runs) · **out of scope**.

**Fallback** says what happens when otter-net does not do it: the whole
command goes to the PowerShell Otter (`otter.ps1`), unchanged, before
anything is written or run.

**Tests:** `tests/web/*.ot` (compared with `tools/compare-web.ps1`); the
otterPS examples and conformance fixtures and `tests/compat` are compiled by
the same script.

**The rule for `otter web`:** the page is compiled natively when every
statement in the program is one otter-net parses and the web compiler here
knows; otherwise the command falls back. **The rule for `otter run` /
`otter check`:** any UI statement falls back (desktop windows need WPF, see
`desktop-options.md`).

otterPS files below: `Web` = `src/Otter.Web.psm1`, `JS` =
`src/Otter.Compiler.JavaScript.psm1`, `Parser` = `src/Otter.Parser.psm1`,
`UI` = `src/Otter.UI.psm1`, `Interp` = `src/Otter.Interpreter.psm1`,
`LoopPasses` = `src/Otter.LoopPasses.psm1`, `Validation` =
`src/Otter.Validation.psm1`.

## Commands

| Capability | otterPS | otter-net | Status | Fallback | Tests | Remaining |
|---|---|---|---|---|---|---|
| `otter web <file.ot> [-NoOpen\|-Open]` (and `otter browse`) | `otter.ps1` (web branch), Web `Export-OtterWebApplication` | `Otter.Cli/WebCommand.cs`, `Otter.Web/WebCompiler.cs` | native | project folders, missing/unreadable files, other options, `use` problems, unported statements | all of `tests/web` | - |
| Writing the page | Web `Export-OtterWebApplication` (`Set-Content -Encoding UTF8`) | `WebCommand.Run` | native | an unwritable page | every test | writes the 5.1 form: BOM + page + CRLF (PowerShell 7 writes no BOM and LF) |
| `<entry>.css` sidecar, link containment (RC3 B4) | Web `Export-OtterWebApplication`, `Resolve-OtterRealPath`, `Test-OtterPathInside` | `WebCommand.WithSidecarStylesheet`, `RealPath`, `PathInside` | native | a `.css` that is a folder or unreadable | `tests/web/sidecar` | - |
| Opening the browser | `otter.ps1` (`Start-Process` / `open` / `xdg-open`, 15 s) | `WebCommand.OpenInBrowser` | native | - | not run in tests (`-NoOpen`) | - |
| Compile errors (exit 2, `FormatDetailed`, remapped into `use`d files) | Web `Export-OtterWebApplication` catch, Module `ConvertTo-OtterRemappedDiagnostics` | `WebCommand.ReportError`, `ResolvedProgram.Remap` | native | - | `tests/web/err_*.ot`, `tests/compat/syn_*.ot` | - |
| Internal error (exit 1, "Otter hit a problem inside itself") | Web `Export-OtterWebApplication` catch-all | - | missing | any unexpected exception falls back | - | by design |
| `otter serve <file.ot> [-Port N]` | `otter.ps1`, `src/Otter.Server.psm1` | - | missing | whole command | - | needs the network area's `WebRoute`/`Respond`/`StartServer`/`ListenServer` and an HttpListener host |
| `otter desktop <file.ot>` | `otter.ps1`, `src/Otter.Desktop.psm1` `Start-OtterDesktopApplication` | - | missing | whole command | - | see `desktop-options.md` (browser window + bridge; not WPF) |
| `otter studio` | `otter.ps1`, Desktop `Start-OtterStudio` | - | out of scope | whole command | - | developed separately |
| `otter run` / `otter check` of a UI program (WPF windows) | Interp UI cases, UI | - | missing | any UI statement | - | see `desktop-options.md` |

## Parsing (web target only)

`Parser.Parse(tokens, webTarget: true)` (`Parser.Ui.cs`); in `run`/`check` every form below still falls back.

| Form | otterPS | otter-net | Status | Tests |
|---|---|---|---|---|
| `create KIND into x` (D44), `create the KIND into the x` | Parser `Read-OtterStatement` Create, `Read-OtterUiResourceTypeName` | `TryReadUiStatement` Create | native | create_resources |
| `x is a KIND [with ...]` / block, `x is panel with ...` (D110), variant kinds | Parser Identifier case | `Parser.cs` `ReadIdentifierStatement` (web mode) | native | resources, variant_buttons |
| `align DIR`, `width/height full`, bare `round`/`spread`, `accepts drops` in `has`/`with` lists (D54, D110) | Parser `Read-OtterObjectBlockProperties`, `Read-OtterInlineObjectProperties` | `ReadUiPropertyValue`, `ReadAcceptsDrops` | native | resources, dimensions, align_spread, dragdrop_* |
| `when x is EVENT` / `when x EVENT` (D46); `when x changes` (Watch) | Parser When | `TryReadWhenStatement` | native | page_counter, declarative |
| `when x receives ...` (web server route) | Parser When | - | missing (network area) | - |
| `put a, b in the c` (D47) | Parser Put | `TryReadPutInStatement` (StatementGroup) | native | window_layout |
| `put data to url` / `as json` (HTTP) | Parser Put | - | missing (network area) | - |
| `show app` (D47) | Parser Show | `TryReadUiStatement` Show | native | many |
| UI elements: window page card heading text button panel section sidebar main link image input grid, `primary/secondary/danger` (D56) | Parser `Read-OtterUiElementStatement` | `ReadUiElementStatement` | native | declarative* |
| Element body: layout, align, gap, spread, `columns N on BP`, `stack on BP`, round/background/placeholder/padding/margin/width/height | same | same | native | declarative |
| Animation blocks hover/press/enter/leave; fade/move/slide/grow/shrink/scale/rotate; `animate N[ms\|s] [easing]` | Parser `Read-OtterAnimationBlock` | `ReadAnimationBlock` | native | declarative |
| Event blocks click/change/input/submit/hover/press/focus/blur | Parser UI element | same | native | declarative |
| `layout MODE [cols]` statement | Parser Layout | `TryReadUiStatement` Layout | native | declarative |
| `state x is v`, `derive x is e`, `memo x`, `shared x is v` | Parser State/Derive/Memo/Shared | `TryReadUiStatement` | native | declarative, err_memo, err_shared |
| `focus x`, `hide x` | Parser Focus/Hide | same | native | err_focus |
| `use word` (not a file import) | Parser Use | same | native | err_use |
| `on start` / `on close` | Parser On (Lifecycle) | `TryReadUiOnStatement` | native | err_lifecycle |
| `on drag of x`, `on drop on x`, `on files dropped on x` (D110) | Parser On | same | native | dragdrop_* |
| `set drag data to v`; `dragged item`, `dropped files`, `drag data`, `drop x`, `drop y` | Parser Statement / `Read-OtterValue` | `TryReadUiStatement`, `TryReadUiValue` | native | dragdrop_board |
| `route P shows page`, `route otherwise shows page`, `go to P`, `go back/forward`, `replace route with P`, `on route change` (D103) | Parser | same | native | routing* |
| `current route`, `route parameter X`, `query parameter X` | Parser `Read-OtterValue` | `TryReadUiValue` | native | routing |
| `the X of Y` readability form | Parser `Read-OtterValue -PropertyTarget`, Statement `the` | - | missing (core grammar, used across the parser) | examples/hello-app.ot falls back |
| `await ...` | Parser Await | - | missing (also used by HTTP) | await_delay falls back |
| Reserved names in UI statements (D-2) | Validation walk | `ReservedWords.cs` (UI cases) | native | err_reserved_name |
| Handler set up in another deferred body using its run names (HandlerRunName) | LoopPasses `Get-OtterHandlerRunCaptures`, Validation | `LoopPasses.HandlerRunDiagnostics` | native | handler_runs_watch_refused |

## The page (Web `ConvertTo-OtterWeb`)

| Piece | otterPS | otter-net | Status | Tests |
|---|---|---|---|---|
| Pass 1: resources, containers, state/derive/watch, functions, declarative roots, handlers, top-level code | Web `ConvertTo-OtterWeb` | `WebCompiler.Run` | native | all |
| Runtime UI discovery (D128) | Web `Get-OtterWebRuntimeUiInfo` | `RuntimeUi.Of` | native | runtime_ui_* |
| Non-literal properties become runtime assignments | Web `New-OtterWebRuntimePropertyAssign` | `WebCompiler.AddProperties` | native | create_resources |
| Root container, title, background, foreground, `scroll` (doc-scroll) | Web | `WebCompiler.Run` | native | resources, title_escaping |
| Static resources: window, page, card, button, text box, text, image, link, checkbox, dropdown/select, slider/range, text area, badge/tag, canvas, progress, toggle/switch, radio, row, column, scroll, other kinds | Web `Render-OtterElementCore` | `WebCompiler.RenderResource` | native | resources, controls |
| Style properties: width/height (full), maxwidth, minwidth, minheight, background, foreground, round/radius, border, shadow, padding, margin, size/fontsize, weight/fontweight, fontstyle/style, family/fontfamily, lineheight, letterspacing, whitespace, overflow, align/align_h, flex, grow, cursor, position, top/bottom/left/right, zindex, customstyle | Web `Render-OtterElementCore` | same | native | resources |
| Row/column alignment (D54), spread, wrap, justify, alignitems/items, conflicts | Web | same | native | resources, err_row_align |
| draggable / accepts drops attributes (D110) | Web `Render-OtterElement` | `RenderElement` | native | dragdrop_* |
| Route pages, hidden until the router shows one (D103) | Web | `WebCompiler.Run` | native | routing_pages |
| Declarative elements, CSS rules, listeners, conditionals, `if` blocks (D56) | Web `Render-OtterDeclarativeElementWeb` | `DeclarativeRenderer` | native | declarative* |
| UI declared in a function called at the top level | Web | `WebCompiler.Run` | native | declarative_function |
| Title from a declarative root when the title is "Otter Web App" | Web | same | native | - (only when the file is called "Otter Web App") |
| State init, derived values, watchers | Web | same | native | declarative |
| Runtime UI templates (`ConvertTo-Json`, 5.1 escaping) + runtime UI script | Web, `Get-OtterWebRuntimeUiJs` | `PowerShellText.JsonString`, `Templates.WebRuntimeUiJs` | native | runtime_ui_*, create_resources |
| `when` handlers (block-scoped), drag/drop handlers with guards | Web | `WebCompiler.Run` | native | two_handlers, dragdrop_* |
| Runnable samples (`runnable true`): compile, refuse UI/host/`ask`, variables, registry, Run button | Web `Get-OtterRunnableJs`, `Test-OtterWebSampleUsesUi` | `RunnableSamples`, `RuntimeUi.SampleUsesUi` | native | runnable_samples, runtime_ui_runnable_samples |
| Page template: CSS, runtime helpers, file/command/system bridges, storage, fetch, router, reactivity engine, canvas runner, crypto runtime, drag/drop runtime | Web here-strings, JS `Get-OtterJsCryptoRuntime` | `Templates.g.cs` (generated by `tools/web-templates.py`) | native | all |
| Top-level code with known globals (D60 1F.1) | Web, JS `Get-OtterJsTopLevelGlobalNames` | `Bindings.TopLevelGlobals` | native | all |

## The JavaScript compiler (JS)

`JsCompiler.Expressions.cs`, `JsCompiler.Statements.cs`, `Analysis.cs`,
`LoopPasses.cs`. Every node otter-net parses today is compiled; a node it
does not know (another area's, not ported yet) makes the command fall back.

| Capability | otterPS | otter-net | Status |
|---|---|---|---|
| Expressions: literal, variable, property access (UI / thing / date / xml / http / websocket), arithmetic, comparison (dates, bytes), and/or/not, contains, starts/ends with, of-operations, larger/smaller, today/now, date from text, days between, call, file exists/locked, xml (from, select, attribute, text of, exists, has attribute, to text), bytes, secure random, sha/hmac, password matches, securely equals, current route, route/query parameter, drag context | JS `ConvertTo-OtterJsExpression` | `JsCompiler.Expression` | native |
| Statements: assign (variable / UI property / thing property), make, say, ask, if, while, count, repeat, for each, list, type, thing (and runtime UI `has`), sort, reverse, join, split, find, replace, add/remove (numbers and lists), try/fail, return, files and folders, run command, environment, system folder, current directory, get/set key, dates (adjust, difference, format), log/warn/error, random, json, csv, xml, crypto, functions and calls, timers, routing, set drag data | JS `ConvertTo-OtterJsStatementCore` | `JsCompiler.Statement` | native |
| Web-target errors: say in color, ask secretly, console primitives, wait, random seed, bytes files, memo/on start/shared/use/focus/hide, nested state/derive/watch/UI element/UI event, nested drag handlers | JS | same | native |
| Runtime UI statements in handlers, functions and loops: create, put, show, when (with D130/D133 captures) | JS | same | native |
| Function locals and async detection (with otterPS's quirks) | JS `Get-OtterJsBindingNames`, `Test-OtterJsBodyNeedsAsync` | `Bindings`, `AsyncAnalysis`, `PowerShellSort` | native |
| Loop pass names (D130) and handler runs (D133) | LoopPasses `Get-OtterLoopPassNames` | `LoopPasses.Names` | native |
| HTTP (get/post/put/delete/start/cancel, options), download, websockets, TCP/UDP, file watching, databases and queries, clipboard, notify, file dialogs, system information, processes, links, owners, registry, event log, credentials, secrets, power actions, printing, remote/ssh commands, `start command` jobs | JS | - | missing (those statements are not parsed by otter-net yet; they belong to the network, system and data areas - the web compiler falls back for them) |

## Desktop

| Capability | otterPS | otter-net | Status | Fallback | Remaining |
|---|---|---|---|---|---|
| WPF resources, properties, events, containment, show, declarative windows, animations | UI, Interp | - | missing | whole program (`otter run`) | decision in `desktop-options.md` |
| `state` / `derive` / `when ... changes` in `otter run` | Interp `OtterSignal`, `OtterDerived` | - | missing | whole program | would come with the desktop decision |
| `otter desktop` (browser window + bridge) | Desktop | - | missing | whole command | portable later (see `desktop-options.md`) |
