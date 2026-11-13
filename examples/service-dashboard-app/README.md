# Otter Pulse — Microservice Telemetry & Web Dashboard

An end-to-end, production-grade microservice telemetry and observability dashboard built natively in the **Otter programming language**.

## Features

- **REST API Endpoints:**
  - `GET /api/health` — Heartbeat status and engine identity.
  - `GET /api/services` — List of all monitored microservices, their ports, status, and health scores.
  - `POST /api/services` — Dynamically register a new microservice with automatic slug generation and JSON persistence.
  - `GET /api/metrics` — Aggregate fleet metrics (total services, P95 response time, fleet uptime).
  - `POST /api/services/ping` — Simulate a live health probe on a microservice.
- **Frontend Dashboard:**
  - Dark glassmorphism interface built with Vanilla CSS and responsive grid layout.
  - Live filter tabs by service category (Core API, Database, Edge Network).
  - Real-time search bar filtering by name, port, or slug.
  - Modal dialog for registering new microservices directly from the browser.
- **Packs Consumed:**
  - `otter-official/file-helpers` (v1.0.0): JSON persistence for microservices.
  - `otter-official/text-utils` (v1.0.0): String slugification and text normalization.
  - `otter-official/math-utils` (v1.0.0): Telemetry averages, lerp, and clamped health score ratings.

## Running the Application

### 1. Start the Live Web Server

```bash
otter run examples/service-dashboard-app
```

The server will listen on `http://127.0.0.1:18092/`. Open this URL in any browser to interact with the live telemetry dashboard.

### 2. Run Self-Test Verification

```bash
otter run examples/service-dashboard-app --verify
```

### 3. Run Automated End-to-End Integration Suite

```bash
python3 examples/service-dashboard-app/tests/test_integration.py
```
