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

## CLI questions (found 2026-10-17, porting the rest of the command line)

### C2-1. PowerShell's own errors name otter.ps1's path and depend on the console

When PowerShell itself rejects otter.ps1's command line (a parameter binding
failure such as `otter -Deb`, `otter a.ot -Port abc`) or fails inside
otter.ps1 (`otter debug a.ot -Breakpoints 1,x`), powershell.exe -File writes
its error record in the default view on the error stream and exits with 1:

    C:\Users\me\AppData\Local\Otter\1.0.1\otter.ps1 : Parameter cannot be processed because ...
        + CategoryInfo          : InvalidArgument: (:) [otter.ps1], ParentContainsErrorRecordException
        + FullyQualifiedErrorId : AmbiguousParameter,otter.ps1

The first part is otter.ps1's full path, which otter-net, having no
otter.ps1, takes as `otter.ps1` next to its own executable (where the
installer puts otterPS's). The two `+` lines are cut into pieces of the
console width less 6; otter-net assumes the standard 120 columns and, as
measured, wraps every line of the view at that width (F5). Standard output and the exit code are not affected. Needs
a decision: are these texts part of the behaviour to keep (they are
PowerShell's, not Otter's)? PowerShell 7 shows them differently again
("otter.ps1: Parameter cannot be processed ..."), so every such case in
`tests/cli/binding.psd1` is a known PowerShell 7 difference.

### C2-2. `otter -v` prints the version (Q3 is mistaken)

Q3 says `otter -v` is PowerShell's "ambiguous parameter" error. PowerShell's
binder (MergedCommandParameterMetadata.GetMatchingParameter, the same in 5.1
and 7) prefers a script's own parameter over common parameters when a prefix
matches several, so `-v` binds `-VersionFlag` and prints the version; `-o`
is `-Open`, `-he` is `-HelpFlag`. Ambiguity errors remain where several of
otter.ps1's own parameters match (`-Deb`, `-p`) or only common ones do (`-i`,
`-e`). otter-net follows the binder.

### C2-3. A lone "-" argument

Windows PowerShell 5.1's `-File` treats every argument starting with a dash
as a parameter name, so `otter a.ot -` passes a parameter with an empty name,
which PowerShell's binder refuses with an argument error ("Cannot process
argument because the value of argument "name" is not valid ...", exit 1).
PowerShell 7 passes "-" as an ordinary argument (the program sees it).
otter-net gives the 5.1 behaviour, read from PowerShell's source; it could
not be run on Windows PowerShell 5.1 here. A program that wants "-" (for
standard input, say) cannot get it on 5.1. Needs a decision.

### C2-4. `otter web` without a file exits with 0

(The same finding as UI2-1.)

otter.ps1 ends `otter web` / `browse` / `serve` / `desktop` without a file
with `exit 1`, but under powershell.exe -File that `exit` reaches the
process as 0 (otter.ps1 notes the same thing for `otter studio` and uses
[Environment]::Exit there). otter-net exits with 0 as otterPS does. Needs a
decision: the usage error most likely should be exit code 1, as for every
other command.

### C2-5. -Verbose and -InformationAction change otterPS's output

otter.ps1 is an advanced script, so PowerShell's common parameters apply to
it. Two of them change what otterPS shows:

- `-Verbose` (or `-vb`): every Import-Module otterPS makes while a command
  runs lists the modules it loads and the functions it imports and exports
  ("VERBOSE: Loading module from path '...src\Otter.Profiler.psm1'.",
  "VERBOSE: Importing function ..."), with otterPS's install path, and its two
  Write-Verbose calls show ("Otter Web App compiled to: <page>", "Otter web
  server listening on http://<host>:<port>/"): `otter run` of a program
  (its compiled engine, unless OTTER_ENGINE=interpreter), `otter profile`,
  `otter debug`, `otter build` and `otter publish` (the web compiler),
  `otter web`/`browse`, `otter serve` and `otter desktop`.
- `-InformationAction Ignore` and `Stop` make every Write-Host of otterPS
  fail (F5-2); `Inquire` asks before each one (F5-1).

otter-net now reproduces -Verbose and -InformationAction Stop and Ignore,
measured on Windows PowerShell 5.1 (F5-2, F5-3); Inquire is still handed to
the PowerShell Otter (F5-1). They are accidents of PowerShell, not Otter
features; needs a decision whether to keep them or to drop them. The other
common parameters (-Debug, -ErrorAction, -WarningAction, -ErrorVariable,
-OutVariable, ...) are bound and validated as PowerShell does and change
nothing visible.

### C2-6. Texts read from PowerShell's and .NET Framework's sources

These could not be checked against Windows PowerShell 5.1 here (the
reference runs on PowerShell 7 on macOS); otter-net takes them from
PowerShell's source (v6.0.0-alpha.9, the 5.1 code base) and .NET
Framework's resources:

- `otter -?` prints otter.ps1's syntax line (Get-Help); PowerShell 7's text
  is reproduced; 5.1 may wrap it at the console width.
- [int] conversion failures: "Input string was not in a correct format.",
  "Index was outside the bounds of the array." (a blank number), and
  ActionPreference without `Break`; `-ErrorAction Suspend`: "The error action
  preference of "Suspend" is supported only on workflows."
- A manifest that is not valid JSON: JavaScriptSerializer's messages ("Invalid
  JSON primitive: x.", "Invalid object passed in, ':' or '}' expected. (12):
  ...") and PowerShell 5.1's "Cannot convert the JSON string because a
  dictionary that was converted from the string contains the duplicated keys
  'a' and 'A'." PowerShell 7 uses Newtonsoft.Json, whose messages (and what
  it accepts: a trailing comma) differ.

