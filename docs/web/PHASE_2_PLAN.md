# Otter Web runtime: Phase 2 plan (proposal)

**Status: the owner approved the six decisions in §6 on 2026-10-27.
This document is the plan; the code comes in separate PRs (2a, 2b, 2c),
none merged without the owner's approval.** Reconciled with `main` at
`fcba67c` (PR #10 merged: **Phase 2a complete**, 2026-10-29). Phase 2b
(`web template`) is in its own PR; 2c (the Guestbook) starts after the
owner reviews 2b.
Phase 2 brings forms, safely escaped HTML templates, SQLite improvements,
and a working **Otter Guestbook** (otter-web-plan `EXAMPLE_APPS.md` A).

**No grammar change.** Everything below uses Otter 1.0.1 syntax, the way
`web server` and `web response` do. The examples ([samples/phase2/phase2.ot](samples/phase2/phase2.ot)) pass
`otter-net check` today; what they *do* is the proposal.

**Prerequisites, all done:**
- Phase 1a and 1b merged (`9d8fcaa`, `d3a7796`);
- S-2 (LIKE wildcards) fixed and merged (PR #8, `9caa3af`), so it is in
  place before any database-backed app reaches testers;
- S-1 handled privately (security release in preparation).

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
  - a body that is not form-encoded (another `Content-Type`) gives an empty
    `form`; the raw body stays in `body of req`;
  - a malformed `%` escape or invalid UTF-8 gives **400** before the
    handler runs, decoded with the same rules as the query string;
  - a `charset` other than UTF-8 gives **415**;
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

**Decided (decision 1, ADR WEB-003):** a native, runtime-rendered **`web
template`** object with secure, context-aware HTML escaping, in the same
`X is a TYPE` form as `web response`.

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
| `{{.}}` | the current item of `{{#each}}` (added in 2b, for lists of text) |
| `{{{name}}}` | the value unescaped. Explicit, greppable, and logged as a warning the first time each template uses it |
| `{{#each entries}}...{{/each}}` | repeat for each item of a list (inside it, `{{name}}` reads the item) |
| `{{#if flag}}...{{else}}...{{/if}}` | on a value that is not `gone`, `false`, `""` or an empty list |
| `{{> header}}` | include `views/header.html` (partials, layouts) |

**Rules:**

- **No code:** no expressions, calls or arithmetic in templates.
- **Templates are files in a `views` folder of the project**, with the same
  path rules as static files: no `..`, no dot names, no links out. They are
  never served as static files.
- **Context safety (context-aware escaping):**
  - in text and in quoted attribute values, values are HTML-escaped;
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
  (`respond with page and status 400` is not Otter: without `as json` the
  1.0.1 grammar reads `page and status` as one value, as for plain text
  in Phase 1a. No grammar change is proposed; the `web response` covers it.)
- **As built in 2b** (RUNTIME_CONTRACT §10): the context checks also refuse
  placeholders in HTML comments, tag and attribute names, `style` and
  `srcdoc` attributes, and in any attribute of elements that load code
  (`script`, `base`, `object`, `embed`, SVG animation elements); a block
  inside a tag needs a space on both sides. The URL check covers every
  URL attribute (`action`, `formaction`, `poster`, `srcset`, ...), not
  only `href`/`src`.

**Alternative not chosen:** a plain-Otter library (`render`) built on
`replace`. It is slow, cannot know the HTML context, and makes escaping
depend on every caller.

## 3. SQLite improvements (fixes S-7; keeps D97)

| Problem (1.0.1) | Decided change |
|---|---|
| `execute` (and `query`, found when reconciling with `main`: both use `sqlite3_prepare_v2` and drop the rest) silently run only the **first** of several statements | **Refused before anything runs** (decision 2), with an error that quotes the start of the second statement and suggests one `execute` per statement inside a transaction |
| A bytes parameter is stored as the text "OtterBytes" | bound as a BLOB (`sqlite3_bind_blob`) |
| BLOB columns are read back as text | read as bytes |
| Integers above 2^53 lose precision | **an integer that an Otter number cannot hold exactly is refused** (decision 3), with an error naming the column and suggesting `cast(x as text)`. Every integer up to 2^53 is exact; a larger one that a number holds exactly (2^60, for example) is still read. The same rule applies to `last inserted id` |
| No busy timeout | 5 s by default (`PRAGMA busy_timeout`), so concurrent tools do not fail at once |
| No migrations | a plain-Otter SDK library, `migrations.ot`: numbered `migrations/NNN-name.sql` files applied in order, each in a transaction, recorded in `schema_migrations`. No grammar change |

- **Scope (decision 4):** throughout otter-net, not only with the web
  runtime, since the old behaviour loses data. otterPS 1.0.1 is unchanged.
- **Compatibility:** each row above changes 1.0.1 behaviour for programs
  that hit the problem. Each is recorded under S-7 in `SECURITY_LEDGER.md`
  with a pinned test in `tests/security` (run on all six platforms).
- **What the corpora hit (checked on `main`):**
  - in `tests/compat`, only `data_db.ot`: one two-statement `execute` (it
    documents the 1.0.1 behaviour) and one BLOB read (`x'414243'`). These
    two checks move to `tests/security`, so the live comparison with
    otterPS keeps covering the rest;
  - no program in otterPS's corpus (conformance, examples, docs) uses
    several statements, BLOBs, bytes parameters or 64-bit integers;
  - the busy timeout changes only what happens when another connection
    holds a lock (otterPS sets none, so it fails at once).

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

**Browser tests (decision 6):** Playwright runs in CI as its own job.
Node is installed **only in that CI job**. It is never a runtime
dependency: an ordinary Otter project, `otter-net` and its packages need
no Node.

## 4b. Known limitations from Phase 2a (nonblocking, tracked)

Recorded in RUNTIME_CONTRACT (Phase 2a). Neither is a security issue;
both are candidates for a later phase:

- **Form charset precedence:** a body that is not UTF-8 *and* declares a
  non-UTF-8 charset gets 400 rather than 415: the body is decoded before
  the charset is looked at. Both refuse the request before the handler.
- **`last inserted id` precision:** the exactness check (S-7) runs after
  the insert, so the row is already stored when the error comes. Inside a
  transaction the program can roll back.

## 5. Order and gates

| PR | Content | Gate |
|---|---|---|
| 2a (**merged**, `fcba67c`) | `form`/`forms`, the SQLite fixes (S-7), busy timeout | contract rows **[T]**; six-platform HTTP and security tests; certification (S-7 changes are recorded differences) |
| 2b | `web template` (ADR WEB-003) | escaping and context tests with negative controls |
| 2c | Guestbook, `migrations.ot`, the Playwright job, docs | EXAMPLE_APPS A pass criteria, in a real browser |

Every PR: the exact tested SHA, all six platforms, ci, certify, desktop,
package, and the security tests. **No PR is merged without the owner's
approval.**

## 6. Owner decisions (approved 2026-10-27)

1. **Templates:** a native `web template` object with secure,
   context-aware HTML escaping.
2. **Several statements** in one `execute` (and `query`): refused, with an
   explicit message.
3. **Integers** that an Otter number cannot hold exactly: refused.
4. **SQLite fixes** throughout otter-net.
5. **File uploads** (multipart): deferred to Phase 3, with cookies and
   CSRF protection.
6. **Playwright** browser tests in CI; Node is never a runtime dependency
   of ordinary Otter projects.
