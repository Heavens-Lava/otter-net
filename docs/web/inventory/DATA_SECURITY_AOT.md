# Otter web backend audit: data, security, Native AOT

Date: 2026-10-22. This audit is read-only. Nothing in `otter-net` or `otterPS-1.0.1` was modified.

**Binary used:** `the otter-net 1.0.1 Native AOT binary (macOS arm64)`. It is Native AOT, Mach-O arm64, 11,201,512 bytes, and prints `Otter 1.0.1`. All samples are in `docs/web/samples/data/`, and every run used `perl -e 'alarm 20; exec @ARGV'`.

**Legend:**
- **[V]**: verified with the binary or by building in scratch.
- **[R]**: read in the repository source or docs.
- **[G]**: general .NET knowledge. It was not verified here.

Short paths:
- `ON` means `otter-net`.
- `PS` means `otterPS-1.0.1`.

---

## 0. Headline findings

1. **The SQLite surface is parameterized and has transactions. It has no migrations, and the release scope matrix lists it as DEFERRED.**
   - D97 raw SQL works on macOS with the operating system's SQLite 3.51.0 [V]: connect, query, execute, `parameter`, transactions, and D98 schema calls.
   - The release scope matrix still says "Database / SQL Integration: DEFERRED (1.2+)" [R]. See `PS/docs/OTTER_1_0_RELEASE_SCOPE_MATRIX.md:26`.
   - The surface manifest gives the D97 nodes `status: candidate` and `boundaryStatus: unresolved` [R]. See `PS/release/otter-1.0-surface.json`, for example `N-DbQuery`.
   - The D99 query language is explicitly experimental and not part of 1.0 [R]. See `PS/SPEC-DECISIONS.md:7368`.
2. **The SQLite provider has real defects that matter for a web backend.** All were confirmed with the binary:
   - `execute` runs only the first SQL statement, and the rest is silently dropped.
   - A `bytes` parameter is stored as the text `"OtterBytes"`, so the data is lost.
   - BLOBs are read back as text.
   - Integers are returned as doubles, so they lose precision above 2^53.
   - No busy timeout is set by default.
   - S-1 (high severity): a security vulnerability in the experimental query language (D99), also present in the released otterPS 1.0.1. Technical details are withheld until a coordinated fix is available (`docs/SECURITY_LEDGER.md`). `LIKE` wildcards in values are not escaped (S-2).
3. **Password hashing already exists:** `hash password P and call it H` and `password P matches hash H`, using PBKDF2-SHA256 with 600,000 iterations and constant-time comparison [V]. Secure random, AEAD-style encryption (AES-CBC plus HMAC), HMAC and `securely equals` also exist [V].
4. **Several things a web backend needs are missing:**
   - HTML escaping, URL encoding, string interpolation and templates [R][V]. D8 says there is "no interpolation syntax".
   - Time zones and UTC [R].
   - ISO-8601 JSON dates. Dates serialize as `{"Value":"\/Date(ms)\/","HasTime":true}` [V].
   - A cross-platform secret store [V].
   - Concurrency: one interpreter thread, with a polled event loop [R].
5. **Native AOT:** otter-net has **zero PackageReferences** [R]. It loads the operating system's SQLite through `LibraryImport` plus a `NativeLibrary` resolver [R].
   - Scratch builds with the official .NET SDK 10.0.402 and runtime 10.0.13 (osx-arm64, Native AOT, TreatWarningsAsErrors, no warnings) gave these sizes [V]:

     | Build | Size (bytes) |
     |---|---|
     | Hello world | 1,102,744 |
     | `HttpListener` + System.Text.Json source generation + server-side WebSocket + PBKDF2 | 4,138,968 |
     | ASP.NET Core `CreateSlimBuilder` minimal API (Kestrel) + source-generated JSON | 9,003,368 |

---

## 1. Databases

### 1.1 D97 raw SQL: status partial

The code is implemented and works on all three operating system families. Its 1.0 release status is DEFERRED or candidate.

**Syntax, verified** (`db1.ot`, `pragma.ot`, `misc.ot`) [V]:

```otter
config has
    provider is "sqlite"
    connection is "audit.db"          # or ":memory:"; optional: readonly is true
.
connect config into db                # shorthand: connect "audit.db" into db
execute db with "create table users (id integer primary key, name text, age integer)"
execute db with
    "insert into users (name, age) values (@name, @age)"
    parameter "name" is evil
    parameter "age" is 42
into result                           # `into` optional
say rows affected of result           # also: last inserted id of result
query db with
    "select id, name from users where name = @name"
    parameter "name" is evil
into rows                             # list of `database row` things; NULL -> gone
query db with "select count(*) as n from users" into c   # single-line form: no parameters
begin transaction on db into tx
execute tx with "insert ..."
commit tx                             # or: rollback tx
get tables from db into tables        # name / schema ("main") / kind of t
get columns from "users" in db into cols   # name, type, database type, nullable, primary key, ...
disconnect db
```

**Semantics and source:**

- **Provider.** `sqlite` is the only provider. Any other name gives "I do not know a database provider called …" [R] (`ON/src/Otter.Runtime/Database.cs:54-61`). PostgreSQL and SQL Server are only design intent (`PS/docs/D99-QUERY-LANGUAGE-DESIGN.md`, `ON/otter-web-plan/ARCHITECTURE.md:14`).
- **Connecting.** A relative path resolves against the current directory. The folder must already exist. Anything after `;` is dropped. `readonly` opens with SQLITE_OPEN_READONLY [R] (`Database.cs:106-139`).
- **Parameters.** Parameters are bound by name. The name `x` matches `x`, `@x`, `:x` or `$x`. A name the statement does not use is silently ignored [R] (`Database.cs:201-233`).
  - `gone` binds NULL, `bool` binds 0/1, and a whole double binds as an int64.
  - **Everything else binds as its text**, so `bytes` binds as the literal `"OtterBytes"` and a date binds as local `yyyy-MM-dd HH:mm:ss`. Both were verified with `typeof(b)` giving `text` and the value `OtterBytes` (`misc.ot`) [V].
