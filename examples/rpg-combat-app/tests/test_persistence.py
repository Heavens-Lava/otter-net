#!/usr/bin/env python3
import os
import subprocess
import json

app_dir = os.path.abspath("examples/rpg-combat-app")
otter_dll = os.path.abspath("src/Otter.Cli/bin/Debug/net10.0/otter.dll")
save_file = os.path.join(app_dir, "rpg_multiprocess.json")

if os.path.exists(save_file):
    os.remove(save_file)

# Script 1: Run process 1 to fight, level up, and save
proc1_code = f"""
use "../.otter/packages/otter-official/game-entity/1.1.0/src/main.ot"
use "../.otter/packages/otter-official/math-utils/1.0.1/src/main.ot"
use "../.otter/packages/otter-official/file-helpers/1.0.1/src/main.ot"

hero is createActor "Aurelius" 150 1
eq is createEquipment
sunblade is createItem "Sunblade" "weapon" 25
mithrilMail is createItem "Mithril Mail" "armor" 18
equipItem eq sunblade
equipItem eq mithrilMail

inv is createInventory 5
potions is createStackableItem "Health Potion" "consumable" 40 3 5
feather is createItem "Phoenix Feather" "consumable" 100
keyItem is createItem "Dungeon Key" "quest" 1
addItemToInventory inv potions
addItemToInventory inv feather
addItemToInventory inv keyItem

# Damage and potion use
applyDamage hero 20
foundPotions is findItemInInventory inv "Health Potion"
itemCount of foundPotions is 2
restoreHealth hero 20

# Gain EXP and level up
gainExperience hero 120 100

stateData is serializeGameState hero inv eq
ok is writeJsonObject "{save_file}" stateData
if ok is not true
    fail with "Process 1 save failed"
.
say "Process 1: RPG state saved to disk"
"""

proc1_file = os.path.join(app_dir, "src", "proc1_game_save.ot")
with open(proc1_file, "w") as f:
    f.write(proc1_code)

res1 = subprocess.run(["dotnet", otter_dll, proc1_file], capture_output=True, text=True, cwd=app_dir)
print("Process 1 Output:\n" + res1.stdout)
if res1.returncode != 0:
    print("Process 1 failed:", res1.stderr)
    exit(1)

if not os.path.exists(save_file):
    print("Save file was not written!")
    exit(1)

# Script 2: Fresh separate process loads state and asserts all properties
proc2_code = f"""
use "../.otter/packages/otter-official/game-entity/1.1.0/src/main.ot"
use "../.otter/packages/otter-official/math-utils/1.0.1/src/main.ot"
use "../.otter/packages/otter-official/file-helpers/1.0.1/src/main.ot"

fallbackSave has
    name is "None"
.
loadedRawState is readJsonObjectSafe "{save_file}" fallbackSave
if name of loadedRawState is not "Aurelius"
    fail with "Process 2: Savegame name mismatch"
.

restoredState is deserializeGameState loadedRawState
restoredHero is actor of restoredState
restoredInv is inventory of restoredState
restoredEq is equipment of restoredState

if name of restoredHero is not "Aurelius"
    fail with "Process 2: Restored hero name mismatch"
.
if level of restoredHero is not 2
    fail with "Process 2: Restored hero level mismatch: expected 2"
.
if health of restoredHero is not 175
    fail with "Process 2: Restored hero HP mismatch: expected 175"
.

restoredWeapon is weapon of restoredEq
if itemName of restoredWeapon is not "Sunblade"
    fail with "Process 2: Restored weapon mismatch"
.

restoredArmor is armor of restoredEq
if itemName of restoredArmor is not "Mithril Mail"
    fail with "Process 2: Restored armor mismatch"
.

restoredPotions is findItemInInventory restoredInv "Health Potion"
if restoredPotions is gone
    fail with "Process 2: Restored inventory missing Health Potion"
.
if itemCount of restoredPotions is not 2
    fail with "Process 2: Restored potion count mismatch: expected 2"
.

say "Process 2: Verified RPG state recovery across separate processes successfully!"
"""

proc2_file = os.path.join(app_dir, "src", "proc2_game_load.ot")
with open(proc2_file, "w") as f:
    f.write(proc2_code)

res2 = subprocess.run(["dotnet", otter_dll, proc2_file], capture_output=True, text=True, cwd=app_dir)
print("Process 2 Output:\n" + res2.stdout)
if res2.returncode != 0:
    print("Process 2 failed:", res2.stderr)
    exit(1)

# Cleanup
os.remove(proc1_file)
os.remove(proc2_file)
if os.path.exists(save_file):
    os.remove(save_file)

print("=== RPG Combat App Multi-Process Persistence Verified ===")
