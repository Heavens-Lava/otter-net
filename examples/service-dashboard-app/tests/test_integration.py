#!/usr/bin/env python3
"""
End-to-End Integration Test Suite for Otter Pulse (examples/service-dashboard-app)

Tests real server-side Otter HTTP health probes against a deterministic local HTTP probe server:
1. Full REST CRUD: GET, POST (201), PUT (200), DELETE (200)
2. Input validation (400), duplicate conflict detection (409), not found (404)
3. Real Otter Http.cs outbound HTTP probes via POST /api/services/ping:
   - Healthy JSON endpoint (200 OK -> reachable=True, status='healthy', real measured latencyMs)
   - Degraded JSON endpoint (200 OK status='degraded' -> status='degraded', reduced healthScore)
   - Unhealthy JSON endpoint (503 status='down' -> status='down', healthScore=5, error captured)
   - Slow endpoint exceeding timeoutSeconds -> real Otter Http.cs timeout caught gracefully via try/otherwise
   - Closed/unreachable localhost port -> connection refused caught gracefully without crashing server
4. Fleet metrics aggregation (healthy/degraded/down counts, average latency, uptime)
5. Static dashboard frontend asset serving (GET /)
6. Multi-process state persistence across clean server restarts
"""

import http.server
import json
import os
import socket
import socketserver
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request


class ProbeTargetHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass  # Keep test output clean

    def do_GET(self):
        if self.path == "/health":
            payload = json.dumps({"status": "healthy", "service": "probe-target"}).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        elif self.path == "/degraded":
            time.sleep(0.03)
            payload = json.dumps({"status": "degraded", "detail": "queue depth elevated"}).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        elif self.path == "/unhealthy":
            payload = json.dumps({"status": "down", "error": "database connection pool exhausted"}).encode("utf-8")
            self.send_response(503)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        elif self.path == "/slow":
            time.sleep(1.5)
            payload = json.dumps({"status": "healthy"}).encode("utf-8")
            try:
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
            except BrokenPipeError:
                pass
        else:
            self.send_response(404)
            self.end_headers()


class ThreadingHttpServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def get_unused_port():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def http_request(url, method="GET", data=None):
    payload = json.dumps(data).encode("utf-8") if data is not None else None
    headers = {"Content-Type": "application/json"} if data is not None else {}
    req = urllib.request.Request(url, data=payload, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=8) as response:
            body = response.read().decode("utf-8")
            return response.status, json.loads(body) if body else {}
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8")
        try:
            return e.code, json.loads(body)
        except Exception:
            return e.code, {"raw": body}


def http_get_raw(url):
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, timeout=5) as response:
        return response.status, response.read().decode("utf-8")


def wait_for_server(url, timeout_secs=12):
    start = time.time()
    while time.time() - start < timeout_secs:
        try:
            status, _ = http_request(url, method="GET")
            if status == 200:
                return True
        except Exception:
            time.sleep(0.2)
    return False


