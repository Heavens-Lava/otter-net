# Otter Web runtime: Phase 2 plan (proposal)

**Status: planning, for the owner's review (2026-10-27). Not implemented.**
Phase 2 brings forms, safely escaped HTML templates, SQLite improvements,
and a working **Otter Guestbook** (otter-web-plan `EXAMPLE_APPS.md` A).

**No grammar change.** Everything below uses Otter 1.0.1 syntax, the way
`web server` and `web response` do. The examples ([samples/phase2/phase2.ot](samples/phase2/phase2.ot)) pass
`otter-net check` today; what they *do* is the proposal.

**Prerequisites, done or in review:**
- Phase 1a/1b merged;
- S-2 (LIKE wildcards) fixed in its own PR;
- S-1 handled privately.

React, Vite, Three.js and React Three Fiber stay on the roadmap (Phases
4-5) and are not started.

## 1. Forms

```otter
when site receives POST at "/sign" into req
    entry is form of req
    get "name" from entry into name
    if name is gone
        respond with "name is required" as json and status 400
    .
.
```

- **`form of req`:** a thing with each field's **first** value
  (`application/x-www-form-urlencoded`: `+` is a space, `%XX` is UTF-8).
  **`forms of req`** gives every value in order, matching
  `query`/`queries`.
- **Missing fields** follow D41: `x of form of req` is an error, and
  `get "x" from form of req` gives `gone`.
- **Body errors:**
  - a body that is not form-encoded (wrong `Content-Type`) gives an empty
    `form`; the raw body stays in `body of req`;
  - invalid encoding or UTF-8 gives **400** before the handler runs;
  - the size limit is `maxbody` (default 1 MiB).
- **Not in Phase 2:** `multipart/form-data` (file uploads). Uploads need
  their own limits, storage outside the static folder (S-8) and scanning
  rules, so they are proposed for Phase 3.
- **CSRF:**
  - Phase 2 has no cookies or sessions, so there is no ambient credential
    to forge with.
  - CSRF protection arrives in Phase 3 **together with** cookies: per-session
    tokens and an `Origin` check. It is a gate for any cookie-authenticated
    form.
  - The guestbook is anonymous, so it does not depend on CSRF protection.

## 2. HTML templates, escaped by default (resolves S-4 for pages)

**Proposed (ADR WEB-003):** a runtime-rendered **`web template`** object,
in the same `X is a TYPE` form as `web response`.

```otter
page is a web template
    file is "views/guestbook.html"
    values is data
.
respond with page
```

The template language is a small, logic-less Mustache subset:

| In the template | Meaning |
|---|---|
| `{{name}}`, `{{entry.name}}` | the value, **HTML-escaped** (`& < > " '`) |
| `{{{name}}}` | the value unescaped. Explicit, greppable, and logged as a warning the first time each template uses it |
| `{{#each entries}}...{{/each}}` | repeat for each item of a list (inside it, `{{name}}` reads the item) |
| `{{#if flag}}...{{else}}...{{/if}}` | on a value that is not `gone`, `false`, `""` or an empty list |
| `{{> header}}` | include `views/header.html` (partials, layouts) |

**Rules:**

- **No code:** no expressions, calls or arithmetic in templates.
- **Templates are files in a `views` folder of the project**, with the same
  path rules as static files: no `..`, no dot names, no links out. They are
  never served as static files.
- **Context safety:**
  - a placeholder inside `<script>` or `<style>`, or in an unquoted
    attribute value, is refused **when the template is loaded**, naming the
    file and line;
  - `href`/`src` values get a URL check: only `http`, `https`, `mailto` and
    relative URLs; `javascript:` and `data:` become `#`.
- **Errors:** a missing value is an error naming the template line (D41
  style); a `{{#each}}` over something that is not a list is an error.
- **Caching:** templates are parsed once and re-read when the file changes,
  so editing during development works.
- **Response:** `text/html; charset=utf-8`. It combines with
  `web response`: `body is page` sends the rendered page with any status.

