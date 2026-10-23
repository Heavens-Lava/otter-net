# WEB-007: WebSockets and the multiplayer boundary

**Status:** proposed (2026-10-23).

- Otter has a WebSocket client (D106) on console and web, no server. A
  server needs new grammar (a decision in SPEC-DECISIONS), likely mirroring
  the TCP server (D113) and the client's `on message from` forms.
- Requirements: origin and session checks at the handshake, frame-size and
  queue limits, ping/pong, cleanup, per-connection rate limits.
- Scope: presence and simple state broadcast demos (Phase 5); authoritative
  game servers are Phase 6 and need separate approval
  ([../web/GAME_DEVELOPMENT_GAPS.md](../web/GAME_DEVELOPMENT_GAPS.md)).
