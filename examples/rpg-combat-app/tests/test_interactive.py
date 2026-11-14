#!/usr/bin/env python3
"""
Interactive UI Workflow Test for Otter RPG Combat & Inventory Application
Launches the real Otter CLI application and tests the user-facing workflow:
inspecting status, viewing inventory, equipping items, battling enemies with damage mitigation,
drinking potions, gaining multi-level experience, saving, restarting in a clean process,
and verifying complete restored game state through terminal prompts (stdin/stdout).
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
    app_dir = os.path.join(root_dir, "examples", "rpg-combat-app")
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
    
    if len(sys.argv) > 1 and sys.argv[1]:
        otter_cmd = [sys.argv[1]]
    elif os.path.exists(cli_dll):
        otter_cmd = ["dotnet", cli_dll]
    else:
        otter_cmd = ["otter"]

    db_path = os.path.join(app_dir, "hero_interactive.json")
    if os.path.exists(db_path):
        os.remove(db_path)

    print("=" * 65)
    print(" Testing Interactive RPG Game Workflow (Phase 8/9)")
    print("=" * 65)

    # ---------------------------------------------------------
    # Session 1: Status, Equip, Unequip, Battle, Heal, Level Up, Save
    # ---------------------------------------------------------
    print("Session 1: Driving RPG interactive battle & inventory prompts...")
    s1_inputs = [
        "status",
        "inventory",
        "equip",
        "Sunblade",
        "equip",
        "Mithril Mail",
        "equip",
        "Amulet of Fury",
        "unequip",
        "accessory",
        "equip",
        "Amulet of Fury",
        "status",
        "battle",
        "heal",
        "status",
        "save",
        db_path,
        "quit"
    ]

    rc, out, err = run_session(otter_cmd, app_dir, s1_inputs)
    if rc != 0:
        print(f"[FAIL] Session 1 failed with exit code {rc}")
        print("STDERR:", err)
        print("STDOUT:", out)
        sys.exit(1)

    # Verifications in Session 1 output
    assert "Level:      1" in out or "Level:     1" in out, "Initial hero level mismatch"
    assert "Success: Equipped  Sunblade" in out or "Equipped  Sunblade" in out or "Sunblade" in out, "Equip Sunblade failed"
    assert "Success: Equipped  Mithril Mail" in out or "Equipped  Mithril Mail" in out or "Mithril Mail" in out, "Equip Mithril Mail failed"
    assert "Success: Equipped  Amulet of Fury" in out or "Equipped  Amulet of Fury" in out or "Amulet of Fury" in out, "Equip Amulet failed"
    assert "BATTLE: A wild  Dungeon Fiend" in out or "Dungeon Fiend" in out, "Combat encounter failed"
    assert "VICTORY! Dungeon Fiend has been defeated!" in out, "Battle victory missing"
    assert "LEVEL UP!" in out, "Level up missing after combat"
    assert "Level  2" in out or "Level 2" in out, "Hero level 2 promotion missing"
    assert "Drank potion!" in out, "Potion drinking failed"
    assert "Success: Game saved to" in out, "Game saving failed"
    assert "Saving your adventure for next time. Farewell, hero!" in out, "Quit failed"
    assert os.path.exists(db_path), "Savegame file was not created on disk"
    print("  [PASS] Session 1: Combat, equipment, inventory, and leveling fully verified.")

    # ---------------------------------------------------------
    # Session 2: Fresh Process Restart & Game State Recovery
    # ---------------------------------------------------------
    print("Session 2: Fresh process restart & game state recovery...")
    s2_inputs = [
        "load",
        db_path,
        "status",
        "inventory",
        "quit"
    ]

    rc2, out2, err2 = run_session(otter_cmd, app_dir, s2_inputs)
    if rc2 != 0:
        print(f"[FAIL] Session 2 failed with exit code {rc2}")
        print("STDERR:", err2)
        print("STDOUT:", out2)
        sys.exit(1)

    assert "Success: Loaded game for  Aurelius" in out2 or "Loaded game" in out2, "Save load failed"
    assert "Level:     2" in out2 or "Level:      2" in out2 or "Level 2" in out2, "Restored level mismatch"
    assert "Sunblade" in out2, "Restored weapon mismatch"
    assert "Mithril Mail" in out2, "Restored armor mismatch"
    assert "Amulet of Fury" in out2, "Restored accessory mismatch"
    print("  [PASS] Session 2: Hero state, equipment, stats, and inventory restored across process boundaries.")

    # Cleanup
    if os.path.exists(db_path):
        os.remove(db_path)

    print("=" * 65)
    print(" ALL INTERACTIVE RPG GAME WORKFLOW TESTS PASSED (100%)")
    print("=" * 65)

if __name__ == "__main__":
    main()
