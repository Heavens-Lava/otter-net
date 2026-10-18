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

## Q2. D99 queries ship in 1.0.1 although D99 is deferred (found 2026-10-17)

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

## Q3. D97 databases on macOS and Linux (found 2026-10-17)

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
