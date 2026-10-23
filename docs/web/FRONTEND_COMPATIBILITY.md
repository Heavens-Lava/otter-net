# Otter 1.0.1 / otter-net: frontend web integration audit

Date: 2026-10-22. Scope: how Otter relates today to HTML, CSS, JavaScript,
TypeScript, React, Three.js, React Three Fiber (R3F) and static assets.

Method: read otter-net `src/Otter.Web`, `src/Otter.Cli/{WebCommand,ServeCommand,DesktopCommand,ProjectBuild,ProjectCommands}.cs`
and otterPS 1.0.1 (`SPEC-DECISIONS.md`, `docs/`, `src/Otter.Web.psm1`, `src/Otter.Server.psm1`). Every
syntax form below was checked with the Native AOT otter-net binary
(`the otter-net 1.0.1 Native AOT binary (macOS arm64)`, reports `Otter 1.0.1`). Web pages were compiled with `otter web x.ot -NoOpen` and the
generated HTML read. Sample programs: `docs/web/samples/frontend/*.ot`.
**No generated page was run in a browser**, so browser behaviour stated
below comes from reading the generated JavaScript ("per generated code").

Labels: **FACT** = verified in the code and/or with the binary. **PROPOSAL** = an idea only. It is not
approved and adds no Otter syntax.

---

## 0. Summary

| Technology | Native today | Partial | Must stay external |
|---|---|---|---|
| HTML | `otter web` compiles a whole program to **one self-contained `.html` file** with fixed element kinds | No raw HTML. Text is always escaped | Hand-written HTML pages |
| CSS | Per-resource style properties, `customstyle` (raw inline CSS), sidecar `<entry>.css` inlined into `<style>` | No classes or selectors from Otter. The sidecar is whole-file only | Preprocessors, Tailwind and similar |
| JavaScript | Otter is **compiled to** JS (one inline `<script>`). HTTP becomes `fetch`, WebSocket becomes `WebSocket`, crypto uses Web Crypto | No way to call JS, import a script, or add `<script src>` | All hand-written JS and npm libraries |
| TypeScript | none | none | `tsc`/Vite entirely |
| React | none (no JSX, no components, no npm) | Otter has its own small reactivity (`state`/`derive`/`when x changes`) that is unrelated to React | React, the build toolchain |
| Three.js / R3F | none. The only "3D" is a hard-coded spinning wireframe cube on a 2D canvas | none | Three.js, R3F, WebGL/WebGPU |
| Static assets / MIME | `otter.json` `assets` are copied into `dist/` by `otter build`/`publish`. A page references them by relative path | No asset server. `otter serve` serves **no files** | A static file server with a MIME table (none exists in Otter) |

Confirmed by search: no reference to npm, Vite, TypeScript, React, JSX,
Three.js or `.tsx` anywhere in otter-net `src/`. The same holds for MIME
types and `.glb/.gltf/.wasm/.mjs/.ktx2/.hdr` in otter-net `src/` and in otterPS `src/*.psm1` and `otter.ps1`.

A proposal pack already exists in the otter-net repo:
`otter-net/otter-web-plan/` (README, ARCHITECTURE, ROADMAP, WEB_API_SPEC,
FRONTEND_3D_INTEGRATION, SECURITY_AND_TESTING, DECISIONS). It is marked
"Proposal for review; no implementation or syntax change authorized". Its
boundary agrees with this audit: React/Three.js/R3F stay browser-side and are built by npm/Vite,
while Otter hosts the result and provides the APIs.

---

## 1. The `otter web` compiler: what exists (FACT)

**Entry points.** `otter web <file.ot> [-NoOpen]` (also `otter browse`),
`src/Otter.Cli/WebCommand.cs`. It writes `<file>.html` next to the program
(UTF-8 with BOM) and opens the default browser unless `-NoOpen` is given.
Web, desktop and game projects run `otter build`, which writes `dist/index.html` plus the declared
assets and `otter.build.json` (`src/Otter.Cli/ProjectBuild.cs` ~l.183-253).
otterPS equivalent: `ConvertTo-OtterWeb` / `Export-OtterWebApplication` in
`src/Otter.Web.psm1` (D50). otter-net is a faithful port. The canvas runner,
`otterStorage` and event mapping are textually present in both
(`Otter.Web.psm1` l.1532, 2582, 2795).

