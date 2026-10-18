# Open questions for the Otter language specification

Things found while porting otterPS to C# where otterPS's code and its own
documentation disagree, or where behaviour is unspecified. otter-net copies
otterPS's **code** exactly - what users get today - until Jeff decides the
question in otterPS's `SPEC-DECISIONS.md`; then both implementations follow.

## Q1. `months between` across a shorter month end (found 2026-10-15)

`Measure-OtterDateDifference` in `src/Otter.Interpreter.psm1` (D32.7) says in
its comment "Calendar months, not averaged days: Jan 31 to Feb 28 is one
month", but the code counts a month only when the end day is at least the
start day:

    months between date from "2024-01-31" and date from "2024-02-28"   # 0

The same rule makes `years between` 2000-06-15 and 2026-06-14 equal 25.

**Status:** behaviour unchanged in both implementations (Jeff, 2026-10-16).
Needs a decision: either the comment is wrong (keep the code, fix the
comment), or the code is (count Jan 31 -> Feb 28/29 as one month, which
changes results for existing programs).
Covered by `tests/compat/dates.ot`.

## Q2. `otter new`: which line endings and encoding (found 2026-10-17)

`New-OtterProject` (`src/Otter.Project.psm1`) writes its files with
`Set-Content -Encoding UTF8` from here-strings. What lands on disk depends
on the host and on how the `.psm1` was checked out: Windows PowerShell 5.1
writes a byte order mark and `\r\n`, and a here-string keeps the line
breaks of the `.psm1` file itself (`\r\n` in a Windows checkout or the
release zip built from one); PowerShell 7 on macOS and Linux writes no byte
order mark and `\n`. The same holds for the files `otter build` and
`otter publish` write (`main.ot`, `otter.json`, `otter.build.json`,
`otter.publish.json`), whose JSON also has each host's `ConvertTo-Json`
layout.

**otter-net:** writes the Windows PowerShell 5.1 bytes (byte order mark,
`\r\n`, 5.1's JSON layout) on every platform, as it does for JSON output.
Needs a decision: should a project created on any machine be byte-for-byte
the same?

## Q3. `otter version` and `otter -v` (found 2026-10-17)

otterPS 1.0.1 has `otter --version` (and `-version`); `otter version` is
"I do not recognize the command", and `-v` is PowerShell's ambiguous
parameter error (`-version` or `-Verbose`). No document says otherwise.
otter-net printed the version for both; it now does what otterPS does
(`-v` is handed to the PowerShell Otter, whose binder reports it).

## Q4. `otter debug`: the order of locals (found 2026-10-17)

`Get-OtterDebugLocals` lists a frame's variables in the order of
`OtterEnvironment.Variables`, a hash table: on PowerShell 7 that order
changes from run to run (randomised string hashing), on 5.1 it is fixed but
unrelated to the program. otter-net lists them in the order they were first
set. The protocol is JSON, so a reader keyed by name sees no difference.

## Q5. `otter publish`: "deterministic" zip (found 2026-10-17)

`New-OtterDeterministicZip` fixes entry order and dates, but the
compressed bytes come from the host's deflate implementation (.NET
Framework on 5.1, zlib-ng on .NET 9+), so the zip and the SHA-256 printed
by `otter publish` differ between hosts, and otter-net's differ from
otterPS's. The extracted files are identical.

## Q6. `otter check` with several files (found 2026-10-17)

`otter check a.ot b.ot` checks only `a.ot`; `b.ot` becomes a program
argument and is ignored. otter-net does the same. Should `check` accept
several files?

## Q7. Registry paths under HKCR:, HKU: and HKCC: (found 2026-10-17)

D78 names five registry namespaces (`HKCU:`, `HKLM:`, `HKCR:`, `HKU:`,
`HKCC:`) and otterPS's `Assert-OtterRegistryKeyPath` accepts all five, but
the statements go through PowerShell's registry provider, which has only the
`HKCU:` and `HKLM:` drives. So under the other three, `get registry value`
answers gone, `registry key ... exists` is false, and `set registry value`
fails with "Cannot find drive. A drive with the name 'HKCR' does not exist."

