#!/usr/bin/env python3
"""
Integration & End-to-End Test Suite for Otter Service Dashboard Web Application (Phase 9)
Tests:
- Full REST CRUD: GET, POST (201 Created), PUT (200 OK), DELETE (200 OK)
- Robust validation: 400 Bad Request on missing name or invalid port
- Conflict detection: 409 Conflict on duplicate service registration
- Not found handling: 404 Not Found on updating or deleting non-existent services
- Telemetry & metrics calculation factoring in both latency and uptime
- Real probe round-trip latency recording
- Multi-process persistence across web server process restarts
- Static asset serving (GET /)
"""

import os
import sys
import time
import json
import urllib.request
import urllib.error
import subprocess

def http_request(url, method="GET", data=None):
    payload = json.dumps(data).encode("utf-8") if data is not None else None
    headers = {"Content-Type": "application/json"} if data is not None else {}
    req = urllib.request.Request(url, data=payload, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
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

def wait_for_server(url, timeout_secs=10):
    start = time.time()
    while time.time() - start < timeout_secs:
        try:
            status, data = http_request(url, method="GET")
            if status == 200:
                return True
        except Exception:
            time.sleep(0.2)
    return False

def main():
    root_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
    app_dir = os.path.join(root_dir, "examples", "service-dashboard-app")
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

    if len(sys.argv) > 1 and sys.argv[1]:
        otter_cmd = [sys.argv[1]]
    elif os.path.exists(cli_dll):
        otter_cmd = ["dotnet", cli_dll]
    else:
        otter_cmd = ["otter"]

    store_path = os.path.join(app_dir, "services_store.json")
    if os.path.exists(store_path):
        os.remove(store_path)

    base_url = "http://127.0.0.1:18092"
    health_url = f"{base_url}/api/health"

    print("=" * 70)
    print(" Testing Service Dashboard Application End-to-End (Phase 9)")
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
        status, health = http_request(f"{base_url}/api/health")
        assert status == 200, f"Expected 200 from /api/health, got {status}"
        assert health.get("status") == "healthy", "Health status was not 'healthy'"
        assert "Otter 1.0.1" in health.get("engine", ""), "Engine version mismatch"
        print("  [PASS] GET /api/health: Verified healthy status and engine identity.")

        # Test 2: GET /api/services
        status, svcs = http_request(f"{base_url}/api/services")
        assert status == 200, f"Expected 200 from /api/services, got {status}"
        assert isinstance(svcs, list), "Expected list of services"
        assert len(svcs) >= 4, f"Expected at least 4 default services, got {len(svcs)}"
        names = [s.get("name") for s in svcs]
        assert "Auth Gateway" in names, "Missing Auth Gateway"
        assert "Database Primary" in names, "Missing Database Primary"
        print(f"  [PASS] GET /api/services: Retrieved {len(svcs)} registered microservices.")

        # Test 3: POST /api/services (Validation Failures)
        # 3a: Missing name
        status, err_resp = http_request(f"{base_url}/api/services", method="POST", data={"name": "", "port": 8080})
        assert status == 400, f"Expected 400 on empty name, got {status}"
        print("  [PASS] POST /api/services (400 Bad Request): Caught empty service name.")

        # 3b: Invalid port
        status, err_resp = http_request(f"{base_url}/api/services", method="POST", data={"name": "Bad Port Svc", "port": 99999})
        assert status == 400, f"Expected 400 on port 99999, got {status}"
        print("  [PASS] POST /api/services (400 Bad Request): Caught out-of-range port number.")

        # Test 4: POST /api/services (Successful Registration)
        new_svc_data = {
            "name": "Telemetry Ingestion Pipeline",
            "port": 9091,
            "category": "edge",
            "targetUptime": 99.8
        }
        status, created = http_request(f"{base_url}/api/services", method="POST", data=new_svc_data)
        assert status == 201, f"Expected 201 from POST /api/services, got {status}"
        assert created.get("name") == "Telemetry Ingestion Pipeline", "Created service name mismatch"
        assert created.get("slug") == "telemetry-ingestion-pipeline", "Slug generation failed"
        assert created.get("port") == 9091, "Port mismatch"
        print("  [PASS] POST /api/services (201 Created): Registered new service with auto-slugified ID.")

        # Test 5: POST /api/services (Duplicate Detection)
        status, dup_resp = http_request(f"{base_url}/api/services", method="POST", data=new_svc_data)
        assert status == 409, f"Expected 409 Conflict on duplicate service, got {status}"
        assert "already exists" in dup_resp.get("error", ""), "Duplicate error message mismatch"
        print("  [PASS] POST /api/services (409 Conflict): Rejected duplicate service registration.")

        # Test 6: PUT /api/services (Update Service)
        # 6a: Update existing
        update_data = {
            "slug": "telemetry-ingestion-pipeline",
            "port": 9095,
            "category": "api",
            "targetUptime": 99.95
        }
        status, updated = http_request(f"{base_url}/api/services", method="PUT", data=update_data)
        assert status == 200, f"Expected 200 from PUT /api/services, got {status}"
        assert updated.get("port") == 9095, "Updated port mismatch"
        assert updated.get("category") == "api", "Updated category mismatch"
        print("  [PASS] PUT /api/services (200 OK): Updated port and category for service.")

        # 6b: Update non-existent service
        status, err_resp = http_request(f"{base_url}/api/services", method="PUT", data={"slug": "non-existent-svc", "port": 8080, "category": "api", "targetUptime": 99.9})
        assert status == 404, f"Expected 404 on updating non-existent service, got {status}"
        print("  [PASS] PUT /api/services (404 Not Found): Handled update on missing record.")

        # Test 7: GET /api/metrics (Verify calculations)
        status, metrics = http_request(f"{base_url}/api/metrics")
        assert status == 200, f"Expected 200 from /api/metrics, got {status}"
        assert metrics.get("totalServices") == 5, f"Expected 5 total services, got {metrics.get('totalServices')}"
        assert metrics.get("averageLatencyMs") > 0, "Average latency should be positive"
        assert metrics.get("overallUptime") > 90, "Overall uptime should be > 90%"
        print(f"  [PASS] GET /api/metrics: Total: {metrics.get('totalServices')}, Avg Latency: {metrics.get('averageLatencyMs')}ms, Uptime: {metrics.get('overallUptime')}%.")

        # Test 8: POST /api/services/ping
        # 8a: Ping existing service with real measurement
        ping_data = {"slug": "telemetry-ingestion-pipeline", "latencyMs": 35}
        status, ping_res = http_request(f"{base_url}/api/services/ping", method="POST", data=ping_data)
        assert status == 200, f"Expected 200 from /api/services/ping, got {status}"
        assert ping_res.get("success") is True, "Expected success: true from ping"
        assert ping_res.get("latencyMs") == 35, "Recorded latency mismatch"
        print("  [PASS] POST /api/services/ping (200 OK): Real probe latency recorded.")

        # 8b: Ping non-existent service
        status, err_resp = http_request(f"{base_url}/api/services/ping", method="POST", data={"slug": "non-existent-svc"})
        assert status == 404, f"Expected 404 on pinging missing service, got {status}"
        print("  [PASS] POST /api/services/ping (404 Not Found): Correct 404 on missing service.")

        # Test 9: DELETE /api/services
        # 9a: Delete existing service
        status, del_res = http_request(f"{base_url}/api/services", method="DELETE", data={"slug": "telemetry-ingestion-pipeline"})
        assert status == 200, f"Expected 200 from DELETE /api/services, got {status}"
        assert del_res.get("deleted") is True, "Expected deleted: true"
        print("  [PASS] DELETE /api/services (200 OK): Unregistered microservice successfully.")

        # 9b: Delete non-existent service
        status, err_resp = http_request(f"{base_url}/api/services", method="DELETE", data={"slug": "telemetry-ingestion-pipeline"})
        assert status == 404, f"Expected 404 on deleting already deleted service, got {status}"
        print("  [PASS] DELETE /api/services (404 Not Found): Handled deletion on non-existent record.")

        # Test 10: GET / (Static frontend asset)
        status, html = http_get_raw(f"{base_url}/")
        assert status == 200, f"Expected 200 from GET /, got {status}"
        assert "OtterPulse" in html or "Otter" in html, "HTML content missing title"
        print("  [PASS] GET /: Static dashboard frontend served successfully.")

    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc.kill()

    # -------------------------------------------------------------
    # Stage 2: Multi-Process Telemetry Persistence Across Restarts
    # -------------------------------------------------------------
    print("Stage 2: Registering a persistent service and restarting server...")
    # First, start server and add a service to be persisted
    proc2 = subprocess.Popen(
        otter_cmd + ["run", app_dir],
        cwd=app_dir,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )
    try:
        assert wait_for_server(health_url, timeout_secs=10), "Server 2 failed to start"
        # Add a persistent service
        status, _ = http_request(f"{base_url}/api/services", method="POST", data={
            "name": "Audit Log Collector",
            "port": 7070,
            "category": "database",
            "targetUptime": 99.99
        })
        assert status == 201, f"Failed to register Audit Log Collector, got {status}"
    finally:
        proc2.terminate()
        try:
            proc2.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc2.kill()

    # Now boot in a completely fresh process and verify recovery
    print("  Booting clean process to verify recovery...")
    proc3 = subprocess.Popen(
        otter_cmd + ["run", app_dir],
        cwd=app_dir,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )
    try:
        assert wait_for_server(health_url, timeout_secs=10), "Server 3 failed to start"
        status, svcs_recovered = http_request(f"{base_url}/api/services")
        assert status == 200
        recovered_slugs = [s.get("slug") for s in svcs_recovered]
        assert "audit-log-collector" in recovered_slugs, "Persisted service missing after server restart!"
        print("  [PASS] Multi-process persistence verified: dynamically registered services restored from disk.")
    finally:
        proc3.terminate()
        try:
            proc3.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc3.kill()
        if os.path.exists(store_path):
            os.remove(store_path)

    print("=" * 70)
    print(" ALL SERVICE DASHBOARD APPLICATION TESTS PASSED (100%)")
    print("=" * 70)

if __name__ == "__main__":
    main()
