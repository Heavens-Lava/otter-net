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
