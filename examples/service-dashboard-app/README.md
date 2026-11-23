# Otter Pulse — Microservice Telemetry & Web Dashboard

Flagship reference web application built natively in **Otter 1.0.1**, demonstrating Schema v2 Pack composition, Kestrel REST APIs, real outbound HTTP health probing via Otter's native HTTP client (`Http.cs`), high-resolution latency timing (`start timer` / `elapsed milliseconds`), JSON persistence, and a responsive web dashboard.

## Official Schema v2 Packs Consumed

- `otter-official/file-helpers` (`1.0.1`) — safe JSON store persistence (`readOrFallback`, `writeSafe`, `deleteFileSafe`)
- `otter-official/text-utils` (`1.0.1`) — service slugification & case normalization (`slugify`, `toLower`)
- `otter-official/math-utils` (`1.0.1`) — fleet telemetry averages & bounded health/timeout clamping (`calcAverage`, `clamp`)

## Capabilities

- **Live Otter HTTP Health Probes (`POST /api/services/ping`):** Executes real server-side outbound HTTP `GET` requests from Otter (`get targetUrl into probeBody with timeout safeTimeout seconds`), measures round-trip latency with Otter's built-in timer, handles timeouts and connection failures gracefully via `try ... otherwise into reason`, and updates persisted health scores and fleet status.
- **Full REST API:**
  - `GET /api/health` — Heartbeat status, version, engine identity, and `probeMode: "live-http"`.
  - `GET /api/services` — List all monitored services with `probeUrl`, `status`, `latencyMs`, `healthScore`, `checks`, and `lastError`.
  - `POST /api/services` — Register a microservice with validation (`400`), duplicate slug conflict detection (`409`), and optional custom `probeUrl`.
  - `PUT /api/services` — Update service port, category, target uptime, or `probeUrl`.
  - `DELETE /api/services` — Unregister a microservice (`200` / `404`).
  - `GET /api/metrics` — Aggregate fleet totals (`totalServices`, `healthyServices`, `degradedServices`, `downServices`, `averageLatencyMs`, `overallUptime`).
  - `POST /api/services/ping` — Run a live HTTP health probe (`{ slug, probeUrl?, timeoutSeconds? }`).
- **Responsive Dashboard UI (`public/`):** Live fleet metrics, search and category filtering, Probe HTTP actions, error banners for failed probes, and service registration modal.

## Running Locally

### 1. Verify Packs & Run Self-Test
```bash
cd examples/service-dashboard-app
otter pack verify --contracts
otter run src/main.ot --verify
```

### 2. Start the Live Web Dashboard
```bash
otter run examples/service-dashboard-app
```
Then open `http://127.0.0.1:18092/` in your browser.

### 3. Run End-to-End Integration Tests (with Local Probe Server)
```bash
python3 examples/service-dashboard-app/tests/test_integration.py
```
This spins up a deterministic local HTTP probe server testing healthy, degraded, unhealthy (`503`), timeout (`0.3s`), and connection-refused probes plus CRUD and restart persistence.