- **Reading rows.** INTEGER values become a `double` (precision is lost above 2^53). A BLOB is read with `sqlite3_column_text`, so it is truncated at the first NUL byte [R] (`Database.cs:256-263`).
- **`execute`** prepares the SQL with `sqlite3_prepare_v2`, ignores the tail, and steps **once** [R] (`Database.cs:288-314`, `Sqlite.cs:57`).
  - `execute db with "insert …; insert …"` ran only the first insert and reported rows affected 1 [V] (`db1.ot`: the count went from 2 to 3, not 4). This is a silent partial migration risk.
  - `INSERT … RETURNING` through `execute` gives no rows. Use `query` instead.
- **`last inserted id`** is `sqlite3_last_insert_rowid` for the connection. It stays non-zero after later UPDATEs (SQLite semantics) [R] (`Database.cs:304-307`).
- **Transactions** use `BEGIN IMMEDIATE TRANSACTION`. Only one transaction can be active per connection. There are no savepoints or nested transactions. `disconnect` rolls back an open transaction [R] (`Database.cs:153, 321-375`). Rollback and commit were verified [V].
- **Errors.** Database problems become line-anchored Otter runtime errors, and `try`/`otherwise` catches them [V]. A missing library gives a clean error message [R] (`ON/src/Otter.Runtime/Interpreter.Data.cs:106-115`). After `disconnect`, the error is "This database connection is closed." [V].
- **Masking.** Connection strings are masked for `password`, `pwd` and `secret` [R] (`Database.cs:47-49`).
- **Concurrency.** No `busy_timeout` and no journal mode are set (searching for "busy" finds nothing in `Database.cs` or `Sqlite.cs`). Both can be set by the program [V] (`pragma.ot`):
  - `execute db with "PRAGMA busy_timeout=5000"`
  - `query db with "PRAGMA journal_mode=WAL" into j` gives `wal`.
- **Migrations: absent.** There is no migration statement or tool. `PRAGMA user_version` works through `query` [V], so a migration runner could be written in Otter. Because `execute` runs only one statement, each DDL statement has to be a separate `execute`.
- **Statement caching:** none. Every call does prepare, step and finalize.
- **Connection pooling:** none. A connection is a single handle and is not thread-safe beyond SQLite's own guarantees. The interpreter is single-threaded anyway.

**How SQLite is loaded** [R]. See `ON/src/Otter.Runtime/Sqlite.cs:7-50`.

- `[LibraryImport("otter-sqlite3")]` is source-generated P/Invoke, which is AOT-safe, together with `NativeLibrary.SetDllImportResolver`.
- The resolver tries these libraries in order:
  - **Windows:** `winsqlite3.dll` (built into Windows 10 and later), then `sqlite3.dll`.
  - **macOS:** `/usr/lib/libsqlite3.dylib` (from the system dyld cache), then `libsqlite3.dylib`.
  - **Linux:** `libsqlite3.so.0`, then `libsqlite3.so`.
- No native library is shipped. otterPS itself is Windows-only (`winsqlite3.dll` via `DllImport`; `PS/src/Otter.Database.psm1:13,216`).
- Linux needs the distro package. The certify job installs `libsqlite3-0` explicitly (`ON/.github/workflows/certify.yml`, the Debian step). A minimal or distroless container image may not have it, and then programs get the "Install SQLite" error.
- The SQLite version is whatever the operating system provides: 3.51.0 on this Mac [V]. On Windows it varies by build. This affects features such as `RETURNING` (3.35 and later), JSON functions and `STRICT` tables.

**Security notes:**
- Raw SQL is injection-safe only when values go through `parameter`. Nothing stops `"… where name = '" plus name plus "'"`, because text concatenation with `plus` works [V].
- The single-line `query db with "…" into x` form cannot take parameters, which nudges people toward concatenation.

**Tests:**
- `ON/tests/compat/data_db*.ot`, `data_syn_db.ot` and `data_the.ot`, run through `tools/compare.ps1`.
- `PS/tests/Database.Tests.ps1` and `PS/tests/Data.Tests.ps1`.

### 1.2 D99 query language (OQL): status experimental, not 1.0

The design target is Otter 1.1 or later (`PS/SPEC-DECISIONS.md:7368-7409`).

**Syntax, verified** (`oql.ot`) [V]:

```otter
get name and age from users in db
    where age is at least minAge or name starts with "k"
    order by name descending
    take 5
into rows
count all from users in db where name is in wanted into n    # wanted is a list variable
get all from users in db where age between 1 and 100 into r2
get all from users in db where name contains pattern into r3
# aggregates: count / sum / total / average|avg / minimum|min / maximum|max ... from T in DB [where] into x
# also: [as alias], skip|offset N, distinct, is not in
```

**Translation** (`ON/src/Otter.Runtime/Queries.cs`):
- Values become `@p0…` parameters (`Queries.cs:119-193`).

