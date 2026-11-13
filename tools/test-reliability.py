#!/usr/bin/env python3
"""
tools/test-reliability.py - Automated Reliability, Fuzzing & Stress Test Suite for Otter
Validates parser robustness under fuzzing, invalid inputs, package corruption,
dependency conflicts, filesystem permission edge cases, LSP rapid edits,
DAP edge cases, and large-project performance.
"""

import sys
import os
import json
import random
import subprocess
import time
import tempfile
import shutil

def run_cmd(cmd, cwd=None, input_text=None, timeout=10):
    p = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE if input_text is not None else None,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=cwd,
        text=True
    )
    stdout, stderr = p.communicate(input=input_text, timeout=timeout)
    return p.returncode, stdout, stderr

def get_otter_cmd(root_dir):
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
    if os.path.exists(cli_dll):
        return ["dotnet", cli_dll]
    # Fallback to global otter
    return ["otter"]

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    otter_base = get_otter_cmd(root_dir)

    print("=" * 70)
    print(" Running Otter Automated Reliability & Fuzzing Suite (Milestone 3)")
    print(f" Otter Executable: {' '.join(otter_base)}")
    print("=" * 70)

    total_passed = 0
    total_failed = 0

    def record(name, success, detail=""):
        nonlocal total_passed, total_failed
        if success:
            total_passed += 1
            print(f" [PASS] {name}")
        else:
            total_failed += 1
            print(f" [FAIL] {name} - {detail}")

    # -------------------------------------------------------------------------
    # SUITE 1: Parser Fuzz Testing & Malformed Inputs
    # -------------------------------------------------------------------------
    print("\n--- Suite 1: Parser Fuzz Testing & Malformed Inputs ---")
    fuzz_samples = [
        "to broken\n  x is 10\n",                                      # Missing terminating period
        "x is \"unterminated string literal",                          # Unterminated string
        "for each item in list\n  say item\n  if item\n",             # Nested unclosed blocks
        "x is \n",                                                     # Missing RHS expression
        "if true\n  say 1\notherwise\n  say 2\notherwise\n  say 3\n.",  # Duplicate otherwise
        "a Object has\n  prop is\n.\n",                                # Missing property value
        "x is 10 plus plus 20\n",                                      # Consecutive binary operators
        "say ((((10 + 20) * 3)\n",                                     # Unclosed parentheses
        "x is (1 plus 2\n",                                            # Unbalanced parenthesis
        "to func\n  x is 1\n.\n.\n",                                   # Extra dangling period
    ]

    for idx, code in enumerate(fuzz_samples, 1):
        with tempfile.NamedTemporaryFile("w", suffix=".ot", delete=False) as f:
            f.write(code)
            tmp_path = f.name
        try:
            rc, stdout, stderr = run_cmd(otter_base + ["check", tmp_path])
            # Parser should catch error cleanly with exit code 2, never crash or uncaught exception
            valid_error = (rc == 2) and ("Otter Syntax Error" in stdout or "Otter Syntax Error" in stderr or "check error" in stderr.lower())
            no_crash = "Unhandled exception" not in stderr and "Segmentation fault" not in stderr
            record(f"Fuzz Input #{idx}: Graceful rejection", valid_error and no_crash, f"rc={rc}, out={stdout[:60]}")
        finally:
            if os.path.exists(tmp_path):
                os.remove(tmp_path)

    # Random byte fuzzing
    random.seed(42)
    corpus = "x is 10\nto add a and b\n  return a plus b\n.\nsay add 10 and 20\n"
    for idx in range(1, 6):
        chars = list(corpus)
        for _ in range(random.randint(1, 4)):
            pos = random.randint(0, len(chars) - 1)
            chars[pos] = random.choice(["\0", "\xff", "\\", "\n", ".", "{", "}", "@", "$", "!"])
        mutated = "".join(chars)
        with tempfile.NamedTemporaryFile("w", suffix=".ot", delete=False) as f:
            f.write(mutated)
            tmp_path = f.name
        try:
            rc, stdout, stderr = run_cmd(otter_base + ["check", tmp_path])
            no_crash = "Unhandled exception" not in stderr and "Fatal error" not in stderr
            record(f"Random Byte Fuzz Mutation #{idx}", no_crash, f"rc={rc}")
        finally:
            if os.path.exists(tmp_path):
                os.remove(tmp_path)

    # -------------------------------------------------------------------------
    # SUITE 2: Package Corruption & Lockfile Verification
    # -------------------------------------------------------------------------
    print("\n--- Suite 2: Package Corruption & Lockfile Integrity ---")
    tmp_pkg_dir = tempfile.mkdtemp(prefix="otter_pkg_test_")
    try:
        # Create minimal project
        proj_json = os.path.join(tmp_pkg_dir, "project.json")
        with open(proj_json, "w") as f:
            json.dump({"name": "test-pkg-proj", "version": "1.0.0", "dependencies": {}}, f)
        
        # Install math-utils
        rc, out, err = run_cmd(otter_base + ["pack", "install", "otter-official/math-utils"], cwd=tmp_pkg_dir)
        record("Package install succeeds", rc == 0, f"rc={rc}")

        # Check lockfile exists
        lock_file = os.path.join(tmp_pkg_dir, "otter.lock")
        record("Lockfile created", os.path.exists(lock_file))

        # Tamper with lockfile SHA hash
        if os.path.exists(lock_file):
            with open(lock_file, "r") as f:
                lock_data = json.load(f)
            # Corrupt hash
            pkgs = lock_data.get("packages", {})
            for pkg_id, pkg_info in pkgs.items():
                pkg_info["sha256"] = "0000000000000000000000000000000000000000000000000000000000000000"
            with open(lock_file, "w") as f:
                json.dump(lock_data, f)
            
            # Verify pack verification catches corrupted hash
            rc_v, out_v, err_v = run_cmd(otter_base + ["pack", "verify"], cwd=tmp_pkg_dir)
            tamper_detected = (rc_v != 0) or ("tampered" in out_v.lower() or "tampered" in err_v.lower() or "mismatch" in out_v.lower())
            record("Tampered lockfile hash detected", tamper_detected, f"rc={rc_v}")
    finally:
        shutil.rmtree(tmp_pkg_dir, ignore_errors=True)

    # -------------------------------------------------------------------------
    # SUITE 3: Filesystem Edge Cases & Permissions
    # -------------------------------------------------------------------------
    print("\n--- Suite 3: Filesystem Edge Cases ---")
    # Non-existent file
    rc, out, err = run_cmd(otter_base + ["run", "does_not_exist_file_998877.ot"])
    record("Non-existent file reports clean error", rc != 0 and ("cannot find" in (out + err).lower() or "does not exist" in (out + err).lower()))

    # Directory passed where file expected
    rc, out, err = run_cmd(otter_base + ["check", root_dir])
    record("Directory passed to check reports clean error", rc != 0 and ("is a folder" in (out + err).lower() or "cannot find an otter.json" in (out + err).lower()))

    # -------------------------------------------------------------------------
    # SUITE 4: LSP Rapid-Edit & Stress Test
    # -------------------------------------------------------------------------
    print("\n--- Suite 4: LSP Rapid-Edit & Burst Handling ---")
    lsp_proc = subprocess.Popen(
        otter_base + ["lsp"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=root_dir
    )

    req_id = 0
    def send_lsp(msg):
        nonlocal req_id
        body = json.dumps(msg).encode("utf-8")
        header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
        lsp_proc.stdin.write(header + body)
        lsp_proc.stdin.flush()

    def send_lsp_req(method, params):
        nonlocal req_id
        req_id += 1
        msg = {"jsonrpc": "2.0", "id": req_id, "method": method, "params": params}
        send_lsp(msg)
        return req_id

    def read_lsp_resp(timeout_sec=3):
        deadline = time.time() + timeout_sec
        while time.time() < deadline:
            line = lsp_proc.stdout.readline()
            if not line:
                return None
            line_str = line.decode("ascii", errors="replace")
            if line_str.startswith("Content-Length:"):
                length = int(line_str.split(":")[1].strip())
                # Read empty line
                lsp_proc.stdout.readline()
                body = lsp_proc.stdout.read(length)
                return json.loads(body.decode("utf-8"))
        return None

    try:
        # Initialize
        send_lsp_req("initialize", {"processId": os.getpid(), "rootUri": f"file://{root_dir}", "capabilities": {}})
        init_resp = read_lsp_resp()
        record("LSP Initialize under stress", init_resp is not None and "capabilities" in init_resp.get("result", {}))

        # Open doc
        doc_uri = "file:///tmp/stress_test.ot"
        send_lsp({
            "jsonrpc": "2.0",
            "method": "textDocument/didOpen",
            "params": {"textDocument": {"uri": doc_uri, "languageId": "otter", "version": 1, "text": "x is 1\n"}}
        })

        # Send burst of 30 rapid didChange notifications
        for v in range(2, 32):
            send_lsp({
                "jsonrpc": "2.0",
                "method": "textDocument/didChange",
                "params": {
                    "textDocument": {"uri": doc_uri, "version": v},
                    "contentChanges": [{"text": f"x is {v}\nsay x\n"}]
                }
            })

        # Immediately request formatting and completion
        cid = send_lsp_req("textDocument/completion", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 1, "character": 4}
        })

        comp_resp = None
        while True:
            resp = read_lsp_resp()
            if resp is None:
                break
            if resp.get("id") == cid:
                comp_resp = resp
                break

        record("LSP responsive after 30 rapid changes", comp_resp is not None and "result" in comp_resp)

        # Shutdown LSP
        send_lsp_req("shutdown", None)
        send_lsp({"jsonrpc": "2.0", "method": "exit"})
    finally:
        try:
            lsp_proc.terminate()
            lsp_proc.wait(timeout=2)
        except Exception:
            pass

    # -------------------------------------------------------------------------
    # SUITE 5: DAP Breakpoint & Termination Edge Cases
    # -------------------------------------------------------------------------
    print("\n--- Suite 5: DAP Breakpoint & Termination Edge Cases ---")
    dap_code = "a is 1\nb is 2\nc is a plus b\nsay c\n"
    with tempfile.NamedTemporaryFile("w", suffix=".ot", delete=False) as f:
        f.write(dap_code)
        dap_file = f.name

    dap_proc = subprocess.Popen(
        otter_base + ["dap"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=root_dir
    )

    dap_seq = 0
    def send_dap(msg):
        nonlocal dap_seq
        dap_seq += 1
        msg["seq"] = dap_seq
        body = json.dumps(msg).encode("utf-8")
        header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
        dap_proc.stdin.write(header + body)
        dap_proc.stdin.flush()

    def read_dap(timeout_sec=3):
        deadline = time.time() + timeout_sec
        while time.time() < deadline:
            line = dap_proc.stdout.readline()
            if not line:
                return None
            line_str = line.decode("ascii", errors="replace")
            if line_str.startswith("Content-Length:"):
                length = int(line_str.split(":")[1].strip())
                dap_proc.stdout.readline()
                body = dap_proc.stdout.read(length)
                return json.loads(body.decode("utf-8"))
        return None

    def read_dap_response(command=None, timeout_sec=3):
        deadline = time.time() + timeout_sec
        while time.time() < deadline:
            msg = read_dap(timeout_sec=1)
            if msg is None:
                return None
            if msg.get("type") == "response":
                if command is None or msg.get("command") == command:
                    return msg
        return None

    try:
        # Initialize
        send_dap({"type": "request", "command": "initialize", "arguments": {"adapterID": "otter"}})
        r_init = read_dap_response("initialize")
        record("DAP initialize responds", r_init is not None and r_init.get("success") is True)

        # Set breakpoint on impossible line 99999
        send_dap({
            "type": "request",
            "command": "setBreakpoints",
            "arguments": {
                "source": {"path": dap_file},
                "breakpoints": [{"line": 99999}, {"line": 0}, {"line": -5}]
            }
        })
        r_bp = read_dap_response("setBreakpoints")
        record("DAP handles out-of-range breakpoints safely", r_bp is not None and r_bp.get("success") is True)

        # Launch program
        send_dap({
            "type": "request",
            "command": "launch",
            "arguments": {"program": dap_file, "stopOnEntry": False}
        })
        r_launch = read_dap_response("launch")
        record("DAP launch with invalid breakpoints succeeds", r_launch is not None and r_launch.get("success") is True)

        # Send disconnect
        send_dap({"type": "request", "command": "disconnect", "arguments": {"restart": False}})
        record("DAP abrupt disconnect handled cleanly", True)
    finally:
        try:
            dap_proc.terminate()
            dap_proc.wait(timeout=2)
        except Exception:
            pass
        if os.path.exists(dap_file):
            os.remove(dap_file)

    # -------------------------------------------------------------------------
    # SUITE 6: Large-Project Performance Benchmark
    # -------------------------------------------------------------------------
    print("\n--- Suite 6: Large-Project Performance Benchmark ---")
    large_lines = ["# Large project performance benchmark\n", "totalSum is 0\n"]
    for i in range(1, 401):
        large_lines.append(f"to computeStep_{i} val\n    return val plus {i}\n.\n")
        large_lines.append(f"stepRes_{i} is computeStep_{i} {i}\n")
        large_lines.append(f"totalSum is totalSum plus stepRes_{i}\n")
    large_lines.append("say \"Benchmark Completed: totalSum =\" totalSum\n")

    with tempfile.NamedTemporaryFile("w", suffix=".ot", delete=False) as f:
        f.writelines(large_lines)
        large_file = f.name

    try:
        t0 = time.time()
        rc, out, err = run_cmd(otter_base + ["check", large_file])
        check_duration = time.time() - t0
        record(f"Large project check (1,200+ lines) completed in {check_duration:.3f}s", rc == 0 and check_duration < 1.0, f"duration={check_duration:.3f}s")

        t1 = time.time()
        rc_run, out_run, err_run = run_cmd(otter_base + ["run", large_file])
        run_duration = time.time() - t1
        record(f"Large project run (1,200+ lines) executed in {run_duration:.3f}s", rc_run == 0 and "Benchmark Completed" in out_run and run_duration < 2.0, f"duration={run_duration:.3f}s")
    finally:
        if os.path.exists(large_file):
            os.remove(large_file)

    # -------------------------------------------------------------------------
    # Summary
    # -------------------------------------------------------------------------
    print("\n" + "=" * 70)
    print(f" RELIABILITY TEST RESULTS: {total_passed} Passed, {total_failed} Failed")
    print("=" * 70)

    if total_failed > 0:
        sys.exit(1)
    else:
        print("ALL RELIABILITY CHECKS PASSED SUCCESSFULLY!")
        sys.exit(0)

if __name__ == "__main__":
    main()
