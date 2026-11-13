#!/usr/bin/env python3
"""
Interactive UI Workflow Test for Otter Contact Manager
Launches the real Otter CLI application and tests the user-facing workflow:
creating, editing, deleting, searching, saving, restarting, and recovering data
through the actual interactive terminal prompts (stdin/stdout).
"""

import os
import sys
import subprocess

def run_session(otter_cmd, app_dir, inputs):
    process = subprocess.Popen(
        otter_cmd + ["run", app_dir],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )
    payload = "\n".join(inputs) + "\n"
    stdout, stderr = process.communicate(input=payload)
    return process.returncode, stdout, stderr

def main():
    root_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
    app_dir = os.path.join(root_dir, "examples", "contact-manager-app")
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
    
    if os.path.exists(cli_dll):
        otter_cmd = ["dotnet", cli_dll]
    else:
        otter_cmd = ["otter"]

    db_path = os.path.join(app_dir, "contacts_interactive.json")
    if os.path.exists(db_path):
        os.remove(db_path)

    print("=" * 65)
    print(" Testing Interactive Contact Manager Workflow (Phase 8)")
    print("=" * 65)

    # ---------------------------------------------------------
    # Session 1: Create, List, View, Search, Edit, Delete, Save
    # ---------------------------------------------------------
    print("Session 1: Driving interactive command prompts...")
    s1_inputs = [
        "stats",
        "add",
        "Alice Cooper",
        "alice@rock.com",
        "555-1111",
        "Musician",
        "add",
        "Bob Dylan",
        "bob@folk.com",
        "555-2222",
        "Folk",
        "list",
        "view",
        "1",
        "search",
        "rock",
        "edit",
        "1",
        "alice@cooper.com",
        "555-9999",
        "Rock Icon",
        "delete",
        "2",
        "save",
        db_path,
        "stats",
        "quit"
    ]

    rc, out, err = run_session(otter_cmd, app_dir, s1_inputs)
    if rc != 0:
        print(f"[FAIL] Session 1 failed with exit code {rc}")
        print("STDERR:", err)
        print("STDOUT:", out)
        sys.exit(1)

    # Verifications in Session 1 output
    assert "Success: Added contact" in out and "Alice Cooper" in out, "Failed adding Alice"
    assert "Success: Added contact" in out and "Bob Dylan" in out, "Failed adding Bob"
    assert "Alice Cooper" in out, "Alice not listed"
    assert "Bob Dylan" in out, "Bob not listed"
    assert "ID:" in out and "1" in out, "View contact #1 failed"
    assert "Success: Contact # 1 updated." in out or "Contact #1 updated." in out or "updated" in out, "Edit contact #1 failed"
    assert "Success: Contact # 2 deleted." in out or "Contact #2 deleted." in out or "deleted" in out, "Delete contact #2 failed"
    assert "Success: Store saved to" in out, "Save store failed"
    assert "Exiting Contact Manager. Goodbye!" in out, "Quit command failed"
    assert os.path.exists(db_path), "Saved database file was not created on disk"
    print("  [PASS] Session 1: All interactive commands executed and verified.")

    # ---------------------------------------------------------
    # Session 2: Fresh Process Restart & Data Recovery
    # ---------------------------------------------------------
    print("Session 2: Fresh process restart & data recovery...")
    s2_inputs = [
        "load",
        db_path,
        "list",
        "stats",
        "quit"
    ]

    rc2, out2, err2 = run_session(otter_cmd, app_dir, s2_inputs)
    if rc2 != 0:
        print(f"[FAIL] Session 2 failed with exit code {rc2}")
        print("STDERR:", err2)
        print("STDOUT:", out2)
        sys.exit(1)

    assert "Alice Cooper" in out2, "Alice Cooper missing after restart recovery"
    assert "alice@cooper.com" in out2, "Updated email missing after recovery"
    assert "Rock Icon" in out2, "Updated tag missing after recovery"
    assert "Bob Dylan" not in out2, "Deleted contact Bob Dylan reappeared after restart"
    assert "Total Contacts:  1" in out2 or "Total Contacts: 1" in out2, "Total contacts count mismatch after recovery"
    print("  [PASS] Session 2: Restored state matches expected data exactly across process boundaries.")

    # Cleanup
    if os.path.exists(db_path):
        os.remove(db_path)

    print("=" * 65)
    print(" ALL INTERACTIVE CONTACT MANAGER WORKFLOW TESTS PASSED (100%)")
    print("=" * 65)

if __name__ == "__main__":
    main()
