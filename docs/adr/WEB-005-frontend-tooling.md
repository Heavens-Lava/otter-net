# WEB-005: Frontend tooling and assets

**Status:** proposed (2026-10-23). Details:
[../web/FRONTEND_COMPATIBILITY.md](../web/FRONTEND_COMPATIBILITY.md).

- Otter does not transpile TypeScript, parse JSX or implement WebGL. npm
  and Vite build HTML/CSS/JS/TS/React/Three.js/R3F; Node is an explicit
  prerequisite of the React/3D templates only.
- Development: Vite dev server proxies `/api` and `/ws` to the Otter
  runtime (loopback). Production: the runtime serves Vite's `dist/`
  (`static is "dist"`), SPA fallback only for HTML navigation requests,
  never for `/api/*` or `/ws/*`; hashed assets immutable; MIME types for
  `.js .mjs .css .wasm .glb .gltf .bin .ktx2 .hdr`, fonts, audio.
- Templates `web-static`, `web-react`, `web-three`, `web-r3f` with pinned
  lockfiles; `otter-net` never installs global npm packages.
- The existing `otter web` compiler (Otter UI → one HTML page) stays as is.