**Security findings [V]:**
- S-1 (high severity): a security vulnerability in the experimental query language (D99), also present in the released otterPS 1.0.1. Technical details are withheld until a coordinated fix is available (`docs/SECURITY_LEDGER.md`).
- `contains`, `starts with` and `ends with` wrap the value in `%…%` without escaping `%` or `_` (`Queries.cs:152-164`). `contains "%"` matched every row [V]. This is wildcard injection, not SQL injection.
- The OQL `where` grammar has no parentheses and no `not` (`ON/src/Otter.Language/Parser.Data.cs:504-520`).
- Expressions in general have no parentheses either. `say "x" (length of y)` is a syntax error [V].

**Tests:** `ON/tests/compat/data_query*.ot`, `data_syn_query.ot`, and `PS/tests/Query.Tests.ps1`.

---

## 2. Other runtime capabilities

### 2.1 JSON: supported, part of the 1.0 portable core

**Syntax, verified** (`data.ot`, `jsondate.ot`) [V]:

```otter
convert user to json into text
convert jsonText from json into parsed     # object -> thing, array -> list, null -> gone, numbers -> double
read json from "out.json" into back
# HTTP forms: get json from URL into x / get URL as json into x (D116A)
```

**Implementation.** `ON/src/Otter.Runtime/Json.cs` is a hand-written port of .NET Framework's JavaScriptSerializer and PowerShell 5.1's ConvertTo-Json and ConvertFrom-Json, quirks included. It does **not** use System.Text.Json; the only use of System.Text.Json is in `Otter.Cli/DesktopCommand.cs:678` [R].

**Output** [V]:
- Indentation is four spaces plus PowerShell-style alignment, so the output is not compact.
- `< > & '` are escaped as `<` and similar (`Json.cs:125-149`). This makes the output safe to embed inside a `<script>` block.
- **Dates** come out as `{"Value":"\/Date(1792733268405)\/","HasTime":true}` (`Json.cs:91-93`). They are not ISO 8601.
- Numbers are doubles. `9007199254740993` is written as `9007199254740992`.
- There is no compact mode and no way to control key casing.
- Two properties whose names differ only in case collapse into one.

**Input** [V]:
- The reader is lenient. `{'k': 1, b: 2}` is accepted (single quotes and unquoted keys).
- The depth limit is 1020 (`Json.cs:327`). There is no size limit.
- Invalid JSON gives an Otter error (D29).

**Web relevance:** A web API needs compact, ISO-date output and strict parsing. That would need a new JSON mode, which could be built on System.Text.Json's `Utf8JsonWriter` and `Utf8JsonReader`. Those are AOT-safe without source generation because the work is manual.

**Tests:** `ON/tests/compat/json*.ot`, `t3_json_date_overflow.ot`, and `PS/tests/Data.Tests.ps1`.

### 2.2 CSV: supported (D95)

**Syntax** [V]:
- `convert people to csv into csvText`
- `write csv people to "people.csv"`
- `read csv from "people.csv" into loaded`
- `convert csvText from csv into rows`

**Semantics.** RFC 4180 quoting is used. Output uses CRLF [V]. Every value read is text. A header row is required, and ragged rows are refused (`PS/SPEC-DECISIONS.md:6909-6976`; `ON/src/Otter.Runtime/Csv.cs:15, 82`).

**Security:** there is **no CSV formula-injection guard**. Values starting with `=`, `+`, `-` or `@` are written as-is (searching `Csv.cs` finds no such check).

### 2.3 Text and encoding: partial

**Available** [V]:
- `length of`, `uppercase of`, `lowercase of`
- `contains`, `starts with`, `ends with`
- `replace X with Y in s [into t]` (plain text, never a regex)
- `split s by "," into words`, `join words with "|" into t`
- `bytes from text|hex|base64 X` and `text|hex|base64 from bytes X`
- `hex from bytes` is **uppercase**.

**Concatenation:**
- `"Hello, " plus name` is allowed in an assignment.
- `a and b make c` works only in a `make` statement.
- `say a b` joins its parts with spaces. Inside `say`, `and` means **addition** and fails on text [V] (D11).

**Absent** [R]: HTML escaping, URL percent-encoding and decoding, query-string parsing, regular expressions, and number formatting. A search for `UrlEncode|EscapeDataString|HtmlEncode|WebUtility` in `src/Otter.Runtime` and `src/Otter.Language` returned only PSRP and JSON internals.

**XML (D105)** exists (`ON/src/Otter.Language/Syntax.cs:438-525`), but it is not a templating tool.

### 2.4 Dates: supported, local time only

**Syntax** [V]:
- `today`, `now`
- `year|month|day|hour|minute|second of d`
- `add 30 minutes to t`, `remove 1 month from d`
- `format d as "yyyy-MM-dd" into iso`
- `date from "2026-01-31" using "yyyy-MM-dd"`. Adding one month to that date gives `2026-02-28` (month-end clamping).
- `days between a and b`

**Semantics.** Everything is **local time only**. There is no time zone or UTC syntax (`PS/SPEC-DECISIONS.md:1030-1066`, D32.1). A search for `TimeZone|ToUniversal` in the runtime finds only internal uses (`Json.cs:91`).

**Web impact:** HTTP dates, cookie expiry, JWT `exp` and stored timestamps all need UTC and ISO-8601 or RFC 1123 formatting. Today a program would have to rely on `format … as` patterns and the host's local time zone. The certify CI sets TZ=UTC.

### 2.5 Files: supported, no sandbox

**Syntax.** These are documented and in the release matrix [R]:
- `read "f" into x`
- `write text to "f" [atomically]`
- `append … to …`
- `file "x" exists`
- `get files in …`
- `bytes from file`
- `zip` / `unzip`

