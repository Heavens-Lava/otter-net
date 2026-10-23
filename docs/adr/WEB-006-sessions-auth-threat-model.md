# WEB-006: Sessions, authentication and deployment threat model

**Status:** proposed (2026-10-23).

- Sessions: a random 256-bit id (`secure random bytes 32`) in an
  `HttpOnly`, `SameSite=Lax` (and `Secure` over HTTPS) cookie; data
  server-side (SQLite); rotated on login, deleted on logout, expiry.
  `../web/samples/proposals/sessions.ot` runs today.
- Passwords: existing `hash password` (PBKDF2-SHA256, 600k, constant-time
  verify). Argon2id only after a dependency/licence review.
- CSRF: per-session token for cookie-authenticated state changes, plus an
  `Origin` check.
- Deployment: loopback by default; production behind a reverse proxy with
  TLS; trusted-proxy list for client addresses; rate limits; health and
  readiness; structured logs; secrets from the environment.
- A written threat model and security review are required before any
  "production" wording (Phase 3).
