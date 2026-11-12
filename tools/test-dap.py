#!/usr/bin/env python3
"""
tools/test-dap.py - Automated DAP Protocol Conformance Test Suite for Otter
Validates launch, breakpoints, step, stack frames, variable inspection, output, and termination.
"""

import sys
import os
import json
import subprocess
import time
import tempfile

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

    # Create temporary Otter script to debug
    code = (
        "x is 10\n"
        "y is 20\n"
        "total is x plus y\n"
        "say total\n"
    )
    with tempfile.NamedTemporaryFile("w", suffix=".ot", delete=False) as f:
        f.write(code)
        target_file = f.name

    cmd = ["dotnet", cli_dll, "dap"]
    print("=" * 60)
    print(" Running Otter DAP Protocol Conformance Tests")
    print(f" Target File: {target_file}")
    print("=" * 60)

    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=root_dir
    )

    seq = 0
    passed = 0
    failed = 0

    def send_message(payload):
        body = json.dumps(payload).encode("utf-8")
        header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
        proc.stdin.write(header + body)
        proc.stdin.flush()

    def send_request(command, args=None):
        nonlocal seq
        seq += 1
        msg = {
            "seq": seq,
            "type": "request",
            "command": command
        }
        if args is not None:
            msg["arguments"] = args
        send_message(msg)
        return seq

    def read_message(timeout=5.0):
        start = time.time()
        headers = {}
        while True:
            if time.time() - start > timeout:
                stderr_output = proc.stderr.read().decode('utf-8', errors='replace')
                raise TimeoutError(f"Timed out reading DAP header. Stderr:\n{stderr_output}")
            line = proc.stdout.readline()
            if not line:
                stderr_output = proc.stderr.read().decode('utf-8', errors='replace')
                raise EOFError(f"DAP server closed stdout. Stderr:\n{stderr_output}")
            line_str = line.decode("latin1").strip()
            if not line_str:
                break
            if ":" in line_str:
                k, v = line_str.split(":", 1)
                headers[k.strip().lower()] = v.strip()

        content_len = int(headers.get("content-length", 0))
        if content_len <= 0:
            raise ValueError(f"Invalid Content-Length: {content_len}")

        body = proc.stdout.read(content_len)
        return json.loads(body.decode("utf-8"))

    def assert_test(name, condition, details=""):
        nonlocal passed, failed
        if condition:
            print(f"  [PASS] {name}")
            passed += 1
        else:
            print(f"  [FAIL] {name}: {details}")
            failed += 1

    try:
        # 1. Initialize
        print("\nStep 1: Testing DAP 'initialize'...")
        init_seq = send_request("initialize", {
            "adapterID": "otter",
            "clientID": "test-client"
        })
        init_resp = read_message()
        assert_test("Initialize response success", init_resp.get("success") is True)
        assert_test("supportsConfigurationDoneRequest is true", init_resp.get("body", {}).get("supportsConfigurationDoneRequest") is True)
        
        # Read initialized event
        init_evt = read_message()
        assert_test("Received 'initialized' event", init_evt.get("event") == "initialized")

        # 2. Launch
        print("\nStep 2: Testing DAP 'launch'...")
        launch_seq = send_request("launch", {
            "program": target_file,
            "stopOnEntry": False
        })
        launch_resp = read_message()
        assert_test("Launch response success", launch_resp.get("success") is True)

        # 3. SetBreakpoints (line 3: total is x plus y)
        print("\nStep 3: Testing DAP 'setBreakpoints' on line 3...")
        bp_seq = send_request("setBreakpoints", {
            "source": {"path": target_file},
            "breakpoints": [{"line": 3}]
        })
        bp_resp = read_message()
        assert_test("setBreakpoints response success", bp_resp.get("success") is True)
        bps = bp_resp.get("body", {}).get("breakpoints", [])
        assert_test("Breakpoint verified on line 3", len(bps) > 0 and bps[0].get("verified") is True and bps[0].get("line") == 3)

        # 4. ConfigurationDone
        print("\nStep 4: Testing DAP 'configurationDone'...")
        conf_seq = send_request("configurationDone", {})
        conf_resp = read_message()
        assert_test("configurationDone response success", conf_resp.get("success") is True)

        # 5. Wait for 'stopped' event at breakpoint
        print("\nStep 5: Waiting for execution to pause on breakpoint...")
        stopped_evt = read_message()
        assert_test("Received 'stopped' event", stopped_evt.get("event") == "stopped")
        assert_test("Stop reason is 'breakpoint'", stopped_evt.get("body", {}).get("reason") == "breakpoint")

        # 6. StackTrace
        print("\nStep 6: Testing DAP 'stackTrace'...")
        st_seq = send_request("stackTrace", {"threadId": 1})
        st_resp = read_message()
        assert_test("stackTrace response success", st_resp.get("success") is True)
        frames = st_resp.get("body", {}).get("stackFrames", [])
        assert_test("At least 1 stack frame returned", len(frames) > 0)
        if frames:
            assert_test("Current frame is on line 3", frames[0].get("line") == 3)

        # 7. Scopes & Variables
        print("\nStep 7: Testing DAP 'scopes' and 'variables' inspection...")
        sc_seq = send_request("scopes", {"frameId": 1})
        sc_resp = read_message()
        assert_test("scopes response success", sc_resp.get("success") is True)
        scopes = sc_resp.get("body", {}).get("scopes", [])
        assert_test("Locals scope returned", len(scopes) > 0 and scopes[0].get("name") == "Locals")

        var_seq = send_request("variables", {"variablesReference": 1})
        var_resp = read_message()
        assert_test("variables response success", var_resp.get("success") is True)
        var_list = var_resp.get("body", {}).get("variables", [])
        var_map = {v.get("name"): v.get("value") for v in var_list}
        assert_test("Variable 'x' has value '10'", var_map.get("x") == "10")
        assert_test("Variable 'y' has value '20'", var_map.get("y") == "20")

        # 8. Step Over (next)
        print("\nStep 8: Testing DAP 'next' (step over)...")
        step_seq = send_request("next", {"threadId": 1})
        step_resp = read_message()
        assert_test("next response success", step_resp.get("success") is True)

        step_stopped = read_message()
        assert_test("Received 'stopped' event after step", step_stopped.get("event") == "stopped")
        assert_test("Stop reason is 'step'", step_stopped.get("body", {}).get("reason") == "step")

        # Verify line progressed to line 4
        st_seq2 = send_request("stackTrace", {"threadId": 1})
        st_resp2 = read_message()
        frames2 = st_resp2.get("body", {}).get("stackFrames", [])
        if frames2:
            assert_test("Stepped to line 4", frames2[0].get("line") == 4)

        # Verify 'total' variable is now computed and visible (30)
        var_seq2 = send_request("variables", {"variablesReference": 1})
        var_resp2 = read_message()
        var_list2 = var_resp2.get("body", {}).get("variables", [])
        var_map2 = {v.get("name"): v.get("value") for v in var_list2}
        assert_test("Variable 'total' has computed value '30'", var_map2.get("total") == "30")

        # 9. Continue to completion
        print("\nStep 9: Testing DAP 'continue'...")
        cont_seq = send_request("continue", {"threadId": 1})
        cont_resp = read_message()
        assert_test("continue response success", cont_resp.get("success") is True)

        # Expect output event with "30\n", then terminated and exited events
        got_output = False
        got_terminated = False
        got_exited = False

        for _ in range(5):
            msg = read_message()
            if msg.get("type") == "event":
                evt_name = msg.get("event")
                if evt_name == "output":
                    output_text = msg.get("body", {}).get("output", "")
                    if "30" in output_text:
                        got_output = True
                elif evt_name == "terminated":
                    got_terminated = True
                elif evt_name == "exited":
                    got_exited = True
                    break

        assert_test("Received program output '30'", got_output)
        assert_test("Received 'terminated' event", got_terminated)
        assert_test("Received 'exited' event", got_exited)

        # 10. Disconnect
        print("\nStep 10: Testing DAP 'disconnect'...")
        disc_seq = send_request("disconnect", {})
        disc_resp = read_message()
        assert_test("disconnect response success", disc_resp.get("success") is True)

        proc.wait(timeout=3.0)
        assert_test("DAP process exited cleanly", proc.returncode == 0)

    except Exception as e:
        print(f"\nFATAL DAP TEST ERROR: {e}", file=sys.stderr)
        proc.kill()
        sys.exit(1)
    finally:
        try:
            os.remove(target_file)
        except:
            pass

    print("\n" + "=" * 60)
    print(f" Summary: {passed} passed, {failed} failed")
    print("=" * 60)

    if failed > 0:
        sys.exit(1)
    else:
        print(" Otter Native DAP Protocol Verification PASSED (100%)\n")

if __name__ == "__main__":
    main()
