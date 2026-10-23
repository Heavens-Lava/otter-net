# Security, Reliability and Release Gates

## Security baseline
- Bind to loopback by default in development; no public interfaces without explicit config.
- HTTPS required in production (direct TLS or correctly configured trusted reverse proxy).
- Limit request bodies, upload sizes, concurrent connections, WebSocket frames, header counts and timeouts.
- Strict path canonicalization for static assets and uploads; prevent directory traversal, symlink escapes and executable upload handling.
- Parameterize SQL; avoid raw interpolation. Keep credentials out of source, logs and browser bundles.
- Encode HTML by default, validate redirects, defend CSRF for cookie-authenticated changes, define CORS allowlist, apply appropriate CSP.
- Cookies: HttpOnly, Secure in HTTPS, SameSite as appropriate; session rotation, expiry, logout invalidation.
- Use vetted password hashing (Argon2id or suitably configured maintained alternative), constant-time verification and rate limiting. Never invent cryptography.
- Enforce authorization per resource, not only route-level authentication.
- Validate WebSocket origin/auth, bounded queues, heartbeat, reconnect and cleanup.
- Dependencies: license check, advisories, SBOM/lockfiles, Native AOT trim warnings as failures where relevant.

## Test matrix
- Existing Otter 1.0.1 conformance and differential suites unchanged.
- .NET unit/integration tests; black-box HTTP tests; browser e2e for Vite/React/Three/R3F.
- All six existing platform targets; Windows desktop regression; packaging/install/uninstall regression where touched.
- Malformed HTTP, encoding, path traversal, SQL injection, XSS, CSRF, session fixation, cookie flags, CORS, WebSocket abuse, upload abuse.
- Load test for concurrency, large assets, graceful shutdown and cancellation.
- Verify no hidden PowerShell fallback or dependency on otterPS.
- Check baseline CLI output and error compatibility.

## Acceptance evidence
Each PR includes tested SHA, commands, counts, failing/skipped tests, exact platforms, reproducible demos, security notes and documented limitations. Do not claim full parity with PHP or production readiness without feature-by-feature evidence.

## Operational safeguards
No production deployment, domain changes, third-party account creation, paid service enrollment, public package release, credential use or direct modification of stable otterPS without explicit owner authorization. Use temporary local data only. No automated writes to real databases.
