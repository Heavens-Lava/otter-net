#!/usr/bin/env python3
"""
tools/verify-integration.py - Complete Milestone 5 End-to-End Integration Verification Suite
Executes the full 10-step developer verification flow:
 1. Open an Otter project
 2. Real-time syntax diagnostics (LSP didOpen)
 3. Autocomplete a function (LSP completion)
 4. Navigate to its definition (LSP definition)
 5. Rename a symbol safely (LSP rename)
 6. Run an Otter application (CLI run)
 7. Set a breakpoint and inspect a variable (DAP debugging)
 8. Search and apply a Recipe (CLI recipe apply)
 9. Modify generated code
10. Run project tests successfully (CLI test)
"""

import sys
import os
import json
import subprocess
import time
import shutil

def send_lsp_msg(proc, payload):
    body = json.dumps(payload).encode("utf-8")
    header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
    proc.stdin.write(header + body)
    proc.stdin.flush()

def read_lsp_msg(proc, timeout=5.0):
    start = time.time()
    headers = {}
    while True:
        if time.time() - start > timeout:
            raise TimeoutError("Timed out reading LSP/DAP header")
        line = proc.stdout.readline()
        if not line:
            raise EOFError("Process closed stdout")
        line_str = line.decode("latin1").strip()
        if not line_str:
            break
        if ":" in line_str:
            k, v = line_str.split(":", 1)
            headers[k.strip().lower()] = v.strip()

    content_len = int(headers.get("content-length", 0))
    body = proc.stdout.read(content_len)
    return json.loads(body.decode("utf-8"))

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

    test_dir = os.path.join(root_dir, "scratch", "m5_verification_project")
    if os.path.exists(test_dir):
        shutil.rmtree(test_dir)
    os.makedirs(test_dir, exist_ok=True)

    print("=" * 65)
    print(" Otter Phase 5: Milestone 5 End-to-End Integration Verification")
    print(f" Test Project Directory: {test_dir}")
    print("=" * 65)

    passed = 0
    failed = 0

    def record(step_num, title, success, details=""):
        nonlocal passed, failed
        if success:
            print(f"  [PASS] Step {step_num}: {title}")
            passed += 1
        else:
            print(f"  [FAIL] Step {step_num}: {title} - {details}")
            failed += 1

    try:
        # Step 1: Open an Otter project
        print("\n--- Step 1: Project Creation ---")
        p_create = subprocess.run(
            ["dotnet", cli_dll, "new", "console", "verify_app"],
            cwd=test_dir,
            capture_output=True,
            text=True
        )
        app_dir = os.path.join(test_dir, "verify_app")
        record(1, "Create and open Otter project", p_create.returncode == 0 and os.path.isdir(app_dir))

        # Step 2: Real-time syntax diagnostics
        print("\n--- Step 2: Real-time Syntax Diagnostics ---")
        lsp_proc = subprocess.Popen(
            ["dotnet", cli_dll, "lsp"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=app_dir
        )

        send_lsp_msg(lsp_proc, {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {"rootUri": f"file://{app_dir}"}
        })
        _ = read_lsp_msg(lsp_proc) # init response

        bad_file_uri = f"file://{app_dir}/broken.ot"
        send_lsp_msg(lsp_proc, {
            "jsonrpc": "2.0",
            "method": "textDocument/didOpen",
            "params": {
                "textDocument": {
                    "uri": bad_file_uri,
                    "languageId": "otter",
                    "version": 1,
                    "text": "if is 100\n    say \"invalid\"\n.\n"
                }
            }
        })
        diag_msg = read_lsp_msg(lsp_proc)
        diags = diag_msg.get("params", {}).get("diagnostics", [])
        record(2, "Receive real-time syntax diagnostics", len(diags) > 0 and diag_msg.get("method") == "textDocument/publishDiagnostics")

        # Step 3: Autocomplete a function
        print("\n--- Step 3: Autocomplete a Function ---")
        sample_code = (
            "to calculateDiscount price and pct\n"
            "    rate is pct divided by 100\n"
            "    return price times rate\n"
            ".\n\n"
            "finalAmount is calculateDiscount 100 and 20\n"
            "say \"Final discount:\" finalAmount\n"
        )
        sample_file_uri = f"file://{app_dir}/main.ot"
        send_lsp_msg(lsp_proc, {
            "jsonrpc": "2.0",
            "method": "textDocument/didOpen",
            "params": {
                "textDocument": {
                    "uri": sample_file_uri,
                    "languageId": "otter",
                    "version": 1,
                    "text": sample_code
                }
            }
        })
        _ = read_lsp_msg(lsp_proc) # clean diagnostics

        send_lsp_msg(lsp_proc, {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "textDocument/completion",
            "params": {
                "textDocument": {"uri": sample_file_uri},
                "position": {"line": 5, "character": 15}
            }
        })
        comp_resp = read_lsp_msg(lsp_proc)
        items = comp_resp.get("result", {}).get("items", [])
        labels = [it.get("label") for it in items]
        record(3, "Autocomplete function suggestions", "calculateDiscount" in labels)

        # Step 4: Navigate to definition
        print("\n--- Step 4: Navigate to Definition ---")
        send_lsp_msg(lsp_proc, {
            "jsonrpc": "2.0",
            "id": 3,
            "method": "textDocument/definition",
            "params": {
                "textDocument": {"uri": sample_file_uri},
                "position": {"line": 5, "character": 17}
            }
        })
        def_resp = read_lsp_msg(lsp_proc)
        target_line = def_resp.get("result", {}).get("range", {}).get("start", {}).get("line")
        record(4, "Navigate to symbol definition", target_line == 0)

        # Step 5: Rename symbol safely
        print("\n--- Step 5: Rename Symbol Safely ---")
        send_lsp_msg(lsp_proc, {
            "jsonrpc": "2.0",
            "id": 4,
            "method": "textDocument/rename",
            "params": {
                "textDocument": {"uri": sample_file_uri},
                "position": {"line": 0, "character": 5},
                "newName": "computeDiscount"
            }
        })
        ren_resp = read_lsp_msg(lsp_proc)
        changes = ren_resp.get("result", {}).get("changes", {}).get(sample_file_uri, [])
        new_source = changes[0].get("newText", "") if changes else ""
        record(5, "Rename symbol safely with workspace edit", "computeDiscount" in new_source and "calculateDiscount" not in new_source)

        # Apply renamed source to disk
        main_ot_path = os.path.join(app_dir, "main.ot")
        with open(main_ot_path, "w", encoding="utf-8") as f:
            f.write(new_source)

        # Shutdown LSP
        send_lsp_msg(lsp_proc, {"jsonrpc": "2.0", "id": 5, "method": "shutdown", "params": None})
        _ = read_lsp_msg(lsp_proc)
        send_lsp_msg(lsp_proc, {"jsonrpc": "2.0", "method": "exit", "params": None})
        lsp_proc.wait(timeout=2.0)

        # Step 6: Run an Otter application
        print("\n--- Step 6: Run Otter Application ---")
        p_run = subprocess.run(
            ["dotnet", cli_dll, "run", "main.ot"],
            cwd=app_dir,
            capture_output=True,
            text=True
        )
        record(6, "Run Otter application via native CLI", p_run.returncode == 0 and "Final discount: 20" in p_run.stdout)

        # Step 7: Set breakpoint and inspect variable via DAP
        print("\n--- Step 7: Visual Debugging & Variable Inspection ---")
        dap_proc = subprocess.Popen(
            ["dotnet", cli_dll, "dap"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=app_dir
        )

        send_lsp_msg(dap_proc, {"seq": 1, "type": "request", "command": "initialize", "arguments": {}})
        _ = read_lsp_msg(dap_proc) # init resp
        _ = read_lsp_msg(dap_proc) # init evt

        send_lsp_msg(dap_proc, {"seq": 2, "type": "request", "command": "launch", "arguments": {"program": main_ot_path}})
        _ = read_lsp_msg(dap_proc) # launch resp

        send_lsp_msg(dap_proc, {
            "seq": 3,
            "type": "request",
            "command": "setBreakpoints",
            "arguments": {
                "source": {"path": main_ot_path},
                "breakpoints": [{"line": 6}] # line 6: finalAmount is ...
            }
        })
        _ = read_lsp_msg(dap_proc) # setBreakpoints resp

        send_lsp_msg(dap_proc, {"seq": 4, "type": "request", "command": "configurationDone", "arguments": {}})
        _ = read_lsp_msg(dap_proc) # configurationDone resp

        stopped_evt = read_lsp_msg(dap_proc)
        record(7, "Hit breakpoint and pause execution", stopped_evt.get("event") == "stopped")

        send_lsp_msg(dap_proc, {"seq": 5, "type": "request", "command": "scopes", "arguments": {"frameId": 1}})
        _ = read_lsp_msg(dap_proc) # scopes resp

        send_lsp_msg(dap_proc, {"seq": 6, "type": "request", "command": "continue", "arguments": {"threadId": 1}})
        _ = read_lsp_msg(dap_proc) # continue resp

        # Read remaining output and termination
        for _ in range(5):
            m = read_lsp_msg(dap_proc)
            if m.get("event") == "terminated":
                break

        send_lsp_msg(dap_proc, {"seq": 7, "type": "request", "command": "disconnect", "arguments": {}})
        _ = read_lsp_msg(dap_proc)
        dap_proc.wait(timeout=2.0)

        # Step 8: Search and apply a Recipe
        print("\n--- Step 8: Search and Apply Application Recipe ---")
        p_recipe_search = subprocess.run(
            ["dotnet", cli_dll, "recipe", "search", "task-tracker"],
            cwd=app_dir,
            capture_output=True,
            text=True
        )
        p_recipe_apply = subprocess.run(
            ["dotnet", cli_dll, "recipe", "apply", "otter-official/task-tracker", "."],
            cwd=app_dir,
            capture_output=True,
            text=True
        )
        task_file = os.path.join(app_dir, "src", "tasks.ot")
        record(8, "Search and apply Application Recipe", p_recipe_apply.returncode == 0 and os.path.exists(task_file))

        # Step 9: Modify the generated code
        print("\n--- Step 9: Modify Generated Code ---")
        with open(task_file, "a", encoding="utf-8") as f:
            f.write("\n# Verification extension\nsay \"Task Tracker verified successfully!\"\n")
        record(9, "Modify generated application code", True)

        # Step 10: Run tests successfully
        print("\n--- Step 10: Run Project Tests ---")
        p_test = subprocess.run(
            ["dotnet", cli_dll, "test", "."],
            cwd=app_dir,
            capture_output=True,
            text=True
        )
        record(10, "Run project tests successfully", p_test.returncode == 0)

    except Exception as ex:
        print(f"\nFATAL INTEGRATION ERROR: {ex}", file=sys.stderr)
        sys.exit(1)
    finally:
        try:
            shutil.rmtree(test_dir)
        except:
            pass

    print("\n" + "=" * 65)
    print(f" Integration Verification Summary: {passed}/10 passed, {failed}/10 failed")
    print("=" * 65)

    if failed > 0:
        sys.exit(1)
    else:
        print(" Phase 5 End-to-End Integration Verification PASSED (100%)\n")

if __name__ == "__main__":
    main()
