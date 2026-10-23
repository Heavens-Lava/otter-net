# Otter 1.0.1 / otter-net: game-development capability audit

Date: 2026-10-22. Same method and sources as `FRONTEND_COMPATIBILITY.md`. Every syntax form
was checked with the otter-net Native AOT binary (`Otter 1.0.1`, macOS). Desktop programs were
only checked, never run. Console-only samples were run with a time limit. Samples:
`docs/web/samples/frontend/`. **FACT** = verified. **PROPOSAL** = an idea only. Proposals are not
approved and introduce no Otter syntax.

## 0. Bottom line (FACT)

Otter 1.0.1 has **no game runtime**: no drawing API, no frame loop or tick, no
keyboard/mouse/gamepad state or payload, no sprites, audio, collision, physics, scene
graph or 3D renderer. No SPEC decision (D1-D133) mentions canvas or games.
What exists is: general-purpose math (degrees-based trig, sqrt, random), custom
types, lists, a monotonic stopwatch, `wait` (console/desktop only), an
event loop for I/O (D121), networking clients (HTTP, WebSocket, TCP/UDP, a TCP server on
console/desktop), persistence (files/JSON, SQLite on console), and a web `canvas`
element that Otter code **cannot draw on**.

`otter new game g` (verified) creates `main.ot` (a page plus a 640x480 `canvas` with
`mode is "2d"`), `main.css` (`body { margin: 0; background: #000; }`), `otter.json`
(`"archetype": "game"`, `"target": "game"`, `scripts.start: "otter web ."`) and
`tests/app_test.ot`. It compiles to an empty `<canvas>`. In 2D mode the runtime does nothing with it
(`Templates.g.cs` l.2024-2028). `otter build` treats `game` as a web target
(`ProjectBuild.cs` l.18). `otter publish` labels it "Modern web browser with HTML5 Canvas / WebGL enabled"
(l.501), but no WebGL code exists.