`write text to "out.json"` and `read json from` were verified [V].

**Paths** (`ON/src/Otter.Runtime/Files.cs:25-53`):
- A relative path resolves against the current directory. A rooted path is used exactly as written.
- `..` is allowed.
- Control characters and `" < > |` are refused. `* ? :` are refused in relative paths.
- **Emulates .NET Framework MAX_PATH** (260 for the full path, 248 for the directory) on every operating system.

**Traversal and sandbox:**
- There is **no containment**. This was deliberate: the program is "a trusted local-programming runtime, not a sandbox" (`PS/docs/OTTER_1_0_SECURITY_REVIEW.md`).
- A web handler that builds paths from request data would need its own containment, for example a new "serve folder" primitive that canonicalizes the path and checks it against a root, including symlinks.
- Project manifests already have containment code for `entryPoint` and `assets`, including symlinks (`PS/docs/OTTER_1_0_PROJECT_MANIFEST.md`, "Staying inside the project"). That code could be reused.

**Other file safety:**
- Atomic writes use `File.Replace` with a same-directory temporary file (`Files.cs:180, 228`).
- Unzip uses `ZipFile.ExtractToDirectory` (`Files.cs:476-484`), which refuses zip-slip entries [G].

### 2.6 Environment, arguments and secrets: partial

**Syntax** [V]:
- `get environment variable "HOME" into home`. An unset variable gives `gone`.
- `set environment variable "OTTER_AUDIT" to "1"`
- `arguments` (a list). `otter run data.ot a1 a2` gives a length of 2.

This is a usable path for 12-factor style configuration such as `DATABASE_PATH` or a session-key environment variable. Bytes would come from `bytes from base64`.

**Secret vault (D111)** [V] (`secrets.ot`):
- Syntax: `store secret "n" with value v`, `secret "n"`, `secret "n" exists`, `delete secret "n"`.
- It uses Windows Credential Manager only (`ON/src/Otter.Runtime/Credentials.cs:137-245`).
- On macOS it fails with "…this platform does not have one Otter supports yet." [V].

**Credentials (D81)** [V]:
- `set|get|delete credential` stores DPAPI-encrypted files under `%LOCALAPPDATA%` (`Credentials.cs:15-47`). It is Windows-only.
- On macOS: "only available on Windows" [V].

**Implication:** a cross-platform web backend has no portable secret store today. Environment variables are the only portable option.

### 2.7 Cryptography: supported (D91, D92, D109)

**Syntax, verified** (`crypto.ot`) [V]:

```otter
hash "hello" as "sha256" into digest                  # lowercase hex; md5|sha1|sha256|sha384|sha512
hash "hello" as "sha256" with key "k" into mac        # HMAC
d2 is sha256 of data                                   # bytes (sha256|sha384|sha512)
m2 is hmac sha256 of data using keyBytes
r is secure random bytes 32                            # 1..1048576, RandomNumberGenerator
hash password "correct horse" and call it stored       # otter-pbkdf2-sha256$600000$<salt b64>$<hash b64>
if password "correct horse" matches hash stored ...    # constant-time
generate encryption key and call it key                # 32 random bytes
encrypt data using key and call it sealed              # v1: 0x01|IV|AES-256-CBC|HMAC-SHA256 (encrypt-then-MAC)
decrypt sealed using key and call it opened
if a securely equals b ...                             # FixedTimeEquals on bytes
encrypt "t" with key "pass" into cipher                # D92: PBKDF2(100k)-derived keys, base64
```

**Source:** `ON/src/Otter.Runtime/Crypto.cs`.
- `HashPassword` is at lines 107-113. It uses `Rfc2898DeriveBytes.Pbkdf2` with SHA256, 600,000 iterations, a 16-byte salt and a 32-byte output.
- `PasswordMatches` is at lines 116-131. It uses `FixedTimeEquals`.
- `SealBytes` and `OpenBytes` are at lines 76-105.
- The interpreter cases are at `Interpreter.cs:596-627` and `1324-1366`.

**Notes:**
- PBKDF2-SHA256 at 600,000 iterations meets OWASP's PBKDF2 guidance [G]. Argon2id is preferred but would need a third-party dependency (see section 4).
- `PasswordMatches` accepts **any iteration count of 1000 or more, with no upper bound**, taken from the stored hash string. Only stored hashes the program itself trusts should be verified. Capping the count on the web path is worth considering, to avoid denial of service from attacker-supplied hashes.
- `random number` and `random item` also use the cryptographic RNG until `set random seed` is called (`ON/src/Otter.Runtime/PowerShellRandom.cs:5-56`). Session tokens should still use `secure random bytes`, which can then be turned into text with `hex from bytes` or `base64 from bytes`.
- There is no JWT, no AES-GCM (because Windows PowerShell 5.1 parity is required) and no signing or verification with public keys.

**Tests:** `ON/tests/compat/crypto*.ot`, `syn_crypto*.ot`, `ON/tests/web/crypto_*.ot`, and `PS/tests/Crypto.Tests.ps1` and `Vault.Tests.ps1`.

### 2.8 Jobs, async and the event loop: supported, single-threaded and cooperative

**Syntax, verified** (`jobs.ot`) [V]:

```otter
start command "echo hello-from-job" and call it job
on output from job
    say "got:" received output
.
on exit of job
    say "exit code:" exit code of job
.
say "main program done"      # printed first; handlers run after the last statement
```