### C2-7. PowerShell 7 differences on the command line

Besides the error texts (C2-1, C2-6): PowerShell 7's `-File` turns
`-Switch:true` / `-Switch:$false` into Booleans (5.1 passes the text, which a
switch refuses), passes a lone "-" as an argument (C2-3), and has the extra
common parameter `-ProgressAction` (so `-p` lists it among the matches).
otter-net gives 5.1's behaviour.

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

### UI2-2. `otter serve` answers with the statement's node text, not a value

`Invoke-OtterServerRoute` (src/Otter.Server.psm1) never runs the program: a
route's first `respond with` gives its value only when it is a literal.
Anything else (`respond with name`, `respond with 1 + 2`) is cast with
`[string]`, which is otterPS's `Node.ToString()`: the client receives
`Variable@L3` or `Math@L5`. Status codes that are not literals are 200.
`say` and every other statement in a route never run. otter-net does the
same. Was a running route body intended (D51)?

### UI2-3. `otter serve`'s JSON differs between Windows PowerShell 5.1 and 7

`respond with V as json` is `ConvertTo-Json -Compress`: on 5.1 a number is
written as `42` and `< > & '` in text as `<` ...; on PowerShell 7 `42.0`
and the characters as they are. otter-net writes the 5.1 form (measured
against PowerShell 7 on macOS: these two are the only differences).

### UI2-4. A derived value is worked out in the scope that reads it

`OtterEnvironment.Get` passes its own environment (`$this`) to the derived
evaluator, so `derive doubled is count times 2` read inside a function that
has a local `count` uses the function's `count`, and the value is then
cached for every reader until something it read changes. A derived value
also subscribes to what it read only once it has been read: a watcher set
up before the first read runs before the derived value is marked dirty, and
sees the old value. otter-net copies both (tests/compat/ui_run_state.ot).
Were they meant?

### UI2-5. `otter run` of a declarative page shows nothing

In `otter run`, a `page`/`card`/... block only runs the statements nested
in it (`UiElement` case); its properties, events and animations are ignored,
and no window opens (`Show-OtterDeclarativeAppWpf` is never called). A
program written for `otter web` therefore runs silently with exit 0, while
`put`/`show` fail and `route`/`go to` statements and `current route` fail
with "I do not know how to run a Route statement yet." otter-net copies
this. Should `otter run` say that the program is a web application?

## P2-1. Remote commands outside Windows end in an internal error (found 2026-10-17)

Every other Windows-only statement starts with `Assert-OtterWindowsFeature`
("... is only available on Windows, and this program is running on
macOS/Linux."). `RunRemoteCommand` does not: off Windows it checks the
credential name, then `Get-OtterCredentialStorePath` joins
`$env:LOCALAPPDATA`, which is not set on macOS and Linux, and the program
stops with "Otter hit a problem inside itself ... Cannot bind argument to
parameter 'Path' because it is null." (with `LOCALAPPDATA` set, it creates
`Otter/Credentials` there and says there is no such credential, since
nothing can be decrypted off Windows). otter-net copies this
(`tests/compat/p2_remote_command.ot`). Needs a decision: give remote commands
the usual Windows-only message, or leave it.

## P2-2. `get event log entries` takes a wildcard log name (found 2026-10-17)