**Documentation that does not match the language (FACT):**
- `otterPS/docs/TUTORIALS.md` §8 (the game tutorial) does not check:
  `if ballX is less than radius or ballX is greater than (800 - radius)` → "I don't understand '('".
  Otter has **no parentheses and no operator precedence** (`docs/GRAMMAR.md` l.187-189:
  `2 plus 3 times 4` is `20`, verified). `when game tick with delta` → syntax error.
  `draw circle at …` → syntax error. `clear canvas` and `play sound "x"` *check*
  as calls to user functions and fail at run time ("Otter could not find anything called
  'clear'/'play'"). `game is a canvas with width 800, height 600, targetFps 60` checks and
  compiles, but `targetFps` is ignored. `when game is tick` compiles to
  `addEventListener('tick')`, and nothing ever dispatches that event.
- `otterPS/projects/test-certified-game/game.ot` (`game with width 640 and height 480` /
  `on tick`) → 3 syntax errors.
- `OTTER_PROGRAMMING_LANGUAGE_COMPLETE_PLATFORM_CHECKLIST.md` §24-25 ticks "Game loop",
  "Delta time", "Keyboard input", "Pointer/mouse input", "Lines/Rectangles/Circles", and §26 ticks "WebGPU/WebGL
  provider". None of these has reachable syntax in 1.0.1. The same checklist correctly reopens "3D
  game/demo" ("only a fixed spinning wireframe cube exists").

---

## 1. Per area

### 1.1 Rendering / canvas / 2D drawing
- **Exists:** the web `canvas` resource (`WebCompiler.cs` l.580) → `<canvas width height data-mode data-animation>`.
  Verified syntax:
  ```otter
  board is a canvas
      width is 640
      height is 480
      mode is "3d"
      animation is "spin"
  .
  ```
  `mode "3d"`, `animation "spin"` or `animation "3d"` start a built-in wireframe-cube animation.
  It draws on a **2D context** with `requestAnimationFrame`, reads the `speed`/`color`/`glow`
  properties, and reads demo-specific ids (`speedSlider`, `agreeCheckbox`, `themeDropdown` from
  `examples/portal.ot`) or any `input[type=range]` on the page.
- **Missing:** any statement that draws (shapes, paths, images, text), clears or
  transforms. Desktop (WPF, `otter run`) has no canvas kind at all. Non-canvas
  "DOM sprites" are not workable either: in a top-level resource's handler, `left of ship is x`
  compiles to `otterSetProperty` → `el.left = x` and does not move the element (per
  generated code, `samples/domsprite.ot`). Only text/value/background/foreground/width/height are style-mapped.
- **Future needs (PROPOSAL):** an immediate-mode 2D draw-list API on the
  web target (batched per frame, Canvas2D first). Possibly a retained sprite layer later. If
  ever, WebGL/WebGPU as a separate provider. Performance: command buffering, no per-call
  DOM lookups (today's helpers call `getElementById` on every access). Security: images loaded only
  from same-origin or project assets, and no `eval` paths.

### 1.2 Game loop, timers, frames
- **Exists:**
  `start timer clock` / `elapsed milliseconds of clock` / `elapsed time of clock` (D101, console and
  web; web uses `performance.now()`). `wait N milliseconds|seconds|…` (console and desktop only;
  the web refuses it at compile time: "`wait ...` is not supported on the web target yet").
  Verified console loop:
  ```otter
  frame is 0
  start timer clock
  while frame is less than 3
      add 1 to frame
      wait 16 milliseconds
  .
  say frame elapsed milliseconds of clock
  ```
  Output: `3 53.5…` (about 17.8 ms per `wait 16 ms`). D121: events are cooperative, with
  "no real-time guarantee", and polling delay is allowed.
- **Missing:** a per-frame callback (no `requestAnimationFrame` hook), a fixed timestep,
  interval timers, pause and resume. On the web there is no way to run code repeatedly
  except event handlers.
- **Future needs (PROPOSAL):** a host-driven frame event with delta time (rAF on
  web, a dispatcher timer on desktop) that fits D121 (one handler at a time, run to
  completion). Also a fixed-step accumulator option, frame-budget diagnostics, and
  handling of background tabs. The web `wait` was deferred because it "needs an async/cancellation
  model" (D101). A frame event must not reopen that question implicitly.

### 1.3 Input (keyboard, mouse, touch, gamepad)
- **Exists:** UI events. Desktop/WPF (`WpfUiProvider.cs` l.67-78): button/variants
  `clicked`, card `clicked` (PreviewMouseDown), text box `changed`, window `closed`.
  Web: `clicked`→`click`, `changed`→`input`, D110 drag/drop (`on drop on X`, `drop x`,
  `drop y` ambient). **Undocumented:** on the web any other event word becomes a raw DOM listener.
  Verified, `when box is keydown` and `when board is mousemove` check and compile to
  `addEventListener('keydown'|'mousemove')`. Handlers get **no payload**:
  `k is key of event` checks, but per the generated code it throws at runtime. WPF rejects these
  event names ("A text box has no event called …").
- **Missing:** key identity, key-down state, pointer position/buttons, wheel, touch,
  pointer lock, gamepad, input mapping. Also focus/`hide`/`show` UI actions (D56,
  planned for 1.1).
- **Future needs (PROPOSAL):** an input-state snapshot read during a frame
  (pressed and held key sets, pointer x/y relative to the canvas) rather than per-event payloads,
  plus a decision on whether the raw DOM-event pass-through is kept, specified or refused.
  Security: no key capture outside a focused canvas. Respect browser user-gesture rules.

### 1.4 Vectors, math, transforms
- **Exists (portable console and web):** `plus minus times divided by`, `power`,
  `percent of`, `square root of`, `absolute value of`, `round/round up/round down of`,
  `larger of A and B`, `smaller of …`, `sine/cosine/tangent of` (**degrees**, D90),
  `log of`, `natural log of`, `pi`. `random number from 1 to 10 into n` (inclusive),
  `random item from list into x` (D30). `set random seed to N` works on console only
  (verified reproducible: `47` twice). On the web it is refused. Custom types:
  ```otter
  a Point has
      x
      y
  player is a Point with x 50, y 50
  x of player is 3
  ```
  (verified through `benchmarks/game_loop.ot` and `benchmarks/3d_render.ot`, which check, run and
  web-compile. The second does manual rotation, perspective projection, backface culling and
  Lambert shading in pure Otter).
- **Constraints that matter for game code (FACT):** no parentheses; strict
  left-to-right arithmetic; `a` is reserved (`a is …` fails); there is no inverse trig
  (`atan2`, `asin`…), `round down of`/`round up of` serve as floor/ceil. There is **no modulo/remainder operator** (`17 modulo 5`, `17 % 5`, `17 mod 5` are syntax errors; `modulo` is only a reserved word in SPECIFICATION.md), so wrap-around needs repeated subtraction or `round down` arithmetic.
  `b is a Point with …` cannot be repeated for the same name in a loop ("Otter will not replace existing a Body called
  'b'"). Construction has to go through a function (`to makeBody startX … return nb`). There are no
  vector or matrix types.
- **Future needs (PROPOSAL):** a vector/transform library (Vector2/3, Matrix,
  Quaternion) and inverse trig, either as Otter library modules (`use "vec.ot"`,
  which is possible today but slow and verbose) or as runtime built-ins. Built-ins need a SPEC
  decision, and degrees vs radians must be decided consistently with D90.

### 1.5 Collision / physics
- **Exists:** nothing beyond hand-written arithmetic (`benchmarks/game_loop.ot` does
  boundary bounces and distance checks).
- **Future needs (PROPOSAL):** AABB/circle tests as library functions first. A physics
  engine (Box2D/Rapier/Cannon) would be an external JS/WASM dependency on the web, which requires a JS-interop
  and asset-hosting decision (see FRONTEND P-F1/P-F6).

### 1.6 Animation / tweening
- **Exists:** CSS-only animation in the **experimental** D56 declarative UI (`enter`
  `fade in`/`move up 20`/`animate 300ms ease-out`, `hover` `grow 1.05` → `@keyframes`/transitions,
  `DeclarativeRenderer.cs`). It is outside the 1.0 surface, and `otter run` refuses
  `UiEvent` ("UI event blocks are not supported in Otter 1.0"). Separately, the fixed cube spin.
- **Missing:** value tweens, sprite-sheet animation, easing on arbitrary values, timelines.

### 1.7 Audio
- **Exists:** nothing. `play sound "boom.wav"` checks only because it parses as a call
  to a user function named `play`. `otter run` → "Otter could not find anything called 'play'".
- **Future needs (PROPOSAL):** a Web Audio provider on the web (decode, play, volume, loop),
  with autoplay/user-gesture rules and assets served with correct MIME. Desktop parity would need its own design.

### 1.8 3D scenes
- **Exists:** only the wireframe-cube demo (above) and pure-math 3D (`3d_render.ot`).
- **Recommended direction (consistent with `otter-web-plan/FRONTEND_3D_INTEGRATION.md`):**
  3D rendering stays in Three.js/R3F (external). Otter provides hosting, JSON/WebSocket APIs
  and persistence. Building a native Otter 3D engine is a separate feasibility study.

### 1.9 Multiplayer networking
- **Exists (FACT, all checked):**
  - WebSocket **client**, console, desktop and web (D106):
    ```otter
    connect to websocket "ws://127.0.0.1:9001/room" and call it room
    on open of room
        send "hello" through room
    .
    on message from room
        say received message
    .
    on close of room
        say close code
    .
    ```
    Web: `new WebSocket(url)`, `binaryType='arraybuffer'`. Only `ws://`/`wss://` are accepted.
  - TCP client and server, UDP: console and desktop only. The web refuses them.
    ```otter
    listen for tcp on port 9100 and call it lobby
    on connection to lobby
        peer is incoming connection
        send bytes from text "welcome" through peer
    .
    open udp on port 9101 and call it beacon
    on data from beacon
        say sender address
    .
    ```
    The TCP server binds `127.0.0.1` unless an address is given (`Interpreter.Network.cs` l.782).
    TLS client is D112. **There is no TLS server and no WebSocket server.**
  - HTTP client with request handles (D116A/B). `otter serve` is literal-only (see FRONTEND §3).
- **Missing:** a WebSocket server (so an Otter program cannot be the game server for browser
  clients), rooms, tick-based state sync, binary message framing helpers, auth/session.
- **Future needs (PROPOSAL):** a WebSocket server endpoint (origin and auth checks, max
  frame/message size, bounded per-connection queues, heartbeat, backpressure, idle timeouts,
  connection caps, rate limits). An authoritative-server model with a fixed tick, and
  client prediction left to the JS client. Load evidence would come before any "multiplayer" claim
  (the otter-web-plan already warns against advertising a chat demo as multiplayer).

### 1.10 Persistence (save games, scores)
- **Exists (FACT):** console: files (`write … to "save.json"`, `read "save.json" into x`),
  JSON (`convert save to json into t`, `convert t from json into r`), verified round trip
  (`samples/persist.ot` prints `3`). SQLite through `connect database into db` / `execute db with
  "…" parameter "title" is v into r` (D97) runs on macOS here (`samples/db.ot`). The scope
  matrix still lists Database as DEFERRED, which is inconsistent with the implementation.
  Secrets: `store secret` (Windows). Web: files only through the desktop bridge (D131 errors in a plain
  tab). Database is refused on the web. `localStorage` helpers exist in the page runtime but
  **no Otter syntax reaches them**.
- **Future needs (PROPOSAL):** a browser key-value save (localStorage/IndexedDB) with
  quota and error semantics, or a server-side save API once `otter serve` is dynamic.

---

## 2. Performance indications (FACT, single runs, not benchmarks)
- `benchmarks/game_loop.ot` (60 frames, 2 entities): 0.02 s wall. `3d_render.ot`: 0.03 s wall
  (console, Native AOT).
- `samples/perf.ot`: 1,000 custom-type bodies × 60 frames, one property read/compute/write
  plus one comparison each, measured with `start timer`: **~95 ms total ≈ 1.6 ms per frame** on
  the console interpreter. It suggests simple 2D logic for a few hundred to a few thousand entities could fit a
  16 ms budget on console. Web performance (compiled JS) was not measured. The generated JS wraps
  every assignment in state checks and helper calls, so web numbers need their own measurement.

## 3. What any future game design would need (PROPOSAL checklist, needs approval)
1. A SPEC decision for each new surface (frame event, draw API, input state, audio, vectors).
   Syntax must be proposed with `otter check`-verified examples and must preserve 1.0.1
   behaviour (otterPS stays the frozen reference).
2. Target matrix first: web-only vs web+desktop (WPF has no canvas). Use the D121 event
   contract for frame and input dispatch.
3. Decide the raw DOM-event pass-through: specify it or refuse it, since today it is an
   undocumented web/desktop divergence.
4. Assets: static serving with a MIME table, path canonicalisation and caching (FRONTEND P-F1).
5. Security: no JS eval paths added. CSP-friendly output (today's runtime needs
   `unsafe-eval`). Network servers loopback by default, with bounded resources.
6. Fix or retire `docs/TUTORIALS.md` §6 and §8, `projects/test-certified-game`, and the
   checklist items ticked without reachable syntax. That is documentation in otterPS, which is frozen, so the fix
   is a decision for the owner.