def main():
    root_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
    app_dir = os.path.join(root_dir, "examples", "service-dashboard-app")
    release_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Release", "net10.0", "otter.dll")
    debug_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

    if len(sys.argv) > 1 and sys.argv[1]:
        otter_cmd = [sys.argv[1]]
    elif os.path.exists(release_dll):
        otter_cmd = ["dotnet", release_dll]
    elif os.path.exists(debug_dll):
        otter_cmd = ["dotnet", debug_dll]
    else:
        otter_cmd = ["otter"]

    store_path = os.path.join(app_dir, "services_store.json")
    if os.path.exists(store_path):
        os.remove(store_path)

    # Start local deterministic HTTP probe target server
    probe_server = ThreadingHttpServer(("127.0.0.1", 0), ProbeTargetHandler)
    probe_port = probe_server.server_address[1]
    probe_thread = threading.Thread(target=probe_server.serve_forever, daemon=True)
    probe_thread.start()
    closed_port = get_unused_port()

    base_url = "http://127.0.0.1:18092"
    health_url = f"{base_url}/api/health"

    print("=" * 72)
    print(f" Otter Pulse End-to-End Integration Suite (probe target 127.0.0.1:{probe_port})")
    print("=" * 72)

    proc = subprocess.Popen(
        otter_cmd + ["run", app_dir],
        cwd=app_dir,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )

    try:
        if not wait_for_server(health_url, timeout_secs=14):
            proc.terminate()
            out, err = proc.communicate(timeout=3)
            print("[FAIL] Otter Pulse server did not start in time!")
            print("STDOUT:", out)
            print("STDERR:", err)
            sys.exit(1)

        print("  [PASS] Otter Pulse web server online on 127.0.0.1:18092.")

        # 1. Health endpoint
        status, health = http_request(health_url)
        assert status == 200 and health.get("status") == "healthy"
        assert health.get("probeMode") == "live-http", f"Expected probeMode=live-http, got {health}"
        print("  [PASS] GET /api/health reports live-http probe mode.")

        # 2. Initial services & validation errors
        status, svcs = http_request(f"{base_url}/api/services")
        assert status == 200 and len(svcs) >= 4
        status, _ = http_request(f"{base_url}/api/services", method="POST", data={"name": "", "port": 8080})
        assert status == 400
        status, _ = http_request(f"{base_url}/api/services", method="POST", data={"name": "Bad Port", "port": 99999})
        assert status == 400
        print("  [PASS] Initial catalog and 400 input validation verified.")

        # 3. Register service pointing at our live local probe target
        reg = {
            "name": "Telemetry Probe Target",
            "port": probe_port,
            "category": "edge",
            "targetUptime": 99.9,
            "probeUrl": f"http://127.0.0.1:{probe_port}/health"
        }
        status, created = http_request(f"{base_url}/api/services", method="POST", data=reg)
        assert status == 201 and created.get("slug") == "telemetry-probe-target"
        assert created.get("probeUrl") == reg["probeUrl"]

        # Duplicate conflict
        status, dup = http_request(f"{base_url}/api/services", method="POST", data=reg)
        assert status == 409 and "already exists" in dup.get("error", "")
        print("  [PASS] POST /api/services (201 Created + 409 Conflict) verified.")

        # 4a. Real Otter HTTP probe -> Healthy 200 JSON target
        status, p_ok = http_request(
            f"{base_url}/api/services/ping",
            method="POST",
            data={"slug": "telemetry-probe-target", "timeoutSeconds": 2}
        )
        assert status == 200, f"Expected 200, got {status}: {p_ok}"
        assert p_ok.get("reachable") is True, f"Expected reachable=True: {p_ok}"
        assert p_ok.get("status") == "healthy", f"Expected healthy: {p_ok}"
        assert p_ok.get("latencyMs", 0) >= 1, f"Expected positive measured latencyMs: {p_ok}"
        assert p_ok.get("checks") == 1
        assert p_ok.get("healthScore", 0) >= 80
        print(f"  [PASS] Real HTTP probe (healthy): {p_ok['latencyMs']}ms, score={p_ok['healthScore']}/100.")

        # 4b. Real Otter HTTP probe -> Degraded JSON target
        status, p_deg = http_request(
            f"{base_url}/api/services/ping",
            method="POST",
            data={
                "slug": "telemetry-probe-target",
                "probeUrl": f"http://127.0.0.1:{probe_port}/degraded",
                "timeoutSeconds": 2
            }
        )
        assert status == 200 and p_deg.get("reachable") is True
        assert p_deg.get("status") == "degraded", f"Expected degraded: {p_deg}"
        assert p_deg.get("healthScore", 100) <= 75, f"Expected degraded score <=75: {p_deg}"
        assert "queue depth" in p_deg.get("error", "")
        print(f"  [PASS] Real HTTP probe (degraded): score={p_deg['healthScore']}/100, detail='{p_deg['error']}'.")

        # 4c. Real Otter HTTP probe -> Unhealthy 503 JSON target
        status, p_down = http_request(
            f"{base_url}/api/services/ping",
            method="POST",
            data={
                "slug": "telemetry-probe-target",
                "probeUrl": f"http://127.0.0.1:{probe_port}/unhealthy",
                "timeoutSeconds": 2
            }
        )
        assert status == 200 and p_down.get("status") == "down", f"Expected down: {p_down}"
        assert p_down.get("healthScore") == 5
        assert "pool exhausted" in p_down.get("error", "")
        print(f"  [PASS] Real HTTP probe (503 unhealthy): status=down, score=5, error='{p_down['error']}'.")

        # 4d. Real Otter HTTP probe -> Slow endpoint timeout (0.3s timeout vs 1.5s server sleep)
        status, p_to = http_request(
            f"{base_url}/api/services/ping",
            method="POST",
            data={
                "slug": "telemetry-probe-target",
                "probeUrl": f"http://127.0.0.1:{probe_port}/slow",
                "timeoutSeconds": 0.3
            }
        )
        assert status == 200
        assert p_to.get("reachable") is False, f"Expected timeout reachable=False: {p_to}"
        assert p_to.get("status") == "down", f"Expected timeout status=down: {p_to}"
        assert "timed out" in p_to.get("error", "").lower(), f"Expected timeout message: {p_to}"
        assert p_to.get("latencyMs", 0) >= 200, f"Expected measured timeout duration >=200ms: {p_to}"
        print(f"  [PASS] Real HTTP probe (timeout): caught gracefully in {p_to['latencyMs']}ms ('{p_to['error']}').")

        # 4e. Real Otter HTTP probe -> Connection refused on unused localhost port
        status, p_ref = http_request(
            f"{base_url}/api/services/ping",
            method="POST",
            data={
                "slug": "telemetry-probe-target",
                "probeUrl": f"http://127.0.0.1:{closed_port}/health",
                "timeoutSeconds": 1
            }
        )
        assert status == 200
        assert p_ref.get("reachable") is False and p_ref.get("status") == "down"
        assert p_ref.get("error", ""), f"Expected connection error message: {p_ref}"
        print(f"  [PASS] Real HTTP probe (connection refused): caught gracefully ('{p_ref['error']}').")

        # 4f. Metrics reflect down service cleanly + missing service returns 404
        status, metrics = http_request(f"{base_url}/api/metrics")
        assert status == 200 and metrics.get("totalServices") == 5
        assert metrics.get("downServices", 0) >= 1, f"Expected downServices >= 1: {metrics}"
        status, _ = http_request(f"{base_url}/api/services/ping", method="POST", data={"slug": "no-such-service"})
        assert status == 404
        print(f"  [PASS] GET /api/metrics reflects live probe health counts ({metrics}).")

        # 5. PUT update + DELETE + 404 checks
        status, updated = http_request(f"{base_url}/api/services", method="PUT", data={
            "slug": "telemetry-probe-target",
            "port": probe_port,
            "category": "api",
            "targetUptime": 99.95,
            "probeUrl": f"http://127.0.0.1:{probe_port}/health"
        })
        assert status == 200 and updated.get("category") == "api"
        status, del_res = http_request(f"{base_url}/api/services", method="DELETE", data={"slug": "telemetry-probe-target"})
        assert status == 200 and del_res.get("deleted") is True
        status, _ = http_request(f"{base_url}/api/services", method="DELETE", data={"slug": "telemetry-probe-target"})
        assert status == 404
        print("  [PASS] PUT / DELETE CRUD and 404 handling verified.")

        # 6. Static dashboard frontend asset
        status, html = http_get_raw(f"{base_url}/")
        assert status == 200 and "Otter" in html and "svc-probe-url" in html
        print("  [PASS] GET / serves updated Otter Pulse frontend.")

    finally:
        proc.terminate()
        try:
            proc.wait(timeout=4)
        except subprocess.TimeoutExpired:
            proc.kill()
        probe_server.shutdown()
        probe_server.server_close()

    # Stage 2: Persistence across server restarts
    print("Stage 2: Verifying multi-process persistence across clean restart...")
    proc2 = subprocess.Popen(otter_cmd + ["run", app_dir], cwd=app_dir, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        assert wait_for_server(health_url, timeout_secs=12), "Server 2 failed to start"
        status, _ = http_request(f"{base_url}/api/services", method="POST", data={
            "name": "Audit Log Collector",
            "port": 7070,
            "category": "database",
            "targetUptime": 99.99,
            "probeUrl": "http://127.0.0.1:7070/health"
        })
        assert status == 201
    finally:
        proc2.terminate()
        try:
            proc2.wait(timeout=4)
        except subprocess.TimeoutExpired:
            proc2.kill()

    proc3 = subprocess.Popen(otter_cmd + ["run", app_dir], cwd=app_dir, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        assert wait_for_server(health_url, timeout_secs=12), "Server 3 failed to start"
        status, recovered = http_request(f"{base_url}/api/services")
        assert status == 200
        match = [s for s in recovered if s.get("slug") == "audit-log-collector"]
        assert len(match) == 1 and match[0].get("probeUrl") == "http://127.0.0.1:7070/health"
        print("  [PASS] Persisted service & probeUrl restored cleanly across restart.")
    finally:
        proc3.terminate()
        try:
            proc3.wait(timeout=4)
        except subprocess.TimeoutExpired:
            proc3.kill()
        if os.path.exists(store_path):
            os.remove(store_path)

    print("=" * 72)
    print(" ALL OTTER PULSE INTEGRATION TESTS PASSED (100%)")
    print("=" * 72)


if __name__ == "__main__":
    main()