D79 describes reading one named log. otterPS passes the name to
`Get-WinEvent -LogName`, which treats `*`, `?` and `[...]` as wildcards: `get
event log entries from "*" up to 5 into e` reads the newest events of every
log together, skipping debug and analytic logs, and a log name with an
unbalanced `[` stops with "The specified wildcard character pattern is not
valid". otter-net does the same. Needs a decision whether that is part of
the language or an accident of the implementation.

## P2-3. An event without a description has no message (found 2026-10-17)

Get-WinEvent starts each event's Message at "Cannot retrieve event message
text." and then replaces it with `FormatDescription()`, which is `$null`
when the provider's metadata or the message is missing, so such entries
have `message` gone (and `level` gone when the provider is unknown), not the
"The description for Event ID ... cannot be found" text Event Viewer shows.
otter-net follows the code. To be confirmed on Windows PowerShell 5.1, whose
Get-WinEvent may differ from the PowerShell 7 source this was read from.

**Measured (2026-10-20)** on Windows PowerShell 5.1
(`tests/windows-state/w5_event_log.ot`, win-verify run 39462260477): an
event from a classic source whose message file is missing, and one whose
source was removed after writing, both have `level` and `message` gone
(the source stays). otter-net stopped the program with "The system cannot
find the file specified." for the first: `EvtFormatMessage` reports
ERROR_FILE_NOT_FOUND there, which .NET Framework turns into an
EventLogNotFoundException that Get-WinEvent swallows. Fixed in
`EventLog.cs` (run 39463793352: identical). Audit events (success and
failure) have the level "Information" in both. See also W5-3.

## T3 questions (found 2026-10-18, porting the remaining tail)

### T3-1. How Win32_Printer turns the spooler's status into PrinterStatus

`get system information "printers"` (D83) reads `Win32_Printer.PrinterStatus`
and words it with otterPS's table (1 other, 3 idle, 4 printing, 5 warming up,
6 stopped printing, 7 offline, else unknown). WMI's provider works the code
out from the spooler's `PRINTER_INFO_2.Status` flags, and that mapping is not
documented. otter-net (`src/Otter.Runtime/Printers.cs`) reads the spooler
directly and maps: no flag is idle (3, the well-known value for a ready
printer); `PRINTER_STATUS_OFFLINE` is offline (7); any flag other than
printing and warming up is a CIM `ExtendedPrinterStatus` beyond
PrinterStatus's range (paused 8, error 9, busy 10, ...), reported as other
(1); otherwise printing (4), else warming up (5). The usual case (idle) and
the empty list without a spooler are certain; the paused, error and
combined-flag cases are a reasoned reading, to be confirmed on Windows with
a paused printer. `Default` is compared with `GetDefaultPrinter`, ignoring
case.

**Measured (2026-10-20)** on a Windows Server 2025 runner with Windows
PowerShell 5.1 (`tools/windows-state.ps1`, `tests/windows-state/w5_printers.ot`;
win-verify runs 39462260477 and 39463793352, now part of ci.yml's Windows
job). Printers on the "Generic / Text Only" driver, compared with
Win32_Printer and the spooler's own flags (Get-Printer):

