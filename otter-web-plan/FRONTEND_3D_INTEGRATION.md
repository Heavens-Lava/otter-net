# Three.js, React Three Fiber, and 3D Websites

## Principles
- **Three.js** runs in the browser as JavaScript/TypeScript, not in the Otter interpreter.
- **React Three Fiber** (`@react-three/fiber`) is a React renderer for Three.js; it requires a React/JS build pipeline and compatible package versions.
- Otter serves built assets and provides JSON/WebSocket APIs, persistence, authentication, content metadata, and optional game-session services.
- Do not rewrite React, JSX, Vite, Three.js, R3F, WebGL or WebGPU in C#.

## Templates
1. `web-static`: HTML/CSS/JS + Otter API.
2. `web-react`: Vite + React + Otter API.
3. `web-three`: Vite + Three.js + Otter API.
4. `web-r3f`: Vite + React + R3F + Otter API.

Use maintained dependency versions, lockfiles, and explicit Node/npm prerequisites. Avoid assuming `otter-net` can execute `.tsx`; npm/Vite compiles it. Document dev mode (Vite frontend + Otter API) and production mode (Otter serves `dist/` + API). Cross-origin dev proxy must be explicit and restricted.

## Asset support
- Serve `.glb`, `.gltf`, `.bin`, `.ktx2`, `.hdr`, `.wasm`, audio, image, shader assets with correct MIME types and caching.
- Support immutable hashed assets; safe paths and traversal rejection; upload limits and asset quotas.
- Consider HTTP range requests for large files after benchmarking; validate CORS and content security policy for textures/workers/WASM.
- Client handles GLTF loading, Draco/Meshopt/KTX2 decoder distribution, GPU fallbacks, disposal and context loss.

## Example: 3D portfolio
- Browser displays a Three.js scene with camera, lights, GLB model and controls.
- Otter `/api/scenes/:id` returns saved scene metadata and permitted asset URLs.
- Browser saves edits to Otter `/api/scenes/:id` after authentication and validation.
- Otter stores metadata in SQLite; large assets are served as files/object storage.

## Example: multiplayer lobby spike
- Otter WebSocket endpoint manages authenticated room presence and text/state events.
- Client renders avatars with R3F. Network transport, tick rate, authoritative simulation, abuse limits and reconciliation are future design decisions.
- Avoid advertising production-ready multiplayer based on a chat-room demo.

## Game development boundary
Web 3D support is not the same as a general game engine. A later feasibility study should compare Otter scripting for game logic, an Otter-backed game server, browser game templates, and integration with established engines. Require performance and security evidence before choosing.