**Alternative considered:** a plain-Otter library (`render`) built on
`replace`. It works today, but it is slow, cannot know the HTML context,
and makes escaping depend on every caller. It is not recommended as the
main path.

## 3. SQLite improvements (fixes S-7; keeps D97)

| Problem (1.0.1) | Proposal |
|---|---|
| `execute` silently runs only the **first** of several statements | **Refused**, with an error naming the second statement's start: "execute runs one statement; use a transaction for several" |
| A bytes parameter is stored as the text "OtterBytes" | bound as a BLOB (`sqlite3_bind_blob`) |
| BLOB columns are read back as text | read as bytes |
| Integers above 2^53 lose precision | exact up to 2^53; beyond it, an error naming the column (or text, owner's choice) |
| No busy timeout | 5 s by default (`PRAGMA busy_timeout`), so concurrent tools do not fail at once |
| No migrations | a plain-Otter SDK library, `migrations.ot`: numbered `migrations/NNN-name.sql` files applied in order, each in a transaction, recorded in `schema_migrations`. No grammar change |

- **Compatibility:** each row above changes 1.0.1 behaviour for programs
  that hit the problem, so each is recorded in `SECURITY_LEDGER.md` or
  `SPEC-QUESTIONS.md` as an intended difference, with a test.
- **Recommendation:** in otter-net generally, not only with the web
  runtime, since the old behaviour loses data.
- **Owner decision:** whether some of these should apply only inside the
  web runtime.

## 4. The Otter Guestbook

`examples/web/guestbook/`:

- `otter.json` (opt-in);
- `main.ot`;
- `views/guestbook.html`, `views/layout.html`;
- `public/style.css`;
- `migrations/001-entries.sql`;
- a README.

**What it does:**

- `GET /` lists the latest 50 entries through the template, all escaped.
- `POST /sign` reads the form and validates it: name 1-60 characters,
  message 1-500. On success it inserts with parameters and answers **303**
  to `/`. Invalid input gets **400** with the form shown again (escaped).
- `GET /api/entries` returns JSON.
- Data lives in `guestbook.db`. `migrations.ot` creates it, and it survives
  restarts.

**Pass criteria (EXAMPLE_APPS A):**

- a fresh checkout runs it with `otter-net run examples/web/guestbook`;
- entries can be created and viewed, and invalid input is rejected;
- a restart keeps the data;
- **XSS:** `<script>` in a name or message is shown as text;
- **SQL injection:** `'); drop table entries; --` is stored as text;
- the 404/405/400 paths behave as specified.

**Tests:**

- black-box HTTP tests on six platforms;
- template security tests: escaping, refused contexts, `{{{ }}}`
  warnings, path rules;
- negative controls: removing escaping makes the suite fail.

A real-browser end-to-end test (Playwright) needs Node, so it is
proposed as an optional job (owner decision).

## 5. Order and gates

| PR | Content | Gate |
|---|---|---|
| 2a | `form`/`forms`, the SQLite fixes (S-7), busy timeout | contract rows **[T]**; six-platform HTTP tests; certification (S-7 changes are recorded differences) |
| 2b | `web template` (ADR WEB-003) | escaping and context tests with negative controls |
| 2c | Guestbook, `migrations.ot`, docs | EXAMPLE_APPS A pass criteria |

Every PR: the exact tested SHA, all six platforms, ci, certify, desktop,
package, and the security tests. **No PR is merged without the owner's
approval.**

## 6. Owner decisions needed before 2a

1. Templates: the `web template` runtime object (recommended) or a library.
2. `execute` with several statements: refuse (recommended) or run all.
3. Integers above 2^53: an error (recommended) or text.
4. SQLite fixes everywhere (recommended) or only in the web runtime.
5. File uploads (multipart) in Phase 3 with cookies and CSRF (recommended).
6. A Playwright browser test job (needs Node in CI only), or HTTP-level
   tests only.
