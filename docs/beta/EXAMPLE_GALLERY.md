# Otter Example Project Gallery

A showcase of real-world, complete applications built with Otter 1.0.1, runnable with the native CLI and tested outside the compiler repository.

---

## 1. Personal Finance Tracker
**Directory:** `examples/finance-tracker`

A complete ledger tracking income, expenses, and savings with category breakdowns and JSON persistence.

### Highlights
- Creates structured transaction entities (`id`, `date`, `category`, `amount`, `isIncome`).
- Computes monthly totals, expense aggregations, and net savings.
- Searches transaction histories with multi-term filtering.
- Persists and reloads full transaction ledgers in JSON format.
- Comprehensive unit tests covering all arithmetic and aggregations.

### How to Run
```bash
cd examples/finance-tracker
otter run src/finance.ot
otter test
```

---

## 2. Desktop Task Manager
**Directory:** `examples/task-manager`

A multi-interface task management application featuring a terminal progress dashboard and a graphical desktop GUI window.

### Highlights
- Complete CRUD: Create, Edit, Toggle Completion, and Delete tasks.
- Filters tasks by status (`Pending`, `InProgress`, `Done`).
- Full-text search across titles and descriptions.
- **Dual Interface:**
  - `src/tasks.ot`: Terminal runner with progress indicators and status summaries.
  - `src/tasks_gui.ot`: Interactive desktop window compiled with `otter web` and hosted via `otter desktop`.
- Complete unit test suite with 100% pass rate.

### How to Run
```bash
cd examples/task-manager

# Run CLI Version
otter run src/tasks.ot

# Run Desktop / Web Version
otter web src/tasks_gui.ot -NoOpen

# Run Test Suite
otter test
```

---

## 3. Dungeon Quest RPG Game
**Directory:** `examples/rpg-game`

A turn-based role-playing combat and progression game demonstrating official package reuse (`otter-official/game-entity`).

### Highlights
- Imports and reuses `otter-official/game-entity` for Actor models and stat clamping.
- Turn-based combat loop: attack, consumable healing potions, enemy counter-attacks.
- Dynamic equipment system: weapons and armor dynamically scale attack and defense stats.
- Scaled experience and leveling progression: levels restore full health and grant stat boosts.
- Save and restore game progress to and from disk.
- Unit tests verifying combat math, healing, and level thresholds.

### How to Run
```bash
cd examples/rpg-game
otter run src/game.ot
otter test
```

---

## 4. Web Application Samples
**Directory:** `docs/web/samples/frontend`

Self-contained web applications compiled from Otter directly into standalone HTML and JavaScript via `otter web`:
- `counter.ot`: Reactive state and event-driven counting.
- `perf.ot`: High-throughput physics simulation loop.
- `tutorial_game.ot`: Canvas 2D game loop with keyboard input.