**Model** (D121, `PS/SPEC-DECISIONS.md:7677-7714`; `ON/src/Otter.Runtime/EventLoop.cs:8-18, 82-103`):
- Handlers run one at a time, on the interpreter's thread, after the main program ends or during `wait`.
- The loop polls `Task.IsCompleted` and sleeps 10 ms when nothing is ready (`EventLoop.cs:92`).
- Concurrency and multithreading are DEFERRED (`PS/docs/OTTER_1_0_RELEASE_SCOPE_MATRIX.md:24`).
- Event sources: file watchers, WebSocket client, TCP client and server, UDP, HTTP request handles, and command jobs.
- There is a TCP server (`EventLoop.cs:465-503`), but **no WebSocket server**.

**Web implication:**
- A request handler would run the same way: one request at a time.
- Any slow handler blocks every other request, including one making a synchronous HTTP `get` or a long SQLite query.
- Adding throughput means either one interpreter per request, which needs isolated scopes, or an explicit decision on async semantics. That is a language decision.

### 2.9 Modules and project manifests: supported for files only

**Modules.** `use "helpers.ot"` works by textual inclusion before lexing. A file is included once. Cycles are an error. Paths are case-sensitive (D122). Error line numbers are remapped to the real file (`ON/src/Otter.Cli/Modules.cs:1-40`). There are no packages and no registry (DEFERRED).

**Manifest** (`otter.json`):
- Fields: `entryPoint` (required), `name`, `version`, `target` (console, desktop, web, game, automation, **server**), `assets`, `build.outputDir`, `build.clean`, `publish.outputDir`, `scripts` (never run).
- Containment is enforced, including through symlinks (`PS/docs/OTTER_1_0_PROJECT_MANIFEST.md`).
- `otter build` does **not** support `server`.

**Verified** (`proj/`):
- `otter check .` gives 0.
- `otter run .` gives `42`, with a function from `use "helpers.ot"`.

### 2.10 Testing with `otter test`: supported, minimal

**Verified** (`proj/tests/`):
- `otter test .` runs every `tests/**/*_test.ot` and `test_*.ot` in a child `otter run` (`ON/src/Otter.Cli/ProjectCommands.cs:238-300`).
- Exit code 0 means PASS. Exit code 2 is reported as "FAIL (syntax error)". Anything else is FAIL, with the `fail with` message.
- The suite exit code was 3 when one test failed [V].
- There is no assertion library, fixtures, HTTP test client or test isolation beyond separate processes. Assertions are written as `if … fail with "…"`.

---

## 3. Templates and string formatting: absent

- **No string interpolation** in the language (D8: "There is **no interpolation syntax** — not `{name}`, `${name}`, or `$name`"; `PS/SPEC-DECISIONS.md:236-249`).
- The only formatting statement is `format DATE as "pattern" into t` (`FormatDateStmt`, `ON/src/Otter.Language/Syntax.cs:332`). There is no number formatting and no padding.
- There is no template engine and no HTML escaping function.
- Building HTML by concatenation is unescaped. This was verified: `"<p>" plus "<b>Ann</b>"` printed raw markup [V].
- The **web compiler** (`otter web`, client side) escapes:
  - `</script` becomes `<\/script` (RT-007).
  - Attributes go through an HTML attribute escape helper.
  - Dynamic text uses `textContent` (`PS/docs/OTTER_1_0_SECURITY_REVIEW.md`; `ON/src/Otter.Web/Templates.g.cs:640-654`).
  - None of this is available to server-side Otter code.
- **Recommendation:** any server-side HTML feature should escape by default (context-aware: text, attribute, URL) with an explicit raw opt-out. It must not concatenate. That needs a syntax decision.

---

## 4. .NET architecture, dependencies and Native AOT

### 4.1 Projects [R]

The shared settings come from `ON/Directory.Build.props`:
- `TargetFramework net10.0`
- `TreatWarningsAsErrors true`
- `InvariantGlobalization true`
- `IsAotCompatible true`
- `Nullable enable`
- `LangVersion latest`
- Version read from `VERSION` (0.9.0-preview.1)

| Project | TargetFramework | PackageReferences | AOT / trimming settings | Notes |
|---|---|---|---|---|
| `src/Otter.Language` | net10.0 (props) | none | IsAotCompatible (props); AllowUnsafeBlocks (LibraryImport for user formats) | lexer, parser, AST |
| `src/Otter.Runtime` | net10.0 | none | IsAotCompatible; AllowUnsafeBlocks (LibraryImport: registry, credentials, clipboard, SQLite) | interpreter and providers; references Language |
| `src/Otter.Web` | net10.0 | none | IsAotCompatible | `otter web` JS compiler; references Language |
| `src/Otter.Cli` (`otter`) | net10.0 | none | **PublishAot true**, OptimizationPreference Speed, UseAppHost false (app host only in CI, for antivirus reasons) | references Runtime and Web; `ServeCommand.cs` uses `System.Net.HttpListener` |
| `src/Otter.Desktop` (`otter-desktop`) | net10.0-windows | none | PublishAot false, PublishTrimmed false, IsAotCompatible false, InvariantGlobalization false, UseWPF | WPF host, JIT; compiles the Cli sources in |

- A search for `PackageReference` under src, tests and tools found **nothing** [R]. The only implicit package is the SDK's ILCompiler.
- There are no `RequiresUnreferencedCode`, `DynamicDependency` or `Reflection.Emit` uses in src [R].
- License: the Otter Free Use License 1.1, which is first-party (`ON/LICENSE`).
- otterPS policy: "contains **no redistributed third-party binaries, libraries, packages**" (`PS/THIRD-PARTY-NOTICES.md` §1). Any new dependency, especially a native one, would break this stance and needs an owner decision (`otter-web-plan/DECISIONS.md`, which already reserves ADR WEB-004 for SQLite and migrations).
- No trimming warning was suppressed in the projects. Since TreatWarningsAsErrors is on, ILC and trim warnings fail the build.