**Status:** otter-net does exactly that. Needs a decision: either the three
prefixes are dropped from D78, or they are made to work (which changes what
existing programs see). Covered on Windows by `tests/compat/sys_windows.ot`.

## Q8. Windows-only system information off Windows (found 2026-10-17)

D129 says features built on Windows stop on macOS and Linux with "... is only
available on Windows, and this program is running on macOS/Linux." The
`get system information` kinds do not: on PowerShell 7 off Windows, "user"
fails as an internal error (`Exception calling "GetCurrent" with "0"
argument(s): "Windows Principal functionality is not supported on this
platform."`), "groups", "software", "tasks", "printers" and "services" are
empty lists, "cpu" has no name and "memory" has no figures, and "os" names
the .NET platform ("Unix 26.1.0"). Likewise `run command ... over ssh`
always looks for `ssh.exe`, so off Windows it reports `I could not find a
program called "ssh.exe".` even where an ssh client is installed.

**Status:** otter-net copies the code. Needs a decision on whether these
should give D129's message (or, for ssh, use the platform's `ssh`). Covered
by `tests/compat/sys_system_info.ot`, `sys_desktop.ot` and
`sys_ssh_arguments.ot`.

## Q9. `kill process p and its children` off Windows (found 2026-10-17)

otterPS finds the children through CIM (`Win32_Process`), which PowerShell 7
on macOS and Linux does not have; the lookup fails quietly and only the
process itself is killed. On Windows the whole tree goes. otter-net kills the
whole tree on every platform (the Windows PowerShell 5.1 behaviour).

**Status:** a platform difference for the cross-platform consistency
question; no test observes it.

## UI questions (found 2026-10-17, porting `otter web`)

Numbered UI-n so they do not collide with questions from the other areas.
In each case otter-net's web compiler copies otterPS 1.0.1's code.

### UI-1. Derived values with arithmetic or text give an invalid page script

`ConvertTo-OtterWeb` (`src/Otter.Web.psm1`) pastes a `derive` expression's
JavaScript into a single-quoted string: `new Function('state', 'derived',
'with(state) { with(derived) { return (EXPR); } }')`. Since D60 Phase 1D-B
every arithmetic expression compiles to code containing `'number'`, so
`derive doubled is count times 2` (`examples/experimental/counter.ot`)
produces a page whose script does not parse, and nothing on the page runs.
Should `derive` work on the web (quote the expression, or compile it as a
function), or be refused like `memo`? Covered by `tests/web/declarative.ot`.

### UI-2. Which functions are `async` depends on fields some nodes do not have

`Test-OtterJsBodyNeedsAsync` and `Test-OtterJsExpressionNeedsAsync`
(`src/Otter.Compiler.JavaScript.psm1`) read `FormatDate.Date`,
`Split/Join.Source` and `.Delimiter`, `Replace.ReplaceWith`,
`CountLoop.Start/End`, `CallStatement.Arguments`, the JSON/CSV conversions'
`Source`, `XmlAttribute.Target`, `XmlHasAttribute.Xml`, `XmlToText.Source`
and `DateFromText.Text` - none of which exist, so PowerShell reads nothing.
A function that, say, checks `file "x" exists` only inside one of those
positions is not made `async`, and its `await` is a syntax error in the page.
The same check hands `add element ... and call it x` (inside a function) a
name where it expects a node, which stops otterPS with an internal error;
otter-net leaves that case to otterPS.

### UI-3. `on route change` and network event bodies are not deferred for D133

`$script:OtterDeferredKinds` (`src/Otter.LoopPasses.psm1`) lists
`[NodeKind]::RouteChange` and `[NodeKind]::NetworkEvent`, which are not node
kinds (the kinds are `RouteChangeEvent` and `WebSocketEvent`); PowerShell
reads both as nothing. So a handler set up inside `on route change` or
`on message from x` gets neither the D133 run names nor the HandlerRunName
check. Intended?

### UI-4. The page file differs between Windows PowerShell 5.1 and PowerShell 7

