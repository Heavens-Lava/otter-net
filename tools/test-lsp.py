#!/usr/bin/env python3
"""
tools/test-lsp.py - Automated LSP Protocol Conformance Test Suite for Otter
Validates JSON-RPC framing, lifecycle, diagnostics, completion, hover, definition,
references, rename, formatting, and workspace symbols.
"""

import sys
import os
import json
import subprocess
import time

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    
    # Locate executable or run via dotnet
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
    if not os.path.exists(cli_dll):
        print(f"ERROR: Cannot find {cli_dll}. Run dotnet build first.", file=sys.stderr)
        sys.exit(1)

    cmd = ["dotnet", cli_dll, "lsp"]
    print("=" * 60)
    print(" Running Otter LSP Protocol Conformance Tests")
    print(f" Target: {' '.join(cmd)}")
    print("=" * 60)

    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=root_dir
    )

    req_id = 0
    passed = 0
    failed = 0

    def send_message(payload):
        nonlocal req_id
        body = json.dumps(payload).encode("utf-8")
        header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
        proc.stdin.write(header + body)
        proc.stdin.flush()

    def send_request(method, params=None):
        nonlocal req_id
        req_id += 1
        msg = {
            "jsonrpc": "2.0",
            "id": req_id,
            "method": method
        }
        if params is not None:
            msg["params"] = params
        send_message(msg)
        return req_id

    def send_notification(method, params=None):
        msg = {
            "jsonrpc": "2.0",
            "method": method
        }
        if params is not None:
            msg["params"] = params
        send_message(msg)

    def read_message(timeout=5.0):
        start = time.time()
        headers = {}
        header_bytes = bytearray()
        while True:
            if time.time() - start > timeout:
                raise TimeoutError("Timed out reading LSP header")
            line = proc.stdout.readline()
            if not line:
                stderr_output = proc.stderr.read().decode('utf-8', errors='replace')
                raise EOFError(f"LSP server closed stdout. Stderr:\n{stderr_output}")
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
        print("\nStep 1: Testing 'initialize' request...")
        init_id = send_request("initialize", {
            "processId": os.getpid(),
            "rootUri": f"file://{root_dir}",
            "capabilities": {}
        })
        init_resp = read_message()
        assert_test("Initialize response ID matches", init_resp.get("id") == init_id)
        caps = init_resp.get("result", {}).get("capabilities", {})
        assert_test("TextDocumentSync Full (change == 1)", caps.get("textDocumentSync", {}).get("change") == 1)
        assert_test("Completion provider declared", "completionProvider" in caps)
        assert_test("Hover provider declared", caps.get("hoverProvider") is True)
        assert_test("Definition provider declared", caps.get("definitionProvider") is True)
        assert_test("References provider declared", caps.get("referencesProvider") is True)
        assert_test("Rename provider declared", caps.get("renameProvider") is True)
        assert_test("Formatting provider declared", caps.get("documentFormattingProvider") is True)
        assert_test("Workspace symbol provider declared", caps.get("workspaceSymbolProvider") is True)

        # 2. Initialized Notification
        print("\nStep 2: Sending 'initialized' notification...")
        send_notification("initialized", {})
        time.sleep(0.1)

        # 3. textDocument/didOpen with syntax error -> publishDiagnostics
        print("\nStep 3: Testing 'textDocument/didOpen' with invalid code...")
        doc_uri = f"file://{root_dir}/test_sample.ot"
        invalid_code = "if is 10\n    say \"broken\"\n.\n"
        send_notification("textDocument/didOpen", {
            "textDocument": {
                "uri": doc_uri,
                "languageId": "otter",
                "version": 1,
                "text": invalid_code
            }
        })
        diag_msg = read_message()
        assert_test("Diagnostic notification received", diag_msg.get("method") == "textDocument/publishDiagnostics")
        diags = diag_msg.get("params", {}).get("diagnostics", [])
        assert_test("Diagnostics contains error for invalid code", len(diags) > 0, f"Found {len(diags)} errors")

        # 4. textDocument/didChange with valid code -> publishDiagnostics empty
        print("\nStep 4: Testing 'textDocument/didChange' with valid code...")
        valid_code = (
            "to addNumbers x and y\n"
            "    return x plus y\n"
            ".\n\n"
            "total is addNumbers 10 and 20\n"
            "say total\n"
        )
        send_notification("textDocument/didChange", {
            "textDocument": {
                "uri": doc_uri,
                "version": 2
            },
            "contentChanges": [
                {"text": valid_code}
            ]
        })
        diag_msg2 = read_message()
        assert_test("Diagnostic notification after change", diag_msg2.get("method") == "textDocument/publishDiagnostics")
        diags2 = diag_msg2.get("params", {}).get("diagnostics", [])
        assert_test("Diagnostics clear (0 errors)", len(diags2) == 0, f"Found {len(diags2)} errors")

        # 5. textDocument/completion
        print("\nStep 5: Testing 'textDocument/completion'...")
        comp_id = send_request("textDocument/completion", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 4, "character": 9} # After "total is "
        })
        comp_resp = read_message()
        assert_test("Completion response ID matches", comp_resp.get("id") == comp_id)
        items = comp_resp.get("result", {}).get("items", [])
        labels = [item.get("label") for item in items]
        assert_test("Completion items list returned", len(items) > 0)
        assert_test("Completion includes user function 'addNumbers'", "addNumbers" in labels)
        assert_test("Completion includes core keyword 'if'", "if" in labels)

        # 6. textDocument/hover
        print("\nStep 6: Testing 'textDocument/hover'...")
        hover_id = send_request("textDocument/hover", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 4, "character": 12} # Over "addNumbers"
        })
        hover_resp = read_message()
        assert_test("Hover response ID matches", hover_resp.get("id") == hover_id)
        hover_val = hover_resp.get("result", {}).get("contents", {}).get("value", "")
        assert_test("Hover markdown explains 'addNumbers'", "addNumbers" in hover_val)

        # 6b. Hover on core keyword "say"
        hover_id2 = send_request("textDocument/hover", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 5, "character": 2} # Over "say"
        })
        hover_resp2 = read_message()
        hover_val2 = hover_resp2.get("result", {}).get("contents", {}).get("value", "")
        assert_test("Hover explains core keyword 'say'", "say" in hover_val2 and "Standard" in hover_val2 or "output" in hover_val2.lower())

        # 7. textDocument/definition
        print("\nStep 7: Testing 'textDocument/definition'...")
        def_id = send_request("textDocument/definition", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 4, "character": 12} # Over "addNumbers"
        })
        def_resp = read_message()
        assert_test("Definition response ID matches", def_resp.get("id") == def_id)
        def_target = def_resp.get("result", {})
        assert_test("Definition points to line 0 (1st line)", def_target.get("range", {}).get("start", {}).get("line") == 0)

        # 8. textDocument/references
        print("\nStep 8: Testing 'textDocument/references'...")
        ref_id = send_request("textDocument/references", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 0, "character": 5} # Over "addNumbers" definition
        })
        ref_resp = read_message()
        assert_test("References response ID matches", ref_resp.get("id") == ref_id)
        ref_list = ref_resp.get("result", [])
        assert_test("Found at least 2 references (declaration + usage)", len(ref_list) >= 2)

        # 9. textDocument/rename
        print("\nStep 9: Testing 'textDocument/rename'...")
        ren_id = send_request("textDocument/rename", {
            "textDocument": {"uri": doc_uri},
            "position": {"line": 0, "character": 5},
            "newName": "sumNumbers"
        })
        ren_resp = read_message()
        assert_test("Rename response ID matches", ren_resp.get("id") == ren_id)
        changes = ren_resp.get("result", {}).get("changes", {}).get(doc_uri, [])
        assert_test("Rename returned workspace edit text", len(changes) > 0)
        if changes:
            new_text = changes[0].get("newText", "")
            assert_test("Renamed source contains 'sumNumbers'", "sumNumbers" in new_text and "addNumbers" not in new_text)

        # 10. textDocument/formatting
        print("\nStep 10: Testing 'textDocument/formatting'...")
        unformatted_uri = f"file://{root_dir}/test_unformatted.ot"
        unformatted_code = "if score is 10\nsay \"ten\"\n.\n"
        send_notification("textDocument/didOpen", {
            "textDocument": {
                "uri": unformatted_uri,
                "languageId": "otter",
                "version": 1,
                "text": unformatted_code
            }
        })
        # consume diagnostics for unformatted
        _ = read_message()
        
        fmt_id = send_request("textDocument/formatting", {
            "textDocument": {"uri": unformatted_uri},
            "options": {"tabSize": 4, "insertSpaces": True}
        })
        fmt_resp = read_message()
        assert_test("Formatting response ID matches", fmt_resp.get("id") == fmt_id)
        fmt_edits = fmt_resp.get("result", [])
        assert_test("Formatting returned text edit", len(fmt_edits) > 0)
        if fmt_edits:
            formatted_text = fmt_edits[0].get("newText", "")
            assert_test("Formatted text contains 4-space indentation", "    say \"ten\"" in formatted_text)

        # 11. workspace/symbol
        print("\nStep 11: Testing 'workspace/symbol'...")
        sym_id = send_request("workspace/symbol", {
            "query": "add"
        })
        sym_resp = read_message()
        assert_test("Workspace symbol response ID matches", sym_resp.get("id") == sym_id)
        symbols = sym_resp.get("result", [])
        sym_names = [s.get("name") for s in symbols]
        assert_test("Found symbol 'addNumbers'", "addNumbers" in sym_names)

        # 12. textDocument/didClose
        print("\nStep 12: Testing 'textDocument/didClose'...")
        send_notification("textDocument/didClose", {
            "textDocument": {"uri": doc_uri}
        })
        close_diag = read_message()
        assert_test("Diagnostics cleared on close", len(close_diag.get("params", {}).get("diagnostics", [])) == 0)

        # 13. Shutdown & Exit
        print("\nStep 13: Testing 'shutdown' and 'exit'...")
        shut_id = send_request("shutdown", None)
        shut_resp = read_message()
        assert_test("Shutdown response result is null", shut_resp.get("result") is None)
        send_notification("exit", None)

        proc.wait(timeout=3.0)
        assert_test("Process exited with code 0", proc.returncode == 0)

    except Exception as e:
        print(f"\nFATAL TEST ERROR: {e}", file=sys.stderr)
        proc.kill()
        sys.exit(1)

    print("\n" + "=" * 60)
    print(f" Summary: {passed} passed, {failed} failed")
    print("=" * 60)

    if failed > 0:
        sys.exit(1)
    else:
        print(" Otter Native LSP Protocol Verification PASSED (100%)\n")

if __name__ == "__main__":
    main()