### 4.2 Candidate building blocks: dependency, license and AOT table

| Building block | Source and license | Native AOT | Size and evidence | Notes |
|---|---|---|---|---|
| `System.Net.HttpListener` | BCL (MIT) | **Works** [V]: already used by `otter serve` in the AOT `otter` (`ON/src/Otter.Cli/ServeCommand.cs:126-140`); scratch AOT build ran | `listener` sample, with STJ, WebSocket and PBKDF2: **4.14 MB** vs hello 1.10 MB [V] | [G] Managed implementation on Linux and macOS; http.sys on Windows, where a non-localhost prefix needs a URL ACL or admin. HTTP/1.1 only. HTTPS on Unix is awkward (certificate files), and on Windows bound through netsh. Microsoft calls it legacy and not recommended for new work. No HTTP/2, limited header and timeout control, no built-in slowloris protection. Current `serve` binds `http://localhost:{port}/` (`ServeCommand.cs:70, 128`) with a 10 MB body cap (`:18`). |
| Kestrel / ASP.NET Core minimal APIs (`Microsoft.AspNetCore.App` shared framework) | BCL (MIT), framework reference, not NuGet | **Works** [V]: `Microsoft.NET.Sdk.Web` + `WebApplication.CreateSlimBuilder` + `MapGet` + STJ source generation published with PublishAot and TreatWarningsAsErrors, no warnings, and ran | **9.00 MB** [V] (about +7.9 MB over hello; the delta inside `otter` would be somewhat smaller because of shared BCL code [G]) | [G] Supported in AOT: minimal APIs (with the Request Delegate Generator), Kestrel, HTTP/1.1, HTTP/2, TLS, `UseWebSockets`, rate limiting, auth (JWT bearer, cookies), CORS, output caching, health checks. Not supported: MVC, Razor, Blazor Server; SignalR is limited. In a console app use `<FrameworkReference Include="Microsoft.AspNetCore.App"/>`, or `Sdk.Web`. Uses ServerGC by default (the link line showed `libRuntime.ServerGC.a`); can be set to workstation GC for a smaller footprint. HTTP/3 needs msquic. Best fit for production hardening (limits, timeouts, TLS). |
| `System.Net.WebSockets` (`WebSocket.CreateFromStream`, `HttpListenerContext.AcceptWebSocketAsync`, Kestrel `UseWebSockets`) | BCL | **Works** [V]: server-side `CreateFromStream(IsServer=true)` in the AOT sample | in the 4.14 MB above | otter-net already uses `ClientWebSocket` as a client (`EventLoop.cs:3`); no server today. |
| SQLite through operating-system libraries (current) | OS library: public-domain SQLite | **Works** [V]: `LibraryImport` + resolver in the AOT `otter` | 0 bytes added | Version depends on the operating system. Linux needs `libsqlite3-0`. No control over compile options (FTS5, JSON1 usually present). |
| `Microsoft.Data.Sqlite` | NuGet, MIT; depends on SQLitePCLRaw (Apache-2.0) bundles shipping native `e_sqlite3` per RID | [G] Generally works under AOT with recent versions. **Not verified here** (offline; the package is not in the local cache) | + managed assemblies + native e_sqlite3 (about 1–2 MB per RID [G]) | Gives ADO.NET, pooling, BLOB streams, `SqliteParameter` types. Ships a **third-party native binary**, which conflicts with the no-redistribution policy and D97's "no external package dependencies". Would also change error text and behaviour parity with otterPS. Recommendation: keep the `LibraryImport` approach and fix the defects in §1 (bind BLOBs with `sqlite3_bind_blob`, read with `sqlite3_column_blob`, return int64 as an exact value, set a busy timeout, loop over the statement tail or refuse multiple statements). |
| `System.Text.Json` source generation | BCL | **Works** [V]: `[JsonSerializable]` context serialized under AOT with no warnings | in the samples above | Reflection-based `JsonSerializer` is **not** AOT-safe (IL2026/IL3050 warnings would fail this build). For dynamic Otter values, `Utf8JsonWriter`/`Utf8JsonReader` or `JsonNode` are AOT-safe without source generation [G]. Existing Otter JSON (`Json.cs`) is hand-written and must keep PowerShell parity for `convert … to json`. |
| PBKDF2: `Rfc2898DeriveBytes.Pbkdf2` | BCL | **Works** [V]: already in `otter` (`Crypto.cs:111`) and in the scratch sample | 0 | Already exposed as `hash password`. FIPS-friendly. |
| Argon2id | Not in the BCL, including .NET 10 [G]. Options: `Konscious.Security.Cryptography.Argon2` (MIT, managed), `Isopoh.Cryptography.Argon2` (CC0/MIT-style, managed), libsodium via `Sodium.Core` (ISC + native binary) | Managed ports are expected to be AOT-friendly (no reflection) [G]. **Unverified** | small (managed) / native for libsodium | Third-party crypto: needs license and maintenance review and an owner decision. Since `hash password` already uses 600k PBKDF2 and stores a self-describing prefix (`otter-pbkdf2-sha256$…`), an `otter-argon2id$…` format could be added later without breaking existing hashes. |
| AES-GCM (`AesGcm`) | BCL | [G] AOT-safe; not used, because of Windows PowerShell 5.1 parity (D109) | 0 | A web-only token format could use it, but would diverge from otterPS. |

