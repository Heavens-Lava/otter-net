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

## Q2. Network handlers in a loop do not keep the loop's pass (found 2026-10-17)

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

## Q3. `respond with VALUE with status N` is never read (found 2026-10-17)

`Read-OtterStatement`'s `Respond` case means to accept both `and status N` and
`with status N` after the value:

    if (Test-OtterTokenKind ([TokenKind]::And) -or Test-OtterTokenKind ([TokenKind]::With)) {

Without parentheses around each call, PowerShell reads this as one call of
`Test-OtterTokenKind` with extra arguments, so only `and status` is
recognised; `with status` is a syntax error ("I expected the respond statement
to end here."). otter-net copies the code. Routes only run under `otter
serve`, so `otter run` programs are affected only in what parses.

## Q4. Platform wording inside network errors (found 2026-10-17)

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

## Q5. Behaviour that differs between otterPS on 5.1 and on 7 in this area (found 2026-10-17)

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
