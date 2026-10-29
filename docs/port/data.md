# Port inventory: data, console and remaining core-library forms

Area owner: the Data agent (see `docs/PORTING.md`, area "Data").
Reference: otterPS 1.0.1. Line numbers are otterPS 1.0.1's.
Status as of 2026-10-17, branch `port/data` (based on `port/native` `befb087`).

Legend: **native** = parsed and run in C#; **partial** = some variants
native; **missing** = throws `Unsupported`, so the program falls back to
otterPS before anything runs.

Where things live in otter-net:

| Part | File |
|---|---|
| Parser (databases, queries, `the`) | `src/Otter.Language/Parser.Data.cs` |
| Syntax nodes | `src/Otter.Language/Syntax.Data.cs` |
| Statement dispatch | `src/Otter.Runtime/Interpreter.Data.cs` |
| Query to SQL (D99) | `src/Otter.Runtime/Queries.cs` |
| Provider logic, checks and messages (`Otter.Database.psm1`) | `src/Otter.Runtime/Database.cs` |
| SQLite through `LibraryImport` | `src/Otter.Runtime/Sqlite.cs` |

## Databases (D97, D98)

| Capability | otterPS source | Status | Tests |
|---|---|---|---|
| `connect CONFIG into db` (`ConnectDbStmt`); CONFIG a thing or a path text | parser 4693 (`connect`, after the `to`/`securely` networking forms); interp 2195; `Connect-OtterDatabase` 759, `Open-OtterSqliteConnection` 308 | native | `data_db`, `data_db_checks`, `data_db_errors` |
| `disconnect db` (`DisconnectDbStmt`), rolls back an open transaction | parser 4700; interp 2203; `Disconnect-OtterDatabase` 794, `Close-OtterSqliteConnection` 368 | native | `data_db`, `data_db_errors` |
| `query db with "SQL" [parameter ... block] into rows` (`DbQueryStmt`) | parser 4707; interp 2210; `Invoke-OtterSqliteQuery` 469, `Bind-OtterSqliteParameters` 426 | native | `data_db`, `data_db_fatal`, `data_syn_db` |
| `execute db with "SQL" [parameter ... block] [into result]` (`DbExecuteStmt`) | parser 4762; interp 2232; `Invoke-OtterSqliteCommand` 540 | native | `data_db`, `data_db_errors` |
| `begin transaction on db into tx`, `commit tx`, `rollback tx` | parser 4818-4851; interp 2250-2270; 587-642, 871-943 | native | `data_db`, `data_db_errors` |
| `get tables from db into t` (`GetTablesStmt`) | parser 3691; interp 2272; `Get-OtterSqliteTables` 644 | native | `data_db` |
| `get columns from TABLE in db into c` (`GetColumnsStmt`) | parser 3703; interp 2286; `Get-OtterSqliteColumns` 691, `Get-OtterPortableColumnType` 669 | native | `data_db`, `data_db_errors` |
| `rows affected of r`, `last inserted id of r`, `primary key of c`, ... | already native (two-word properties) | native | `data_db` |
| SQLite library missing | otterPS: `Add-Type` + `DllImport("winsqlite3.dll")`, always present on Windows | native: runtime error `I could not load SQLite (<library>), which Otter needs to use a database.` | none (cannot be produced in CI) |

How it matches otterPS (except for the five S-7 corrections of Phase 2a,
`docs/SECURITY_LEDGER.md`, marked **S-7** below):

- Values: SQLite integers become numbers (double), reals numbers, text
  text, NULL `gone`. otterPS reads a blob as its text up to the first zero
  byte; **S-7:** otter-net reads it as bytes, and refuses a whole number a
  double cannot hold exactly (otterPS rounds it).
- Parameters bind by name to `name`, `@name`, `:name` or `$name`; names
  ignore case (a PowerShell hashtable), unused ones are ignored; `gone` binds
  NULL, true/false 1/0, a whole number an integer, other numbers a real,
  anything else PowerShell's `[string]` of it (**S-7:** bytes bind as a
  BLOB; otterPS binds the text "OtterBytes").
- otterPS runs only the first statement of the SQL text (one
  `sqlite3_prepare_v2`; the tail is ignored). **S-7:** otter-net refuses
  text holding a second statement before anything runs. `execute` steps
  its statement once.
- **S-7:** a connection waits up to 5 s for another connection's lock
  (otterPS sets no busy timeout and fails at once).
- Every check otterPS makes, in its order, with its words; a mandatory
  PowerShell parameter that is `gone` or empty (`query gone with ...`,
  `query db with "" ...`, a provider of `gone`) fails as otterPS does, as an
  internal problem (`Cannot bind argument to parameter 'TargetValue' because
  it is null.`).
- The `database connection`, `database transaction`, `database row`,
  `database result`, `database table` and `database column` things have
  otterPS's properties, including the hidden `__connection` and
  `__transaction`.
- The library is `winsqlite3.dll` on Windows (as otterPS),
  `/usr/lib/libsqlite3.dylib` on macOS and `libsqlite3.so.0` on Linux,
  resolved by `NativeLibrary.SetDllImportResolver` for the name
  `otter-sqlite3`. Native AOT compatible; `AllowUnsafeBlocks` is on in
  `Otter.Runtime.csproj` because `LibraryImport` generates unsafe code.
  Only one resolver can be set per assembly: if another area needs one for
  `Otter.Runtime`, the two must be merged.