**AOT experiment provenance [V]:**
- Projects: `a scratch folder (not kept)`.
- Build: SDK `a scratch SDK folder` 10.0.402, packages from `a scratch NuGet folder` (10.0.13), `-p:RestoreSources=<local cache>` so there was no network access, `-r osx-arm64`.
- All three ran. `kestrel` was run without arguments, so it built the app but did not listen.
- The Homebrew SDK 10.0.401 also produced binaries, but only after adding `-L` linker paths, and it links Homebrew OpenSSL and brotli dynamically. **Use Microsoft's SDK for size measurements** (CI does).

---

## 5. Test harnesses relevant to web work [R]

- **`tools/compare.ps1`.** Runs each `.ot` file through otterPS and otter-net and compares stdout and stderr byte for byte, plus the exit code.
  - `-IncludeEventPrograms` covers programs that listen or use URLs.
  - `-NativeOnly` forbids handing a program to PowerShell.
  - `-CandidateOnly` runs otter-net alone and captures the output for a later `compare-captures.ps1`.
  - The default corpus is otterPS's conformance fixtures and examples plus `ON/tests/compat` (208 files, including the `data_db*`, `data_query*`, `json*`, `csv`, `crypto*` and `net_*` cases).
- **`tools/compare-web.ps1`** (and `compare-web-batch.ps1`). Runs `otter web <file> -NoOpen` with both implementations and compares every output file byte for byte. It knows about three PowerShell 7 vs 5.1 differences. Its corpus is `tests/web` plus otterPS's examples and conformance. This tool covers the client-side compiler only.
- **`tools/compare-cli.ps1`.** CLI cases in `tests/cli/*.psd1`: run, test, build, projects and modules.
- **`tests/network/*.ot` with `tools/network-test-server.js`.** A Node peer on `127.0.0.1:18765`:
  - HTTP `GET /hello.txt` and `/data.json`, 404 for other paths, 501 for other methods.
  - A WebSocket echo service with a `bye` close code of 4001.
  - Used because "a synchronous `get` blocks the program that would answer it, and Otter has no WebSocket server of its own". A web-server feature would need the inverse: an Otter server driven by an external client, which could be Node or curl in this script.
- **`tools/Invoke-OtterNetCertification.ps1`.** Runs these gates from a clean detached worktree: build (0 warnings, Native AOT publish), conformance, programs, network (needs node), cli, web, fuzz, projects, site, no-powershell, native-coverage, desktop (Windows), machine-state (Windows), and repository-clean.
- **`.github/workflows/certify.yml`.** Six platforms:
  - win-x64 and win-arm64, with TZ set to UTC.
  - osx-arm64 and osx-x64 (macos-15-intel).
  - linux-x64 and linux-arm64, in a Debian 12 container with `libsqlite3-0` and nodejs installed and TZ=UTC.
  - Only win-x64 compares directly with otterPS on Windows PowerShell 5.1. The others capture their output and `-Combine` diffs it against Windows, using the declared differences in `tests/cross-platform.psd1`.
  - Workflows run on manual dispatch and on pushes to `cert/certify` and the release branch. Per the project's memory notes, CI is not the usual merge gate; local tests are.

**How a new web-backend feature would be tested across the six platforms (proposal):**
1. **Otter-level fixtures** go in `tests/compat/web_*.ot`. Each is a server program that serves N requests and then stops itself, so it fits `compare.ps1 -IncludeEventPrograms`.
2. **A client driver.** Extend `network-test-server.js`, or add a sibling `tools/web-test-client.js`, that sends a fixed request script (methods, headers, bodies, bad input, oversized body, path traversal, slow client) and prints a normalized transcript. Comparison is by transcript.
3. **The reference problem.** otterPS 1.0.1 is frozen and `otter serve` only answers literal routes. New server semantics will have **no otterPS reference**. Those tests must be golden-file (expected output checked in), not differential. They also need a new declared-difference category in the certification record.
4. **Fit with existing gates.** Native AOT publish with TreatWarningsAsErrors on all six RIDs is already a gate. Add a binary-size budget check, because Kestrel adds roughly 8 MB.
5. **SQLite.** Linux runners must keep `libsqlite3-0`. Add a "SQLite missing" case and a minimum-version check (for example `RETURNING` needs 3.35 or later). Windows `winsqlite3` versions differ between images.
6. **Security tests** go in a dedicated `tests/web-security/` folder: injection (SQL through `parameter`, OQL identifiers), XSS escaping, traversal, body and header limits, and timing-safe comparisons.

---

## 6. Security posture relevant to web [R]

**From `PS/docs/OTTER_1_0_SECURITY_REVIEW.md`:**
- Scope: the console runtime, files, processes, generated web output, local listeners, credentials and modules.
- No critical or high findings.
- MEDIUM, resolved: notification `innerHTML` was replaced with `textContent`.
- LOW, by design: file deletion, processes and the registry are exposed to trusted scripts. **"Otter 1.0 RC may be described as a trusted local-programming runtime, not a sandbox for hostile scripts."**
- HARDENING, open:
  - File imports reached via `otter serve` must be re-reviewed.
  - "Desktop/server and terminal bridges depend on local listeners. Their exposure and origin/token controls need a supported-host review." Status: **Not certified**.
- Controls noted:
  - Process launches use `UseShellExecute=false` with explicit argument quoting.
  - `GetFullPath` validates path shape but does **not** sandbox.
  - Atomic writes use temporary files with GUID names.