**Generated page structure** (`src/Otter.Web/Templates.g.cs`, `Templates.Page`, ~l.939-2141):
1. `<!DOCTYPE html>`, `<meta charset>`, viewport, `<title>` (from the root
   page/window `title`, else first heading, else the file name).
2. One `<style>` with the theme (CSS custom properties `--otter-bg` and others, a
   system font stack), then the declarative CSS rules. The sidecar `<entry>.css` is inserted
   as `<style id="otter-sidecar-style">` before `</head>` (`WebCommand.cs` l.138).
3. `<body>`: the rendered resources, then `<div id="otter-live-output">`
   (where `say` output is shown).
4. **One inline `<script>`** (classic script, not a module): runtime helpers
   (`otterGetElement`, `otterSetText`, `otterSetStyle`, `otterSetProperty`, `otterSay`/`otterFormatValue`),
   the file/terminal/clipboard/dialog bridge client (desktop only), `otterStorage`, `otterFetch`,
   the SPA router (D103), the reactivity engine, the canvas runner, then the compiled
   top-level program in an `async` IIFE, then the event listeners.
5. No external resource is loaded (D127: "no mandatory external dependency …
   starts and renders offline"). Verified: generated pages contain no `<script src>` and no `<link>`. A minimal page is ~55 KB.

**Otter construct → HTML** (`WebCompiler.cs` `RenderResource`, l.~400-660):
`window`/`page` → container (`<main class="otter-page">` with a header for a page);
`button` (+ `primary`/`secondary`/`danger` variants) → `<button>`;
`text box` → `<input type=text>`; `text` → `<div class="otter-text">`;
`text area` → `<textarea>`; `image` → `<img src alt>`; `link` → `<a>`
(external `http(s)` gets `target=_blank rel="noopener noreferrer"`);
`checkbox`, `toggle`/`switch`, `radio`, `dropdown`/`select`,
`slider`/`range`, `progress`, `badge`/`tag`, `card`, `row`, `column`
(flexbox), `scroll`, **`canvas` → `<canvas width height data-mode data-animation>`**;
any other known kind (`table`, `dialog`, `modal`, `panel`, `list`) → a generic `<div class="otter-resource otter-KIND">`.
D56 declarative elements (`card "Title"` blocks with `layout`, `hover`/`enter`
animations) are rendered by `DeclarativeRenderer.cs` to HTML plus CSS `@keyframes`. That surface is
**outside the 1.0 supported surface** (D56). `otter web` still compiles it: `examples/experimental/counter.ot` checks and compiles.

Verified program (`samples/styling.ot`, valid, compiled):
```otter
app is a page
    title is "Styling"
    background is "linear-gradient(#000, #123)"
.
hero is a image
    source is "assets/hero.png"
    alt is "Hero"
.
docs is a link
    url is "https://example.com"
    text is "Docs"
.
panel is a text
    text is "<b>not bold</b>"
    customstyle is "transform: rotate(5deg); filter: blur(1px)"
.
put hero, docs, panel in app
show app
```
Output: `<img id="hero" class="otter-image" src="assets/hero.png" alt="Hero" />`,
`<a … href="https://example.com" target="_blank" rel="noopener noreferrer">Docs</a>`,
`<div id="panel" class="otter-text" style="transform: rotate(5deg); filter: blur(1px);">&lt;b&gt;not bold&lt;/b&gt;</div>`.

**Behaviour → JS.** `when X is clicked` → `addEventListener('click')`, `changed` → `'input'`.
**Any other event word is passed through verbatim as a DOM event name**
(`WebCompiler.cs` l.299-304). Verified: `when box is keydown` and
`when board is mousemove` pass `otter check` and compile to
`addEventListener('keydown'|'mousemove', …)` (`samples/keys.ot`). This is
undocumented and not in any decision. The desktop (WPF) provider only knows
`clicked`/`changed`/`closed` and rejects other names. Handlers get **no event
payload**: `k is key of event` checks and compiles. Per the generated code it then fails at runtime
("I can only read properties of a thing…") because the DOM event is not an Otter thing (`samples/keys2.ot`).

Other compiled features: `state`/`derive`/`when x changes` (reactivity
engine; `data-otter-bind`/`data-otter-if` evaluated with `new Function` + `with`);
routing (`route "/p" shows page`, D103, History API); HTTP client (D116A/B → `fetch` with
`AbortController`); WebSocket client (D106 → `new WebSocket`, `binaryType='arraybuffer'`);
crypto (D114 → Web Crypto); bytes, XML, CSV, JSON, dates, timers (`start timer` → `performance.now()`).
Runtime UI creation inside handlers is supported (D128).

**Refused on the web target (compile-time errors, verified):** `wait …`
("not supported on the web target yet"), `set random seed`, TCP and UDP ("Browsers
cannot listen on TCP ports"), web-server statements ("run with `otter serve`"),
database ("Browsers cannot connect directly to databases"). File I/O works only through the desktop bridge (D131: errors in a plain tab).

---

## 2. Per technology

### 2.1 HTML
- **FACT, native:** the page is generated whole. Element kinds are a fixed list
  (`WebCompiler.cs` l.20-28). Text and attribute values are HTML-escaped (`HtmlAttr`).
- **FACT, missing:** no raw-HTML embedding, no templates or partials, no way to add
  `<meta>`, `<link rel=…>` or `<script>` tags from Otter. There is no element-id or class
  control beyond the resource name becoming the `id`.
- **External:** any hand-authored HTML. (A user can edit the generated `.html`
  afterwards, but `otter web` overwrites it on every compile.)

### 2.2 CSS
- **FACT, native:** style properties per resource (`width`, `height`,
  `background`, `foreground`, `padding`, `margin`, `border`, `shadow`,
  `radius`/`round`, font properties, `position`/`top`/`left`/`zindex`, `flex`, `cursor`…;
  `WebCompiler.cs` l.~420-462). There is also `customstyle "…"`, raw CSS appended to the inline
  `style` (l.461). Property values are inserted into `style="…"` without CSS
  sanitisation (author-controlled).
- **FACT, native:** sidecar stylesheet. `<entry>.css` beside the entry file is
  inlined (D125). It must resolve inside the entry's folder (RC3 B4, symlink-checked).
  `otter new web|game` creates `main.css`. Because it is plain CSS, a developer
  *may* `@import` an external stylesheet; D127 allows this ("Developers may add external resources deliberately").
- **FACT, partial (code reading):** in a top-level resource's handler, only
  `text`/`value`/`background`/`foreground`/`width`/`height` (and `title`) map to DOM style or text
  (`JsCompiler.Statements.cs` ~l.1115-1122). Every other property (`left`, `top`, `source`…) is
  written to the element object (`el.left = …`) and does **not** change the rendering.
  Resources created at runtime (D128) use a fuller setter (`Templates.g.cs` ~l.640-720).
- **External:** Sass, PostCSS, Tailwind, CSS modules.

### 2.3 JavaScript
- **FACT:** Otter compiles *to* JS, but an Otter program cannot call JS:
  no FFI, no `import`, no `<script src>`, no inline-JS statement (searched
  `Otter.Language/Syntax*.cs`, `Parser.Ui.cs`).
- **FACT:** `window.otterFetch/otterGetJson/otterPostJson`, `httpGet/httpPost`,
  `otterStorage` (localStorage/sessionStorage) and `getStorage/setStorage` exist in the runtime
  (`Templates.g.cs` l.1811-1880) but **no Otter syntax reaches them**. They are present in
  both implementations. Otter's own HTTP statements compile to inline `fetch` code.
- **FACT:** state variables are mirrored onto `window[name]` and `otterState`.
  Hand-written JS on the same page could read them, but that is an internal detail, not a contract.
- **FACT, `fetch`:** yes, through Otter syntax (verified `samples/http.ot`):
  ```otter
  get json from "http://127.0.0.1:5000/api/scores" into scores
  post scores as json to "http://127.0.0.1:5000/api/scores" into saved
  start get from "http://127.0.0.1:5000/api/state" and call it req
      with timeout 5 seconds
  on complete of req
      say received response
  .
  ```
  (Note: `start get "url"` without `from` is a syntax error.)
- **Security notes (FACT):** the page has no CSP. The reactivity engine
  uses `new Function(...)` with `with(...)` (`Templates.g.cs` l.781, 1980), so a
  strict CSP needs `'unsafe-eval'` as well as `'unsafe-inline'` (or a hash) for the inline script.

### 2.4 TypeScript
- **FACT:** nothing. **External:** `tsc`/Vite/esbuild. Otter does not
  emit `.d.ts` files or typed API clients.

### 2.5 React
- **FACT:** nothing. Otter's `state`/`derive`/`when x changes` (D56 v1
  subset) is a separate, page-global reactivity model, not components, and it has known
  console/web differences (scope matrix: `when x changes` fires on equal reassignment on web).
- **External:** React, ReactDOM, JSX compilation, npm/Vite.

### 2.6 Three.js and React Three Fiber
- **FACT:** nothing. The `canvas` kind with `mode is "3d"` or `animation is "spin"`
  starts a **fixed demo**: a 12-edge wireframe cube projected onto a **2D** context with
  `requestAnimationFrame` (`Templates.g.cs` l.2024-2108). It reads hard-coded element
  ids from `examples/portal.ot` (`speedSlider`, `agreeCheckbox`, `themeDropdown`) and *any*
  `input[type=range]` on the page as its speed. There is no WebGL and no Otter-programmable scene.
  otterPS's own platform checklist reopened "3D game/demo" for this reason.
- **External:** Three.js, R3F (+ drei and the like), loaders (GLTF/Draco/KTX2/Meshopt), WebGL/WebGPU.

### 2.7 Static assets and MIME types
- **FACT:** `otter.json` `"assets": [...]` entries are validated (no absolute
  paths, no escape from the project, must exist) and copied into `dist/` with the same
  relative path by `otter build` (`ProjectBuild.cs` l.138-160, 232-244). Verified:
  a `game` project with `assets/ship.glb` and `assets/phys.wasm` builds to
  `dist/index.html`, `dist/assets/ship.glb`, `dist/assets/phys.wasm`, `dist/otter.build.json`.
  Pages refer to assets by relative URL (`image` `source`), which works from `file://`.
- **FACT:** there is **no MIME table anywhere**. `otter serve` serves no files
  (see §3). For a 3D site a static server must provide at least `.html .js .mjs .css .json .wasm (application/wasm)
  .glb (model/gltf-binary) .gltf (model/gltf+json) .bin .ktx2 (image/ktx2) .hdr .png .jpg .webp .svg`, audio types and fonts.
  Today that has to come from an external server (or the Vite dev server).

---

## 3. `otter serve`, `otter desktop`, and how they relate (FACT)

**`otter serve <file.ot> [-Port N]`** (`ServeCommand.cs`; otterPS `Otter.Server.psm1`, D51):
- The program is **checked but never run**. Top-level
  `when S receives METHOD at "/path"` routes are answered from their **first
  `respond with`**, and only a literal value is real. Any other expression is sent
  as the node's debug text. Verified on loopback (port 58731) with `samples/server.ot`:
  `GET /api/health` → `200 text/plain "ok"`. `GET /api/scores`
  (`respond with scores as json`) → `200 application/json "Variable@L13"`.
  `POST` with `respond with status 201` → 201. Unknown path → `404 {"error":"Not Found"}`.
  `/index.html` → 404. The request body is read (10 MB cap) but discarded.
  Path matching is exact, case-insensitive, with no route parameters.
  `HttpListener` binds `http://{host}:{port}/` (default `localhost:8080`).
- Consequence: **Otter cannot serve dynamic JSON or static files today.** The
  1.0 capability matrix records "Desktop/server listener hosting … BLOCKED BY ENVIRONMENT"
  (not certified).
- A real backend in Otter is currently possible only through the console runtime's raw
  sockets: `listen for tcp [on ADDR] on port N and call it S` (D113; defaults to `127.0.0.1`,
  `Interpreter.Network.cs` l.782), or by being a *client* (HTTP and WebSocket). There is no WebSocket server.

**`otter desktop <file.ot>`** (`DesktopCommand.cs`; Windows only. On macOS it prints
"desktop windows are only available on Windows. Build this program as a web application instead"):
- It is *not* WPF. It compiles the same web page, starts an authenticated loopback
  bridge (`HttpListener` on `127.0.0.1`, random 32-byte token, an Origin allowlist that includes
  `null` for `file://`), injects port and token into a `.desktop.html` copy, and opens it in Edge or Chrome
  `--app=`. The bridge serves `/api/fs/*`, `/api/terminal/exec`, `/api/system/*`,
  `/api/dialog/*`, `/api/timer/wait` and heartbeat. **It serves no static assets** (the page is `file://`).
- WPF is used by **`otter run`** for D44-D48 UI (`src/Otter.Desktop/WpfUiProvider.cs`). It has no
  `canvas`, `page` or `image` kinds.

**`otter new web|game`** (verified): both make `main.ot`, `main.css`,
`otter.json` (`"assets": []`, `build.outputDir: "dist"`, `scripts.start: "otter web ."`),
`tests/app_test.ot`. The `game` template is a page holding a 640x480 `canvas` with
`mode is "2d"`. The 2D mode does nothing at runtime (see GAME_DEVELOPMENT_GAPS.md).

---

## 4. Native vs external: the boundary

| Concern | Native Otter (today) | External tooling (stays external) |
|---|---|---|
| Page/UI for simple apps | `otter web`, `otter build` | — |
| Styling | properties, `customstyle`, sidecar CSS | CSS frameworks/preprocessors |
| Client logic | Otter compiled to JS (single file) | All JS/TS libraries |
| HTTP/WS client in browser | `get/post/put/delete`, `start get from … and call it`, `connect to websocket` | — |
| Build of TS/JSX/React/R3F | — | npm, Vite, tsc, React, Three.js, R3F |
| Static hosting with MIME and caching | — (copy-only via `assets`) | any static server; Vite dev server |
| Dynamic JSON API server | — (`otter serve` is literal-only) | currently external |
| Persistence | console: files, JSON, SQLite (`connect … into db`); browser: none reachable | browser storage/IndexedDB via JS |

---

## 5. Integration options

**Possible today (FACT, no changes):**
1. **An Otter page as a client of any JSON API.** `get json from URL into x` / `post x as json to URL`
   compile to `fetch`. Normal CORS rules apply. A page opened from
   `file://` has origin `null`, so the API must allow it, or the page must be served over HTTP by an external server.
2. **An Otter page that is a WebSocket client** of an external realtime server
   (`connect to websocket "ws://…" and call it room`; `on message from room`, `received message`).
3. **External frontends that talk to Otter-side logic only indirectly:** a console Otter program can
   produce JSON files (`convert … to json`, `write … to "file"`) or act as an HTTP/WS
   *client* of a backend. It cannot act as the HTTP server.
4. **Ship assets beside an Otter page** with `otter.json` `assets` and `otter build`.

**PROPOSALS (need owner approval; no syntax is implied):**
- P-F1: a runtime-only static file server for `otter serve` (or a new CLI
  command): MIME table (incl. `.wasm .glb .gltf .ktx2 .hdr .mjs`), `dist/` root,
  canonical-path and symlink checks, `ETag` and immutable caching for hashed names, optional
  range requests, SPA fallback for navigation only, loopback by default.
- P-F2: make `otter serve` execute route bodies (real dynamic JSON). This changes the
  observable 1.0 behaviour (today non-literal → node text), so it needs a SPEC decision. The
  otter-web-plan `WEB-001/002` ADRs already queue this.
- P-F3: `otter new` templates `web-react`, `web-three`, `web-r3f` that contain a Vite
  project plus an Otter backend (otter-web-plan `FRONTEND_3D_INTEGRATION.md`). They would need Node/npm as an explicit
  prerequisite. Dev: Vite proxies `/api` and `/ws` to Otter. Prod: Otter serves `dist/`.
- P-F4: a typed contract (OpenAPI/JSON Schema) generated from Otter routes so TS
  clients can be generated externally.
- P-F5: an opt-in CSP-compatible output mode (remove `new Function`/`with`; external
  script file with a hash). That is a runtime/compiler change only, but it affects 1.0 output byte parity.
- P-F6 (language-level, highest bar): any JS interop or "include script" syntax
  would be new grammar and a security-boundary decision. Not recommended before P-F1 and P-F2.

## 6. Discrepancies worth flagging (FACT)
- `docs/TUTORIALS.md` §6 ("Services & REST API Backend": `use http`, `create http server`,
  `return json {…}`, `payload.subtotal`) and §8 (game) use syntax that does
  not parse in 1.0.1. See GAME_DEVELOPMENT_GAPS.md for the checked failures. Dot access is UNSUPPORTED by design.
- The scope matrix calls `otter web` "SUPPORTED … with … asset embedding". There is no asset
  embedding: images are referenced by URL, and only the sidecar CSS is inlined.
- Arbitrary DOM event names pass through on the web, but desktop (WPF) rejects them. That is an
  undocumented cross-target difference.