`Export-OtterWebApplication` writes with `Set-Content -Encoding UTF8` (5.1:
byte order mark and CRLF at the end; PowerShell 7: neither), and the runtime
UI templates with `ConvertTo-Json` (5.1 writes `< > & '` as `<` ...;
PowerShell 7 does not). A program number with more than 15 significant
digits is also written differently (`[string]` of a double). otter-net
writes the 5.1 form on every platform, as it does for JSON output (see
`docs/baselines/`). Separately: the page is mostly built from here-strings,
whose line endings are those of the checked-out `.psm1` files; with Git's
`core.autocrlf` on Windows those parts of the page would end lines with CRLF
while the rest uses LF. otter-net uses LF throughout (the files as stored in
the repository). Which is the reference?

## Q10. D99 queries ship in 1.0.1 although D99 is deferred (found 2026-10-17)

`SPEC-DECISIONS.md` D99 says the query language is deferred from Otter 1.0
(target 1.1+) and `docs/D99-QUERY-LANGUAGE-DESIGN.md` calls the code
"experimental, non-1.0 surface", yet otterPS 1.0.1 parses and runs
`get ... from TABLE in DB ... into x` and the aggregate forms (`count all
from ...`, `sum of ...`). otter-net ports the code as it is.

Within that code:

- A qualified column (`name of t`) in a projection, an `order by` or an
  aggregate becomes `[t].[]`: otterPS reads `.PropertyName`, which
  `PropertyAccessExpr` does not have (its field is `Property`), so the
  column name is empty and SQLite reports `no such column`. In a `where`
  clause the same form works (`[t].[name]`).
- `first N` is documented as a limit word (design doc, question 22, and the
  parser's `take/first/limit`), but `first` is lexed as the `first of`
  operation token, so the clause never matches and the query asks for
  `into`; `take N` and `limit N` work.
- `is in`, `is not in` and `is between` exist only inside a query's
  `where`; in an ordinary condition they are a syntax error (`I expected
  the statement to end here. I found 'is in' instead.`).

**Status:** behaviour copied; needs a decision when D99 is taken up.
Covered by `tests/compat/data_query.ot`, `data_query_checks.ot`,
`data_in_conditions.ot`.

## Q11. D97 databases on macOS and Linux (found 2026-10-17)

D97 names `winsqlite3.dll`, the SQLite built into Windows, as the
reference provider. otterPS on PowerShell 7 on macOS or Linux cannot load
it, so every database program stops there with an internal error
(`Exception calling "sqlite3_open_v2" ...`). otter-net uses the operating
system's SQLite on every platform (`/usr/lib/libsqlite3.dylib`,
`libsqlite3.so.0`), as the porting brief asks, so databases work there.
Also: D97's text says "Otter 1.1 introduces" databases, but they ship in
1.0.1; and only the first statement of an `execute`/`query` text runs (the
rest is silently ignored).

**Status:** otter-net behaves as otterPS on Windows. Needs a decision on
whether databases are part of Otter on macOS and Linux (and on SQLite's own
error texts, which differ between versions).

## Q12. An empty program is an internal error (found 2026-10-17)

`otter run empty.ot` (and `check`), for a file with no text or only one
empty line, stops with "Otter hit a problem inside itself ... Cannot bind
argument to parameter 'Source' because it is an empty string." and exit
code 2: `ConvertTo-OtterTokens` declares `-Source` mandatory, and PowerShell
refuses an empty string for it. otter-net reproduces this. Needs a decision:
an empty program most likely should run and do nothing (exit 0).
Covered by `tests/cli/run.psd1` ("an empty program").

## Q13. Network handlers in a loop do not keep the loop's pass (found 2026-10-17)

D130 says "a handler set up during a pass keeps" the pass's values. otterPS's
`Get-OtterLoopPassNames` (src/Otter.LoopPasses.psm1) gives a loop pass names
only when its body holds a "deferred" statement, from this list:

    [NodeKind]::When, [NodeKind]::WebRoute, [NodeKind]::UiEvent, [NodeKind]::MemoDef,
    [NodeKind]::Watch, [NodeKind]::Lifecycle, [NodeKind]::RouteChange,
    [NodeKind]::WatchEvent, [NodeKind]::NetworkEvent

