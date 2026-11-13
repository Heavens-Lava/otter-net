# RPG Inventory and Combat Application

A complete Otter RPG inventory and combat program demonstrating real-world pack consumption, equipment bonus calculations, stackable inventory management, combat mitigation, leveling, and full game state recovery across restarts.

## Packs Consumed
- `otter-official/game-entity` v1.0.0 (`createActor`, `createEquipment`, `equipItem`, `createInventory`, `addItemToInventory`, `applyDamage`, `restoreHealth`, `gainExperience`, `serializeGameState`, `deserializeGameState`)
- `otter-official/math-utils` v1.0.0 (`clamp`, math calculations)
- `otter-official/file-helpers` v1.0.0 (`writeJsonObject`, `readJsonObjectSafe`, `deleteFileSafe`)

## Capabilities
- Character creation with base stats, level, and max health
- Weapon and armor equipment slots with dynamic attack and defense calculations
- Inventory bag capacity limit enforcement
- Stackable items (e.g. Health Potion stack management)
- Consumable item consumption with health restoration
- Turn-based combat loop with damage mitigation and lethality checks
- Experience threshold leveling with automatic stat boosts
- Complete game state serialization to JSON
- Full state recovery across independent application processes

## Running the Application
```bash
otter run examples/rpg-combat-app
```

## Running Persistence Tests
```bash
python3 examples/rpg-combat-app/tests/test_persistence.py
```
Demonstrates that Process 1 conducts battles, equips gear, gains EXP, levels up to Level 2, saves progress to disk, and exits. Process 2 launches in a clean OS process and recovers the character, stats, equipment, and remaining inventory.
