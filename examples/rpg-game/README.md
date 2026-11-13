# Otter RPG: Dungeon Quest

A turn-based RPG game written in Otter 1.0.1, demonstrating entity mechanics, equipment, inventory, combat calculation, leveling progression, and persistence.

## Features

- **Reuses Official Pack**: Built with `otter-official/game-entity` (Actor models, Health clamping, Inventory items, and Leveling).
- **Equipment & Stats**: Dynamic attack and defense scaling based on equipped weapons and armors.
- **Turn-based Combat**: Turn sequence supporting basic attacks, consumable healing items, and enemy counter-attacks.
- **Experience & Leveling**: Scaled XP thresholds trigger stat boosts and full health restoration on level-up.
- **Save & Load**: JSON-based game state serialization and restoration.

## Running

```bash
# Run game demo
otter run src/game.ot

# Run test suite
otter test
```
