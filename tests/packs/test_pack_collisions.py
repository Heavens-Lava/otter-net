#!/usr/bin/env python3
"""
Task 2 Test Suite: Pack Variable Collision Audit & Protection Verification
Tests that calling functions in all 5 official packs does NOT overwrite caller
variables named: result, total, totalCount, clean, item, target, actor, content, copy.
Also verifies new public game-entity API: consumeItemFromInventory.
"""

import os
import sys
import subprocess
import tempfile

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    cli_dll = os.path.join(repo_root, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

    if len(sys.argv) > 1 and sys.argv[1]:
        otter_cmd = [sys.argv[1], "run"]
    elif os.path.exists(cli_dll):
        otter_cmd = ["dotnet", cli_dll, "run"]
    else:
        otter_cmd = ["otter", "run"]

    test_script = f"""
use "{repo_root}/library/packs/otter-official/math-utils/src/main.ot"
use "{repo_root}/library/packs/otter-official/text-utils/src/main.ot"
use "{repo_root}/library/packs/otter-official/list-utils/src/main.ot"
use "{repo_root}/library/packs/otter-official/file-helpers/src/main.ot"
use "{repo_root}/library/packs/otter-official/game-entity/src/main.ot"

say "--- Testing Pack Collision Protection ---"

# 1. Caller defines result
result is "USER_RESULT_PRESERVED"
rep is repeatText "abc" 3
if result is not "USER_RESULT_PRESERVED"
    fail with "Collision: 'result' was overwritten by repeatText"
.
padded is padLeft "12" 5 "0"
if result is not "USER_RESULT_PRESERVED"
    fail with "Collision: 'result' was overwritten by padLeft"
.
sampleList are empty
add 1 to sampleList
add 2 to sampleList
add 1 to sampleList
deduped is dedupeList sampleList
if result is not "USER_RESULT_PRESERVED"
    fail with "Collision: 'result' was overwritten by dedupeList"
.
say "1. 'result' protected across text-utils and list-utils."

# 2. Caller defines total
total is 88888
nums are empty
add 10 to nums
add 20 to nums
add 30 to nums
avgVal is calcAverage nums
if total is not 88888
    fail with "Collision: 'total' was overwritten by calcAverage"
.
sumVal is calcSum nums
if total is not 88888
    fail with "Collision: 'total' was overwritten by calcSum"
.
lSum is calcListSum nums
if total is not 88888
    fail with "Collision: 'total' was overwritten by calcListSum"
.
say "2. 'total' protected across math-utils and list-utils."

# 3. Caller defines clean
clean is "USER_CLEAN_INPUT"
slugVal is slugify "My Awesome Project (v1.0)"
if clean is not "USER_CLEAN_INPUT"
    fail with "Collision: 'clean' was overwritten by slugify"
.
say "3. 'clean' protected in text-utils slugify."

# 4. Caller defines item
item is "USER_MAGIC_WAND"
newItem is createItem "Iron Shield" "armor" 15
if item is not "USER_MAGIC_WAND"
    fail with "Collision: 'item' was overwritten by createItem"
.
stackItem is createStackableItem "Mana Potion" "potion" 25 5 10
if item is not "USER_MAGIC_WAND"
    fail with "Collision: 'item' was overwritten by createStackableItem"
.
say "4. 'item' protected in game-entity createItem / createStackableItem."

# 5. Caller defines target
target is "USER_TARGET_RECORD"
jsonRes is fromJsonSafe "{{\\"val\\": 100}}" gone
if target is not "USER_TARGET_RECORD"
    fail with "Collision: 'target' was overwritten by fromJsonSafe"
.
say "5. 'target' protected in file-helpers fromJsonSafe."

# 6. Caller defines actor
actor is "USER_ACTOR_NAME"
newAct is createActor "Hero" 120 1
if actor is not "USER_ACTOR_NAME"
    fail with "Collision: 'actor' was overwritten by createActor"
.
say "6. 'actor' protected in game-entity createActor."

# 7. Caller defines content and copy
content is "USER_CONTENT_PAYLOAD"
nonExistent is readOrFallback "missing_file_xyz.txt" "FALLBACK"
if content is not "USER_CONTENT_PAYLOAD"
    fail with "Collision: 'content' was overwritten by readOrFallback"
.
copy is "USER_COPY_VAR"
sortedNums is sortList nums
if copy is not "USER_COPY_VAR"
    fail with "Collision: 'copy' was overwritten by sortList"
.
say "7. 'content' and 'copy' protected in file-helpers and list-utils."

# 8. Test consumeItemFromInventory API
say "--- Testing consumeItemFromInventory Public API ---"
bag is createInventory 5
potions is createStackableItem "Health Potion" "potion" 50 5 10
addItemToInventory bag potions

if getInventoryCount bag is not 1
    fail with "Expected 1 inventory slot used"
.

# Consume 2 potions
consumeOk is consumeItemFromInventory bag "Health Potion" 2
if consumeOk is not true
    fail with "consumeItemFromInventory failed for 2 potions"
.
foundPot is findItemInInventory bag "Health Potion"
if itemCount of foundPot is not 3
    fail with "Expected 3 potions remaining after consuming 2"
.

# Consume remaining 3 potions -> should remove slot
consumeAllOk is consumeItemFromInventory bag "Health Potion" 3
if consumeAllOk is not true
    fail with "consumeItemFromInventory failed for remaining 3 potions"
.
if getInventoryCount bag is not 0
    fail with "Expected inventory slot to be freed when stack reaches 0"
.

# Attempt to consume again when empty -> should return false
consumeEmpty is consumeItemFromInventory bag "Health Potion" 1
if consumeEmpty is not false
    fail with "Expected consumeItemFromInventory to return false when item absent"
.
say "8. consumeItemFromInventory stack reduction and slot removal verified."

say "ALL PACK COLLISION & CONSUME TESTS PASSED 100%"
"""

    with tempfile.NamedTemporaryFile("w", suffix=".ot", delete=False) as f:
        f.write(test_script)
        test_path = f.name

    try:
        p = subprocess.run(otter_cmd + [test_path], capture_output=True, text=True)
        print(p.stdout)
        if p.stderr:
            print("STDERR:", p.stderr)
        if p.returncode != 0:
            print(f"[FAIL] Test failed with exit code {p.returncode}")
            sys.exit(1)
        else:
            print("[SUCCESS] All Pack variable collision and consume tests PASSED.")
            sys.exit(0)
    finally:
        if os.path.exists(test_path):
            os.remove(test_path)

if __name__ == "__main__":
    main()
