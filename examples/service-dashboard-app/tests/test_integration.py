#!/usr/bin/env python3
"""
Integration & End-to-End Test Suite for Otter Service Dashboard Web Application
Boots the native Otter Web Server, verifies REST API endpoints (/api/health, /api/services,
/api/metrics, /api/services/ping), validates static dashboard serving (GET /), and tests
multi-process telemetry persistence across server restarts.
"""

import os
import sys
import time
import json
import urllib.request
import urllib.error
import subprocess

def http_get(url):
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, timeout=5) as response:
        return response.status, json.loads(response.read().decode("utf-8"))

def http_get_raw(url):
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, timeout=5) as response:
        return response.status, response.read().decode("utf-8")

def http_post(url, data):
    payload = json.dumps(data).encode("utf-8")
    req = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=5) as response:
        return response.status, json.loads(response.read().decode("utf-8"))

def wait_for_server(url, timeout_secs=10):
    start = time.time()
    while time.time() - start < timeout_secs:
        try:
            status, data = http_get(url)
            if status == 200:
                return True
        except Exception:
            time.sleep(0.2)
    return False

def main():
    root_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
    app_dir = os.path.join(root_dir, "examples", "service-dashboard-app")
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

    if os.path.exists(cli_dll):
        otter_cmd = ["dotnet", cli_dll]
    else:
        otter_cmd = ["otter"]

    store_path = os.path.join(app_dir, "services_store.json")
    if os.path.exists(store_path):
        os.remove(store_path)

    base_url = "http://127.0.0.1:18092"
    health_url = f"{base_url}/api/health"

    print("=" * 70)
    print(" Testing Service Dashboard Application End-to-End (Phase 8)")
    print("=" * 70)

    # -------------------------------------------------------------
    # Stage 1: Launch Web Server & Test Live REST Endpoints
    # -------------------------------------------------------------
    print("Stage 1: Booting Otter Web Server on 127.0.0.1:18092...")
    proc = subprocess.Popen(
        otter_cmd + ["run", app_dir],
        cwd=app_dir,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )

    try:
        ready = wait_for_server(health_url, timeout_secs=12)
        if not ready:
            print("[FAIL] Web server did not start in time!")
            out, err = proc.communicate(timeout=2)
            print("STDOUT:", out)
            print("STDERR:", err)
            sys.exit(1)

        print("  [PASS] Web server online & listening on port 18092.")

        # Test 1: GET /api/health
        status, health = http_get(f"{base_url}/api/health")
        assert status == 200, f"Expected 200 from /api/health, got {status}"
        assert health.get("status") == "healthy", "Health status was not 'healthy'"
        assert "Otter 1.0.1" in health.get("engine", ""), "Engine version mismatch"
        print("  [PASS] GET /api/health: Verified healthy status and engine identity.")

        # Test 2: GET /api/services
        status, svcs = http_get(f"{base_url}/api/services")
        assert status == 200, f"Expected 200 from /api/services, got {status}"
        assert isinstance(svcs, list), "Expected list of services"
        assert len(svcs) >= 4, f"Expected at least 4 default services, got {len(svcs)}"
        names = [s.get("name") for s in svcs]
        assert "Auth Gateway" in names, "Missing Auth Gateway"
        assert "Database Primary" in names, "Missing Database Primary"
        print(f"  [PASS] GET /api/services: Retrieved {len(svcs)} registered microservices.")

        # Test 3: POST /api/services (Register new service)
        new_service_payload = {
            "name": "Telemetry Ingestion Pipeline",
            "port": 9999,
            "category": "edge",
            "targetUptime": 99.95
        }
        status, created = http_post(f"{base_url}/api/services", new_service_payload)
        assert status == 201, f"Expected 201 from POST /api/services, got {status}"
        assert created.get("name") == "Telemetry Ingestion Pipeline", "Created service name mismatch"
        assert created.get("slug") == "telemetry-ingestion-pipeline", "Slug generation failed"
        assert created.get("port") == 9999, "Port mismatch"
        print("  [PASS] POST /api/services: Registered new service with auto-slugified ID.")

        # Test 4: GET /api/metrics
        status, metrics = http_get(f"{base_url}/api/metrics")
        assert status == 200, f"Expected 200 from /api/metrics, got {status}"
        assert metrics.get("totalServices") == len(svcs) + 1, "Metrics totalServices count mismatch"
        assert metrics.get("averageLatencyMs") > 0, "Metrics averageLatencyMs should be > 0"
        assert metrics.get("overallUptime") >= 99.0, "Metrics overallUptime should be >= 99.0"
        print(f"  [PASS] GET /api/metrics: Total: {metrics.get('totalServices')}, Avg Latency: {metrics.get('averageLatencyMs')}ms, Uptime: {metrics.get('overallUptime'):.2f}%.")

        # Test 5: POST /api/services/ping
        ping_payload = {"slug": "telemetry-ingestion-pipeline"}
        status, ping_res = http_post(f"{base_url}/api/services/ping", ping_payload)
        assert status == 200, f"Expected 200 from /api/services/ping, got {status}"
        assert ping_res.get("success") is True, "Ping probe failed"
        print("  [PASS] POST /api/services/ping: Health probe executed and latency recorded.")

        # Test 6: GET / (Static Dashboard HTML)
        status, html = http_get_raw(f"{base_url}/")
        assert status == 200, f"Expected 200 from GET /, got {status}"
        assert "<title>Otter Pulse" in html, "Dashboard HTML title mismatch"
        assert "dashboard-shell" in html, "Dashboard shell element missing"
        print("  [PASS] GET /: Static dashboard frontend served successfully.")

    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc.kill()

    # -------------------------------------------------------------
    # Stage 2: Server Restart & Telemetry Persistence Verification
    # -------------------------------------------------------------
    print("Stage 2: Restarting server in fresh process to test disk recovery...")
    proc2 = subprocess.Popen(
        otter_cmd + ["run", app_dir],
        cwd=app_dir,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )

    try:
        ready = wait_for_server(health_url, timeout_secs=10)
        assert ready, "Server restart failed to listen on port 18092"

        status, restored_svcs = http_get(f"{base_url}/api/services")
        assert status == 200
        restored_slugs = [s.get("slug") for s in restored_svcs]
        assert "telemetry-ingestion-pipeline" in restored_slugs, "Dynamically registered service missing after server restart!"
        print("  [PASS] Multi-process persistence verified: dynamically registered services restored from disk.")

    finally:
        proc2.terminate()
        try:
            proc2.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc2.kill()

        if os.path.exists(store_path):
            os.remove(store_path)

    print("=" * 70)
    print(" ALL SERVICE DASHBOARD APPLICATION TESTS PASSED (100%)")
    print("=" * 70)

if __name__ == "__main__":
    main()