`RouteChange` and `NetworkEvent` are not members of `NodeKind` (the nodes are
`RouteChangeEvent` and `WebSocketEvent`), so PowerShell reads both entries as
`$null`. A loop whose body sets up `on data from`, `on message from`, `on
connect of`, `on complete of` (any `NetworkEventStmt`) or `on route change`
therefore has no pass names, and every such handler reads the loop's names
live (the last pass's values). File-watcher handlers (`on change of`, a
`WatchEvent`) do keep their pass.

**Status:** otter-net copies the code (`tests/compat/net_loop_passes.ot`
shows both). Needs a decision: either the list is meant to name
`WebSocketEvent` and `RouteChangeEvent` (a fix that changes what such
programs print), or network handlers are deliberately excluded (then D130
should say so).

## Q14. `respond with VALUE with status N` is never read (found 2026-10-17)

`Read-OtterStatement`'s `Respond` case means to accept both `and status N` and
`with status N` after the value:

    if (Test-OtterTokenKind ([TokenKind]::And) -or Test-OtterTokenKind ([TokenKind]::With)) {

Without parentheses around each call, PowerShell reads this as one call of
`Test-OtterTokenKind` with extra arguments, so only `and status` is
recognised; `with status` is a syntax error ("I expected the respond statement
to end here."). otter-net copies the code. Routes only run under `otter
serve`, so `otter run` programs are affected only in what parses.

## Q15. Platform wording inside network errors (found 2026-10-17)

Several network messages end with the text of the .NET exception underneath
(`network error` / `error of request` for a refused or failed connection,
"Could not connect to tcp H on port N: ...", "The TLS handshake with H failed:
...", an asynchronous request's failure). otterPS on Windows PowerShell 5.1
shows .NET Framework's wording ("No connection could be made because the
target machine actively refused it 127.0.0.1:1"); otterPS on PowerShell 7 and
otter-net show .NET's ("Connection refused"). The Otter part of each message is
the same. otter-net cannot reproduce .NET Framework's texts without a table of
them; until that is decided, programs that print these messages differ on
Windows, and the `net_` compatibility programs only test their Otter prefix.
Synchronous `get`/`post`/`put`/`delete` and `download` are not affected for a
refused connection or an unknown host (otterPS words those itself).

## Q16. Behaviour that differs between otterPS on 5.1 and on 7 in this area (found 2026-10-17)

otter-net gives the Windows PowerShell 5.1 behaviour in each case:

- `connect securely to tcp ... using protocol P` is refused on 5.1 ("Protocol
  negotiation ... is not available on this Otter runtime (Windows PowerShell
  5.1 has no ALPN support)."); on 7 otterPS connects and silently ignores the
  protocols.
- `post gone as json to URL` (or a JSON body that is gone): `ConvertTo-Json`
  gives nothing on 5.1, so the request fails with "Exception calling ".ctor"
  with "3" argument(s): ...Parameter name: content"; on 7 it sends `null`.
- A URL that is an absolute Unix path ("/x") is "not a valid URL" on Windows;
  .NET on macOS and Linux reads it as a `file:` URL, so otterPS there says the
  URL uses "file".

## UI2 questions (found 2026-10-17, porting UI in `run`, `check`, `serve`, `desktop`)

### UI2-1. `otter web` without a file exits with 0

`otter web`, `browse`, `serve` and `desktop` with no file print
`Usage: otter <command> <script.ot>` and end with a plain `exit 1`
(otter.ps1, the UI commands' branch). Under `powershell -File` a plain
`exit` in the script does not set the process's exit code (otterPS's own
comment on `otter studio` says so), so the process exits with 0, unlike every
other usage error (`[Environment]::Exit(1)`). Measured on PowerShell 7:
`pwsh -NoProfile -File otter.ps1 web` exits 0. otter-net exits 0 too. Was 1
meant?