## Queries (D99: deferred from Otter 1.0, but its code ships in 1.0.1)

Ported as the code is, experimental status noted in `docs/SPEC-QUESTIONS.md`
(Q10, Q11).

| Capability | otterPS source | Status | Tests |
|---|---|---|---|
| `get [distinct] all/FIELDS from TABLE in DB [as alias] [with] [where ...] [order by ... [then ...]] [take/limit N] [skip/offset N] into T` (`QueryStmt`), including clauses on indented lines | parser 1947 (`Test-OtterIsQueryStatement`), 2119 (`Read-OtterQueryStatement`), 2010-2117; interp 2301 | native | `data_query`, `data_query_checks`, `data_syn_query` |
| `count all` / `count/sum/total/average/avg/minimum/min/maximum/max [of] FIELD from TABLE in DB [where ...] into T` (`QueryAggregateStmt`) | parser 2263 (`Test-OtterAggregateStatementAhead`), 2305; interp 2396 | native | `data_query`, `data_query_checks` |
| `X is between A and B` (`QueryBetweenExpr`), `X is [not] in LIST` (`QueryInExpr`), `contains`, `starts/ends with` in a where clause | parser 2034-2085; interp 1403-1530 (`ConvertTo-OtterQuerySqlExpression`) | native | `data_query` |
| `is in` / `is not in` / `is between` in an ordinary condition | otterPS returns the left side and the caller reports `I expected the statement to end here. I found 'is in' instead.` | native (the same syntax error) | `data_in_conditions` |
| `query parameter "x"` (`QueryParameterExpr`) | parser 585 | missing | - | **UI agent** (SPA routing, D103), not a database form |

## Remaining core-language forms

| Capability | otterPS source | Status | Tests |
|---|---|---|---|
| `the PROPERTY of X` as a value | parser 406 (`Read-OtterValue`); `-PropertyTarget` reads at 455, 499, 858-861, 889, 908, 923, 1118, 3776-3796, 4485, 4495, 5027, 5097, 5116 | native (the network forms at 778-794 take `propertyTarget: true` when the Network agent ports them) | `data_the`, `data_syn_the` |
| `the PROPERTY of X is V` as an assignment | parser 2594 | native | `data_the`, `data_syn_the` |
| `the` as an ordinary name elsewhere (`the is "x"`, `say the`, `the of thing`) | - | native | `data_the` |
| `the` before a resource name (`Read-OtterOptionalTheBeforeName`, parser 1405) | used only by `start` (server), `put`, `show`, `create ... into`, `lock`/`restart`/`shut down the computer` and `when` (refuses `the`) | **other areas** (Network, UI, System) | - |
| `say X in color "red"`, `ask secretly`, `larger of`/`smaller of`, random numbers and seed, hashing, XML, `format ... as` | various | native (already on `port/native`) | existing `tests/compat` |

## CSV (D98) and console UX (D100)

All native on `port/native` already (`read csv`, `write csv`, `convert ... to/from
csv`, `set cursor`, `show progress`, `choose from`, `console is interactive`);
tests `csv.ot`, `console.ot`, `syn_console.ot`.

## Shared files edited (minimal)

- `src/Otter.Language/Parser.cs`: `ReadValue(bool propertyTarget = false)`
  skips the article first (`SkipReadabilityArticle`); `propertyTarget: true`
  at otterPS's `-PropertyTarget` reads; `the` removed from the
  special-statement and special-value word lists; aggregate queries
  (`IsAggregateQueryAhead`, the `count`/`sum`/`average`/`minimum`/`maximum`
  cases) and `get` queries (`IsQueryStatementAhead`) call the readers in
  `Parser.Data.cs`; `get tables`/`get columns` read by `TryReadSchemaGet`;
  the `Unsupported` for `is in`/`is between` in conditions and for `get`
  followed by a new line removed.
- `src/Otter.Language/ReservedWords.cs`: the `into` targets of the database
  and query statements are checked (D-2), as otterPS's generic walk does.
- `src/Otter.Runtime/Otter.Runtime.csproj`: `AllowUnsafeBlocks`.

## Verification

- macOS, PowerShell 7: otterPS cannot load `winsqlite3.dll`, so every
  program that opens a database differs from the reference there (otterPS
  stops with `Exception calling "sqlite3_open_v2" ...`). Windows CI compares
  them. Programs whose checks happen before SQLite is used
  (`data_db_checks`, `data_db_internal`, `data_query_checks`, the syntax
  tests, `data_the`, `data_in_conditions`) match the reference on macOS.
- To check the SQLite behaviour on macOS, a scratch copy of otterPS 1.0.1
  with `winsqlite3.dll` replaced by `/usr/lib/libsqlite3.dylib` in
  `Otter.Database.psm1` (the reference itself untouched) gives the same
  output as otter-net for every `data_*` program. SQLite's own error texts
  could differ between Windows' winsqlite3 version and macOS's.
- `data_query_internal`: the .NET wording of PowerShell's `[int]` failure
  differs between PowerShell 7 (`The input string 'many' was not in a
  correct format.`) and Windows PowerShell 5.1 (`Input string was not in a
  correct format.`); otter-net gives the 5.1 wording.
