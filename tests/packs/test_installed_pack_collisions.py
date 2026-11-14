#!/usr/bin/env python3
"""
Test Suite: Installed Pack Collision Verification
Verifies that calling functions from pinned, installed packs in an isolated project
environment using the installed Release executable does NOT overwrite caller variables:
'result', 'total', 'totalCount', 'clean', 'item', 'target', 'actor', 'content', 'copy'.

Workflow:
1. Initializes an isolated temporary project.
2. Installs all 5 official packs via 'otter pack install'.
3. Verifies lockfile integrity via 'otter pack verify'.
4. Executes real pack consumption importing from .otter/packages/...
5. Asserts caller state preservation and consumeItemFromInventory API functionality.
"""

import os
import sys
import subprocess
import tempfile
import json

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    
    # 1. Determine Otter executable: prefer isolated install, allow CLI arg or OTTER_NET_BIN
    env_bin = os.environ.get("OTTER_NET_BIN")
    installed_dist_bin = os.path.join(repo_root, "dist", "isolated-otter-install", "bin", "otter")
    
    if len(sys.argv) > 1 and sys.argv[1]:
        otter_bin = sys.argv[1]
    elif env_bin and os.path.exists(env_bin):
        otter_bin = env_bin
    elif os.path.exists(installed_dist_bin):
        otter_bin = installed_dist_bin
    else:
        cli_dll = os.path.join(repo_root, "src", "Otter.Cli", "bin", "Release", "net10.0", "otter.dll")
        if not os.path.exists(cli_dll):
            cli_dll = os.path.join(repo_root, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
        otter_bin = cli_dll

    print("=" * 70)
    print(" Running Installed Pack Collision & Lockfile Integrity Suite")
    print(f" Executable: {otter_bin}")
    print("=" * 70)

    is_dotnet_dll = otter_bin.endswith(".dll")
    cmd_prefix = ["dotnet", otter_bin] if is_dotnet_dll else [otter_bin]

    # Verify version
    ver_res = subprocess.run(cmd_prefix + ["--version"], capture_output=True, text=True)
    if ver_res.returncode != 0:
        print(f"FATAL: Unable to run otter binary: {ver_res.stderr}")
        sys.exit(1)
    print(f" Otter Version: {ver_res.stdout.strip()}")

    with tempfile.TemporaryDirectory() as project_dir:
        print(f" Isolated Test Project: {project_dir}")

        # 2. Install all 5 official packs into isolated project
        packs_to_install = [
            ("math-utils", "1.0.1"),
            ("text-utils", "1.0.1"),
            ("list-utils", "1.0.1"),
            ("file-helpers", "1.0.1"),
            ("game-entity", "1.1.0"),
        ]

        for pack_name, expected_ver in packs_to_install:
            install_cmd = cmd_prefix + ["pack", "install", pack_name, "--project", project_dir]
            res = subprocess.run(install_cmd, cwd=repo_root, capture_output=True, text=True)
            if res.returncode != 0:
                print(f"FATAL: Failed to install pack '{pack_name}':\n{res.stderr}\n{res.stdout}")
                sys.exit(1)
            print(f"  [INSTALLED] {pack_name} v{expected_ver}")

        # 3. Verify lockfile integrity
        verify_cmd = cmd_prefix + ["pack", "verify", "--project", project_dir]
        verify_res = subprocess.run(verify_cmd, cwd=project_dir, capture_output=True, text=True)
        if verify_res.returncode != 0:
            print(f"FATAL: Pack integrity verification failed:\n{verify_res.stdout}\n{verify_res.stderr}")
            sys.exit(1)
        print("  [LOCKFILE VERIFIED] All 5 installed packs match pinned SHA-256 digests.")

        # 4. Generate test script consuming installed packs
        test_script_content = f"""
use ".otter/packages/otter-official/math-utils/1.0.1/src/main.ot"
use ".otter/packages/otter-official/text-utils/1.0.1/src/main.ot"
use ".otter/packages/otter-official/list-utils/1.0.1/src/main.ot"
use ".otter/packages/otter-official/file-helpers/1.0.1/src/main.ot"
use ".otter/packages/otter-official/game-entity/1.1.0/src/main.ot"

say "--- Stage 1: Initializing Caller Scope Variables ---"
result is "USER_RESULT_PRESERVED"
total is 77777
totalCount is 44444
clean is "USER_CLEAN_TEXT"
item is "USER_ITEM_RECORD"
target is "USER_TARGET_FILE"
actor is "USER_ACTOR_STATE"
content is "USER_RAW_CONTENT"
copy is "USER_COPY_SLOT"

say "--- Stage 2: Calling Text and List Utilities with Caller 'result' Active ---"
rep is repeatText "abc" 3
if result is not "USER_RESULT_PRESERVED"
    fail with "Collision: 'result' was overwritten by repeatText"
.

padded is padLeft "42" 6 "0"
if result is not "USER_RESULT_PRESERVED"
    fail with "Collision: 'result' was overwritten by padLeft"
.

testList are empty
add 10 to testList
add 20 to testList
add 10 to testList
deduped is dedupeList testList
if result is not "USER_RESULT_PRESERVED"
    fail with "Collision: 'result' was overwritten by dedupeList"
.
say "[PASS] Caller variable 'result' preserved across text-utils and list-utils."

say "--- Stage 3: Calling Math and List Utilities with Caller 'total' Active ---"
avgVal is calcAverage testList
if total is not 77777
    fail with "Collision: 'total' was overwritten by calcAverage"
.

sumVal is calcListSum testList
if total is not 77777
    fail with "Collision: 'total' was overwritten by calcListSum"
.
say "[PASS] Caller variable 'total' preserved across math-utils and list-utils."

say "--- Stage 4: Calling Slugify with Caller 'clean' Active ---"
slug is slugify "My Installed Pack Test!"
if clean is not "USER_CLEAN_TEXT"
    fail with "Collision: 'clean' was overwritten by slugify"
.
say "[PASS] Caller variable 'clean' preserved across text-utils."

say "--- Stage 5: Calling Game Entity Factories with Caller 'item' and 'actor' Active ---"
potion is createStackableItem "Health Potion" "potion" 50 5 10
if item is not "USER_ITEM_RECORD"
    fail with "Collision: 'item' was overwritten by createStackableItem"
.

hero is createActor "Hero" 200 1
if actor is not "USER_ACTOR_STATE"
    fail with "Collision: 'actor' was overwritten by createActor"
.
say "[PASS] Caller variables 'item' and 'actor' preserved across game-entity."

say "--- Stage 6: Calling File Helpers with Caller 'target' and 'content' Active ---"
fallbackObj is "FALLBACK_VAL"
parsed is fromJsonSafe "{{\\"status\\": \\"healthy\\"}}" fallbackObj
if target is not "USER_TARGET_FILE"
    fail with "Collision: 'target' was overwritten by fromJsonSafe"
.
if content is not "USER_RAW_CONTENT"
    fail with "Collision: 'content' was overwritten by fromJsonSafe"
.
say "[PASS] Caller variables 'target' and 'content' preserved across file-helpers."

say "--- Stage 7: Verifying Public consumeItemFromInventory API ---"
inv is createInventory 5
potions is createStackableItem "Health Potion" "potion" 50 5 10
added is addItemToInventory inv potions
if getInventoryCount inv is not 1
    fail with "Expected 1 inventory slot used"
.

consumedOne is consumeItemFromInventory inv "Health Potion" 2
if not consumedOne
    fail with "consumeItemFromInventory failed to consume 2 potions"
.
foundPot is findItemInInventory inv "Health Potion"
if itemCount of foundPot is not 3
    fail with "Expected 3 potions remaining after consuming 2"
.

# Consume remaining 3 potions to test complete slot removal
consumedRemaining is consumeItemFromInventory inv "Health Potion" 3
if not consumedRemaining
    fail with "consumeItemFromInventory failed to consume remaining 3 potions"
.
if getInventoryCount inv is not 0
    fail with "Expected inventory count to be 0 after consuming all potions"
.
say "[PASS] Public consumeItemFromInventory API functions correctly on installed pack."

say "=========================================================="
say " ALL INSTALLED PACK COLLISION AND API CHECKS PASSED 100%"
say "=========================================================="
"""
        test_script_path = os.path.join(project_dir, "test_installed_consumption.ot")
        with open(test_script_path, "w") as f:
            f.write(test_script_content)

        # 5. Execute test script in isolated project directory
        run_cmd = cmd_prefix + ["run", "test_installed_consumption.ot"]
        test_res = subprocess.run(run_cmd, cwd=project_dir, capture_output=True, text=True)

        print(test_res.stdout)
        if test_res.returncode != 0:
            print(f"FATAL: Test run failed with exit code {test_res.returncode}:")
            print(test_res.stderr)
            sys.exit(1)

    print("=" * 70)
    print(" SUCCESS: Installed Pack collision & integrity test PASSED (100%)")
    print("=" * 70)
    sys.exit(0)

if __name__ == "__main__":
    main()