**From `PS/docs/OTTER_1_0_RED_TEAM_REPORT.md`** (20,040 cases, 9 defects, all fixed; P0 = 0). The web-relevant items:
- **RT-007 (P2):** `</script>` breakout and raw interpolation of control text in generated HTML. Fixed with `<\/script` and `Escape-OtterHtmlAttr`. Regression case `V10_script_breakout_escaped`.
- **RT-009 (P2):** deep recursion. Now limited to 250 calls with a clean error. otter-net has the same limit at `ON/src/Otter.Runtime/Interpreter.cs:1678`. This is relevant to denial of service from recursive request handling.
- Resource soak results: stable across 500 JSON round-trips and 1,000 executions. That says nothing about long-running server memory.

**From `PS/SECURITY.md` (the Otter Studio threat model):**
- Loopback-only binding, plus Origin and Referer checks and a session token.
- Path containment with symlink-escape defense.
- A 10 MB body limit and a CSP of `default-src 'self'`.
- These are good precedents for a web SDK's defaults.

**Gaps a web backend must close (from this audit):**
1. **Untrusted input meets a trusted-script runtime.** Every file, process, registry and credential statement is reachable from a handler. Path containment for request-derived paths is required.
2. **SQL:**
   - Make `parameter` the easy path, which means allowing parameters in the single-line form.
   - Fix the bytes, BLOB, int64 and multi-statement defects.
   - Escape `]` in OQL identifiers and refuse computed identifiers.
   - Escape `LIKE` wildcards.
3. **Output encoding.** There is no server-side HTML escaping, URL encoding or templating. JSON escaping of `< > & '` is fine for embedding.
4. **Time.** There is no UTC, which affects tokens, cookies and logs.
5. **Secrets.** There is no store on macOS or Linux, and `say` of a secret prints it (D111 notes this).
6. **Concurrency.** The single-threaded event loop means one slow handler is a denial of service for everyone. Limits on bodies, headers, time and recursion are needed.
7. **`otter serve` today** binds `localhost` with HttpListener and a 10 MB body cap. It **never runs the program**: routes answer only with a literal `respond with` value (`ServeCommand.cs:9-14`).

---

## 7. Verified samples (`docs/web/samples/data/`)

| File | `check` | `run` | What it proves |
|---|---|---|---|
| `db1.ot` | 0 (after two syntax fixes: no `(…)` in expressions; `say` joins parts by juxtaposition, `and` is addition) | 0 | connect, `has` config, execute with and without `into`, `parameter` binding (an injection payload stored verbatim), query, rollback, commit, `get tables`, `get columns`, **multi-statement execute ran only the first statement**, a closed-connection error |
| `oql.ot` | 0 (after replacing a `[...]` list literal with an `are` block) | 0 | D99 projection, where, or, starts with, order by descending, take, `count all … is in`, `between`, `contains`; **`contains "%"` matched every row** |
| `pragma.ot` | 0 | 0 | `PRAGMA journal_mode=WAL` gives `wal`; `busy_timeout=5000`; `sqlite_version()` 3.51.0; `PRAGMA user_version` 0 |
| `misc.ot` | 0 | 0 | `x is "a" plus name` and `a and b make c` concatenate; HTML stays unescaped; lenient JSON `{'k':1, b:2}` accepted; **bytes parameter stored as text `OtterBytes`**; a date parameter stored as local `yyyy-MM-dd HH:mm:ss` |
| `crypto.ot` | 0 | 0 | hash and HMAC (hex), `sha256 of`, `hmac sha256 of … using`, `secure random bytes`, `hash password` / `matches hash`, generate key, encrypt and decrypt (bytes), `securely equals`, D92 text encrypt and decrypt, `random number from 1 to 6 into roll` |
| `data.ot` | 0 (after fixes: no nested `are` inside `has`, no parentheses, `split … by … into`) | 0 (args `a1 a2`) | JSON write (`<` escaping), parse (null becomes gone), `read json`; CSV convert, write and read (RFC 4180, CRLF); get and set environment variables, missing variable is gone; `arguments`; `format` date; `date from … using`, month clamp; `uppercase`, `replace`, `split`, `join` |
| `jsondate.ot` | 0 (a property named `at` is refused: "I expected a property name") | 0 | **dates serialize as `{"Value":"\/Date(ms)\/","HasTime":true}`**; 2^53+1 becomes …992 |
| `secrets.ot` | 0 | 0 | `store secret` and `set credential` fail cleanly on macOS (Windows-only) |
| `jobs.ot` | 0 | 0 | `start command … and call it`, `on output from`, `on exit of`; handlers run after the main program |
| `proj/` (otter.json, `use "helpers.ot"`, `tests/*_test.ot`) | `check .` = 0 | `run .` = 0 (prints 42); `test .` = 3 (1 passed, 1 failed as intended) | modules, manifest, test runner, `fail with` reporting |
| `aot/{hello,listener,kestrel}` | n/a | all exit 0 | Native AOT sizes on osx-arm64: 1,102,744 / 4,138,968 / 9,003,368 bytes; no ILC warnings under TreatWarningsAsErrors |

**Syntax pitfalls found while verifying:**
- There are no parentheses in expressions.
- There are no `[a, b]` list literals. Use an `x are … .` block.
- A list cannot be nested inside a `has` block. Assign a list variable instead.
- Inside `say`, `and` is addition, so `say "a" and text` fails at runtime. Write `say "a" text`.
- Some short words such as `at` cannot be property names.
- Functions are `to name param` / `return …` / `name arg make result`.