| Printer | Spooler status | Win32_Printer PrinterStatus (Extended) | otterPS | otter-net |
|---|---|---|---|---|
| idle, and a second one on the same port | none | 3 (2) | idle | idle |
| paused (`Pause` through CIM) | Paused | 1 (8) | other | other |
| paused, with a job waiting | Paused | 1 (8) | other | other |
| a job that failed (its port's folder removed) | Error | 1 | other | other |
| a job to an unreachable TCP/IP port | Error | 1 | other | other |
| "work offline" set (`WorkOffline`, an attribute) | none | 3 (2) | idle | idle |
| Microsoft Print to PDF | none | 3 | idle | idle |

The default printer (set with `SetDefaultPrinter`) was the only `isDefault`
true in both, and both listed the printers in the same order (the spooler's,
newest first, not alphabetical). Not reached on a runner: the spooler's
OFFLINE flag (7; a port to an unreachable address reports Error, and "work
offline" is an attribute WMI ignores), printing (4) and warming up (5),
which need a physical device. The mapping is confirmed for every state a
runner can produce; those three stay a reasoned reading.

### T3-2. Which scheduled tasks Get-ScheduledTask lists, and in what order

`get system information "tasks"` (D80) uses `Get-ScheduledTask` and
`Get-ScheduledTaskInfo` (the ScheduledTasks CIM provider). otter-net
(`src/Otter.Runtime/ScheduledTasks.cs`) reads the Task Scheduler 2.0 COM API
the provider is built on: every folder from `\` down, hidden tasks included,
a folder's tasks before its subfolders'. A time the Task Scheduler reports as
its zero date is gone (the provider's empty datetime). The order and the
never-run case are to be confirmed against Windows PowerShell 5.1
(`tests/compat/t3_system_tasks_printers.ot` prints every task name).

**Measured (2026-10-20)**, same runs as T3-1 (`tests/windows-state/w5_tasks.ot`):
223 tasks, including fourteen registered in `\`, `\OtterW5\`,
`\OtterW5\Deeper\`, `\OtterW5 Another\` and `\otterw5 lower\` (registered
out of alphabetical order; never run, without and with triggers, a trigger
only in the past, disabled, hidden, run and succeeded, run and failed,
still running). otterPS and otter-net printed the same names, states and
times in the same order: the root's tasks first, then each folder depth
first (`\OtterW5\Deeper\` before `\OtterW5 Another\`), tasks and folders
in the Task Scheduler's order, which is alphabetical without regard to case
("Otter a registered second" before "Otter z registered first"); hidden
tasks are listed. A task that never ran has `lastRunTime` "1999-11-30
00:00:00", the Task Scheduler's own "never" date, which the CIM provider
passes on (LastTaskResult 267011), not gone; a task with no future run has
`nextRunTime` gone (the COM API's zero date, 1899-12-30); a running task is
"Running", a disabled one "Disabled". Nothing to change in otter-net.
Whether a never-run task should rather show gone is a question for the
specification.

### T3-3. `otter serve` with a port [int] cannot read

Get-OtterServerDefinition (src/Otter.Server.psm1) reads a `web server`'s
`port is X` and the first `listen on port X` with `[int]`. When the cast
fails (`port is "abc"`, `port is 30000000000`, `listen on port "x"`), the
function runs with otter.ps1's `$ErrorActionPreference` (Stop) - the earlier
reasoning that a module sees only the global `Continue` was wrong - so the
error ends otter.ps1 before the server starts. Measured on Windows
PowerShell 5.1 (F5): powershell.exe reports "Get-OtterServerDefinition :
Cannot convert value "abc" to type "System.Int32". Error: "Input string was
not in a correct format."" at Start-OtterServer's call (`$def =
Get-OtterServerDefinition -Program $Program`, `src\Otter.Server.psm1` line
282), `InvalidArgument: (:) [Get-OtterServerDefinition], RuntimeException`,
`InvalidCastFromStringToInteger` (text) or `InvalidCastIConvertible` (a
number), exit code 1. otter-net (`ServeCommand.CastPort`) does the same
(`tests/cli/ps-texts.psd1`). Needs a decision whether an unreadable port
should rather be an Otter error.

## D4 questions (found 2026-10-18, porting desktop windows to the WPF host)

Each was observed on Windows PowerShell 5.1 with otterPS 1.0.1 by
`tests/desktop` (desktop.yml); otter-net does the same.

### D4-1. A failure inside a window's handler is reported as "already closed"

`Show-OtterUiResource` wraps `ShowDialog()` in a catch-all, so any failure in
a handler while the window is shown (`fail with`, an unknown property, a
`stop`) ends the message loop and becomes "This window has already been
closed, so it can't be shown again." at the `show` line; the handler's own
message is lost, and the window stays on screen, unresponsive, until the
program ends (tests/desktop/handler_error*.ot). Should the handler's error
be reported instead?

### D4-2. A change handler that fails while the program sets the text

`text of box is "x"` raises the text box's `changed` handler at once; if it
fails, PowerShell reports its own `Exception setting "Text": "..."` as
"Otter hit a problem inside itself" (tests/desktop/sync_handler_error.ot).
Similarly a card's `round` has no WPF property behind it, so setting it is
PowerShell's "The property '' cannot be found on this object..." and reading
it gives gone; a width of "Infinity" is WPF's refusal in the user's culture.
Were these meant to be Otter errors?

### D4-3. `width of x is true` is full width

The check for `full` is PowerShell's `$Value -eq 'full'`, so `true`, any
capitalisation of "full", and a list holding one of them all mean full
width, while `false` is "I expected a number ... but got false."
(tests/desktop/quirks.ot). `spread of x is false` also spreads (the value is
not read). Intended?

### D4-4. Every UI resource equals every other

`Test-OtterEqual` compares two resources as PowerShell's `[string]` of the
class, so `if b is other` is true for any two resources, and a resource
equals the text "OtterUiResource" (tests/desktop/errors.ot). Things compare
by identity (D44's own comment says resources should too).

### D4-5. A text box's foreground changes its placeholder's background

`Set-OtterUiProperty` treats every colour property alike: on a text box with
a placeholder, setting `foreground` also replaces the colour the watermark
is drawn on (and the background restored once text is typed), and on a
padded window the `foreground` paints the padding Border's background.
otter-net copies it (tests/desktop/placeholder.ot). Intended?

## V4 questions (found 2026-10-18, the source-level coverage audit)

### V4-1. `route parameter X` and `query parameter X` cannot be written

D103 adds two expressions, `route parameter "id"` and `query parameter
"search"` (RouteParameterExpr, QueryParameterExpr; `Read-OtterValue` in
src/Otter.Parser.psm1 checks for an identifier `route`, or the token `query`,
followed by the token `parameter`). But `parameter` and `query` are keywords
only as the first word of a line (D33 mechanism 2, the lexer's
`$StatementHeadKeywords`): after `route` or after any other word, `parameter`
lexes as an ordinary identifier. So no program can reach either form -
`x is route parameter "id"` is "I expected the assignment to end here. I
found 'parameter' instead.", `say route parameter "id"` says the variables
`route` and `parameter`, in otterPS 1.0.1 and in otter-net alike. The web
compiler and the run-time error for both are therefore dead code in both
implementations. Found by `tools/native-coverage.ps1`: the only NodeKinds no
program of any corpus contains. Needs a decision: make the forms reachable
(a lexer change, new behaviour) or drop them from the documented grammar.

### V4-2. `otter run` runs some programs on otterPS's compiled engine

otterPS 1.0.1's `Invoke-OtterSource` first hands a plain run to its compiled
engine (`src/Otter.Compiler.Native.psm1`, C# 5 compiled with `Add-Type`
against `src/native/OtterNativeRuntime.cs`); only a program the compiler
refuses is interpreted. `docs/OTTER_NATIVE_COMPILER_DESIGN.md` describes the
engine as experimental and not changing `otter run`'s default, but `otter.ps1`
1.0.1 enables it (`$script:OtterCompiledRunAllowed`; `OTTER_ENGINE_TRACE=1`
shows "otter engine: compiled"). Its runtime mirrors the interpreter's
messages, but not everywhere:

- `length of` bytes is refused ("... but this is bytes.");
- where .NET itself fails (a date moved past year 9999, an empty random
  range), `otherwise into` keeps .NET's message where the interpreter keeps
  PowerShell's `Exception calling "AddYears" with "1" argument(s): "..."`, and
  an uncaught failure ends the program as "Otter's compiled engine hit an
  internal problem: Exception calling "Invoke" ..." instead of "Otter hit a
  problem inside itself";
- numbers become Int32 by C#'s `(int)` cast instead of PowerShell's `[int]`:
  a random bound or a repeat count beyond Int32 is not an error, and the
  result depends on the processor and runtime (0x80000000 on Windows
  PowerShell 5.1's .NET Framework on x64, saturation on .NET 9 and later,
  so `repeat 3000000000 times` does nothing on 5.1 and runs for ever on
  PowerShell 7).

otter-net decides as otterPS does whether a run is compiled
(`OtterPsCompiledEngine.Accepts`, a port of the compiler's refusal rules) and
reproduces these differences, the .NET Framework x64 casts and the .NET
Framework message texts (read from the reference source: "Years value must
be between +/-10000.\r\nParameter name: years"); the texts are to be
confirmed on Windows by CI (`c4_compiled_engine.ot`, `c4_interpreter_dotnet.ot`).
Needs a decision: should the compiled engine's differences be part of the
language, or is the interpreter the specification?

### V4-3. `convert X to json` of a timer, xml or a network handle

`ConvertTo-OtterJsonShape` passes anything that is not a thing, a list, a
function or a type straight to `ConvertTo-Json -Depth 32`, which writes the
PowerShell object's own properties: a timer becomes the Stopwatch
(`IsRunning`, `Elapsed` with every TimeSpan field, `ElapsedMilliseconds`,
`ElapsedTicks`), xml becomes `{"Node": [...]}` (XmlNode's enumeration), and a
websocket, tcp or udp handle, an http request or a command job becomes its
PowerShell class's fields. otter-net writes such a value's type name as a
JSON text (`"System.Diagnostics.Stopwatch"`, `"OtterXml"`). The timer's
numbers change from run to run, and the other shapes are .NET runtime
internals (and differ between Windows PowerShell 5.1 and PowerShell 7), so
otter-net has not copied them. Needs a decision: refuse these values ("Otter
cannot turn ... into JSON"), or specify a shape.

## W5 questions (found 2026-10-20, verifying Windows behaviour with real machine state)

Measured on `windows-latest` runners (Windows Server 2025, Windows
PowerShell 5.1) given printers, scheduled tasks, an event log, services and
software entries by `tools/windows-state.ps1`, other regional formats, and a
150 % display scale; otterPS 1.0.1 and otter-net run back to back on the
same machine (`tests/windows-state`, `.github/workflows/win-verify.yml`, now
folded into `ci.yml` and `desktop.yml`). Results for T3-1, T3-2 and P2-3 are
recorded under those questions.

### W5-1. Network interfaces: .NET Framework and .NET list different ones

`get system information "network"` is `NetworkInterface.GetAllNetworkInterfaces()`
filtered to "up and not loopback". On the runner, Windows PowerShell 5.1
(.NET Framework) listed one interface ("Ethernet 3"); .NET 10 listed five,
adding the hidden filter interfaces ("Ethernet 3-WFP Native MAC Layer
LightWeight Filter-0000", "...-QoS Packet Scheduler-0000", "...-WFP 802.3 MAC
Layer LightWeight Filter-0000") and "Ethernet 4": .NET 5 and later ask
GetAdaptersAddresses for all interfaces. otter-net now reads the adapters on
Windows with .NET Framework's flags (`src/Otter.Runtime/NetworkAdapters.cs`;
win-verify run 39461167416: identical). otterPS on PowerShell 7 on Windows
would list the extra ones, a 5.1/7 difference like Q16.

### W5-2. List-valued registry entries in "software"

otterPS keeps `DisplayName`, `DisplayVersion` and `Publisher` as
Get-ItemProperty returns them. A REG_MULTI_SZ or REG_BINARY value is a
PowerShell array, not an Otter list: it prints as its items joined by
spaces (`1 2`, PowerShell's `[string]` of an array; otter-net printed `1, 2`
before), and counts as false when empty or when it holds one false item
(`if ($entry.DisplayVersion)`). otter-net now gives that text
(`SystemInfo.cs` `SoftwareValue`, run 39461167416: identical), so printing
and truthiness match; `length of` or `for each` over such a value would treat
the PowerShell array differently from otter-net's text. Numbers (REG_DWORD,
REG_QWORD) and REG_EXPAND_SZ (expanded) already matched. Needs a decision
whether software values should be plain text.

### W5-3. An event whose message is empty shows uninitialised characters

An event written with an empty message (`EventLog.WriteEntry(source, "")`)
comes back from Windows PowerShell 5.1's Get-WinEvent with a `Message` of
three characters that change from run to run ("エ䕔ǜ", "ⵘ쨦Ƙ" in two runs,
39461167416): .NET Framework's EventLogRecord formats the empty description
into a buffer it never initialises. otterPS prints those characters;
otter-net prints an empty text (what PowerShell 7's .NET returns). It cannot
be compared, so `tools/windows-state.ps1` writes no such event. Needs a
decision (empty text is the evident intent).

### W5-4. The user's regional format reaches the output in a few places

With German formats (`Set-Culture de-DE`; display language English) the whole
of `tests/compat` (209 programs) and the desktop tests (26) were compared
(run 39459678298). One difference: Windows PowerShell writes the number in
its conversion errors with the user's formats ("Cannot convert value
"2147483647,5" to type "System.Int32"", `c4_interpreter_dotnet.ot`), where
otter-net, running with invariant globalization, wrote "2147483647.5". Task
and event times (`ToString('yyyy-MM-dd HH:mm:ss')`, whose `:` is the culture's
time separator) and WPF's refusal of an infinite width ("'∞' is not a valid
value") depend on it in the same way. otter-net now reads the user's number
and time symbols from Windows (`Otter.Language.UserFormats`, GetLocaleInfoEx,
the data .NET Framework uses) and uses them in exactly those places;
everything else Otter prints stays invariant, as otterPS's own formatting
does (D32.5). After the change the whole of `tests/compat` is identical
under German formats (run 39469496133; only `p2_remote_command.ot`, which
is handed to the PowerShell Otter by design, cannot run natively), and
ci.yml now compares the machine-state and conversion programs under German
and Finnish formats (run 39469494984: 11 of 11 identical in each),
desktop.yml the error programs under German formats (run 39469499072). Needs a decision whether a program's
output should depend on the user's regional format at all.

### W5-5. On a non-English display language otterPS's PowerShell and .NET texts are translated

otterPS shows some texts that PowerShell and .NET Framework write (C2-6,
D4-2, every "Cannot convert value", file and event log errors). The runner
has the German resources of both (`System.Management.Automation.resources`
and `PresentationFramework.resources`, de). Running otterPS with its thread's
UI culture set to de-DE (`desktop-harness --reference-ui-culture de-DE`, run
39459678298) gives, for `tests/desktop/errors.ot` and
`sync_handler_error.ot`:

    29: Die Eigenschaft "" wurde für dieses Objekt nicht gefunden. Vergewissern Sie sich, dass die Eigenschaft vorhanden ist und festgelegt werden kann.
    31: Ausnahme beim Festlegen von "Width": ""∞" ist kein gültiger Wert für die Eigenschaft "Width"."
    caught: Ausnahme beim Festlegen von "Text": "no changes allowed"

where otter-net writes PowerShell's English texts ("The property '' cannot be
found on this object...", "Exception setting "Width": "'∞' is not a valid
value for property 'Width'.""). Otter's own messages are English in both.
What could not be verified: otter-net under a real German display language.
`Set-WinUILanguageOverride de-DE` succeeds on the runner, but takes effect at
the next sign-in, which a hosted runner never has (new processes still
report en-US), and .NET offers no other way to give a process another UI
language. otter-net does not translate any text, so on a German Windows
these texts, and every other PowerShell or .NET text otterPS passes on,
would differ. Needs a decision: is otterPS on an English display language
the reference (otter-net's position), or should these texts follow the
display language (a table of PowerShell's and .NET Framework's translations
for each text otter-net reproduces)?

### W5-6. High DPI: verified at a 150 % monitor scale, not at a 150 % system DPI

`tools/set-display-scale.ps1` sets the runner's display to 1920x1080 at
150 % without signing out (as the Settings app does): the monitor reports
144 dpi, while the session's system DPI stays 96 until the next sign-in.
All 26 desktop tests are identical at that scale (window trees, sizes in
physical pixels, snapshot pixels; runs 39459678298, 39465229800), so
powershell.exe and otter-desktop.exe are scaled the same way. A session that
starts at 150 % (system DPI 144) needs a sign-in, which hosted runners do
not offer; it remains unverified.

## F5 questions (found 2026-10-20, measuring the texts PowerShell itself prints)

The last places otter-net handed to the PowerShell Otter because the text
is PowerShell's own were measured on Windows PowerShell 5.1 in CI
(`.github/workflows/cli-windows.yml`; `tools/compare-cli.ps1
-ShowReference` prints the reference's raw output) and reproduced natively;
`tests/cli/ps-texts.psd1` holds the cases. The positions PowerShell quotes
are lines of otter.ps1 and its modules (`tools/New-PowerShellSiteTable.ps1`),
named with the PowerShell Otter's folder as in C2-1. Every line of an error
view is wrapped at the console width (a word longer than a line fills the
current line first; a VERBOSE line starts it on a line of its own).

### F5-1. -InformationAction Inquire stays with the PowerShell Otter

Measured with standard input redirected: each Write-Host shows its text,
then reads a line. Y, A or an empty line goes on (A does not carry over to
the next Write-Host); H stops ("The running command stopped because the user
selected the Stop option."); S opens a nested prompt ("PS <folder>>> ") that
runs PowerShell commands until `exit`; ? and anything else ask again; the
end of input fails with "Object reference not set to an instance of an
object.". Each line read is echoed to standard output, but the question
itself goes to the console, not to standard output, so a redirected run
cannot show it and a run on a console cannot be measured in CI. The nested
prompt runs PowerShell, which otter-net cannot do. otter-net therefore hands
`-InformationAction Inquire` to the PowerShell Otter, as before. Needs a
decision with C2-5.

### F5-2. -InformationAction Stop and Ignore

Every Write-Host of otterPS fails - otter.ps1's own, its modules' (`otter
build`'s "Building ...", the profile report, `otter desktop`'s "running"
line) and the program's `say`: Stop writes the text and then throws "The
running command stopped because the preference variable
"InformationPreference" or common parameter is set to Stop: <text>"; Ignore
writes nothing and throws "The value Ignore is not supported for an
ActionPreference variable. ... has been replaced by the default value.", and
PowerShell puts the default back in the scope of that Write-Host. The failure
then goes where otterPS sends a failure: an `otherwise` of the program, then
Invoke-OtterFile's catch (Show-OtterFailure, whose first Write-Host fails in
turn), the catch of `otter new`/`test`/`build`/`publish`, or the end of
otter.ps1, where powershell.exe reports the Write-Host that failed last (exit
code 1). Under Ignore the catch of `otter new` shows its message, because the
Write-Host that failed ran in otter.ps1's own scope. Nothing that runs after
the first Write-Host happens: no browser opens for `otter web`, a server
stops at once, `otter desktop` stops after its window opened (as otterPS
does, the window stays). PowerShell 7 differs: Ignore hides every Write-Host
silently and Stop's report is its ConciseView (known differences in
`tests/cli/ps-texts.psd1`). Needs a decision with C2-5.

### F5-3. -Verbose shows the PowerShell Otter's module lists

The VERBOSE lines are measured lists (`src/Otter.Cli/VerboseImports.g.cs`):
six Import-Module calls (the compiled engine, profiler, debugger, web
compiler, web server and desktop modules), the same in every case measured,
with the paths of the PowerShell Otter's files. They are printed where otterPS
imports (before a run; at the start of `otter build` and of publish's build
step; before the web page, the server or the desktop app), and the web
compiler only once. otter-net names the files beside the PowerShell Otter's
otter.ps1 (C2-1). A build's "Otter Web App compiled to:" names its staging
folder, whose name is random in both. Needs a decision with C2-5.

### F5-4. `otter serve` of an empty program, and a server that cannot start

An empty program stops at `ConvertTo-OtterTokens -Source ''` outside the
catch: PowerShell's parameter binding error, as for otter.ps1's own
parameters (no position, exit code 1; see Q12). A server that cannot start
ends otter.ps1 with PowerShell's report of the .NET method call at
`$session = Start-OtterServer ...`, in .NET Framework's words: a prefix
registered by someone else ("Failed to listen on prefix 'http://localhost:
8080/' because it conflicts with an existing registration on the machine."),
Windows' text for any other HTTP.sys error without its full stop ("The
parameter is incorrect": a host with a space or a colon, port 0, port 70000),
and `Exception calling "Add"` with "Parameter name: uriPrefix" on a line of
its own for an empty host. Off Windows otter-net uses .NET's own text (the
reference there is PowerShell 7, whose view differs anyway). Needs a decision:
should a server that cannot start be an Otter error?

### F5-5. `otter desktop` failures that end otter.ps1

Start-OtterDesktopApplication runs with otter.ps1's `$ErrorActionPreference`
(Stop), so three failures end otter.ps1 with powershell.exe's report: an
import problem from Resolve-OtterModuleSource (missing, cyclic, a folder,
unreadable, or in other capitals), reported at its `throw` in
`src\Otter.Module.psm1` with "OperationStopped: (:) [], OtterError" and the
message as the error id; a page that cannot be written (Set-Content's
"Access to the path '...' is denied." at `src\Otter.Web.psm1` line 3059 for the
page, `src\Otter.Desktop.psm1` line 1681 for the session copy); and a bridge
that finds no free port among the 20 it tries ("Could not bind terminal bridge
listener on port N.", at its `throw` in `src\Otter.Desktop.psm1`). The first
two are measured; the third cannot be provoked (the 20 ports follow a port
the system has just found free) and is rendered the way the measured
OtterErrors are. Nothing is opened in any of them. Needs a decision: should
these be Otter errors?

### F5-6. How deep UI resources can be nested in a web page

otterPS's web compiler renders a resource put inside another recursively.
On Windows PowerShell 5.1 a chain of 1,609 nested resources renders and
deeper ones end in "Otter hit a problem inside itself ... The script failed
due to call depth overflow." - but not at a fixed depth: bisecting the same
programs in CI, a chain of 1,610 rendered in one run and overflowed in the
next, and `otter build` (which calls more PowerShell functions on the way)
overflows at the same depth as `otter web`, so the limit is the thread's
stack, not a count of calls. It may move with the Windows and .NET Framework
version or a 32-bit PowerShell. otter-net renders up to 1,500 levels natively
(measured to render in every run) and hands a deeper page to the PowerShell
Otter, the one case of this batch that cannot be reproduced exactly (a cycle
is still native: otterPS always overflows). Needs a decision: should such a
page be an Otter error (or render) at any depth?

