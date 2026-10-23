# WEB-001: Reuse the D51 server grammar behind an opt-in runtime

**Status:** proposed (2026-10-23). **Decider:** project owner.

## Context
Otter 1.0.1 (otterPS) certifies a web-server grammar (SPEC-DECISIONS D51)
whose runtime is a stub: `otter serve` sends the first `respond with` of
each route, literal values only (non-literals as node text,
`Variable@L13`), and `otter run` refuses routes. otter-net reproduces this
byte-for-byte. Changing it in place changes certified output.

## Options
1. **Fix `otter serve` in place.** Simplest for users; breaks byte parity
   with 1.0.1 and the certification.
2. **New grammar** (e.g. a new server statement). Needs a language
   decision and duplicates D51.
3. **Opt-in runtime (proposed):** a project declares the web runtime in
   `otter.json`; then `otter-net run .` gives D51's grammar real behaviour.
   Without the declaration nothing changes.
4. A new CLI command (e.g. `otter-net web-serve`). Also changes the 1.0.1
   CLI surface (unknown-command output) and splits the workflow.

## Decision (proposed)
Option 3. The manifest field is the only new contract; all syntax is
D51's. The certified `otter serve` stub stays as it is.

## Consequences
- 1.0.1 programs and commands unchanged; certification unaffected.
- Two behaviours for the same source depending on the manifest: documented
  prominently; `otter-net check .` reports which runtime a project uses.
- A future Otter 1.1 may promote the runtime to the default.
