# Otter Standard Library & Ecosystem Reference

Otter includes a curated, fully offline catalog of 38 snippets, 5 official source packs, 4 blueprints, and 4 application recipes.

---

## 1. Official Source Packs

Official source packs are versioned libraries maintained in the Otter repository and distributed with every install.

| Pack | Version | Description | Key APIs |
|------|---------|-------------|----------|
| `otter-official/math-utils` | 1.0.1 | Math helpers and scalar ranges | `clamp`, `lerp`, `mapRange`, `calcAverage` |
| `otter-official/list-utils` | 1.0.1 | Collection utilities and search | `hasItem`, `dedupeList`, `filterList`, `countMatches` |
| `otter-official/text-utils` | 1.0.1 | String formatting and transformation | `padLeft`, `padRight`, `slugify`, `splitText` |
| `otter-official/file-helpers` | 1.0.1 | Safe file I/O and JSON storage | `readOrFallback`, `writeSafe`, `readJsonObject` |
| `otter-official/game-entity` | 1.1.0 | Combat and RPG entity mechanics | `Actor`, `applyDamage`, `restoreHealth`, `levelUp`, `Inventory`, `consumeItemFromInventory` |

### Installing a Pack
```bash
otter pack install otter-official/math-utils
```

### Inspecting a Pack
```bash
otter pack inspect otter-official/math-utils
```

---

## 2. Snippet Catalog (38 Core Snippets)

Otter Snippets are verified, self-contained code patterns that can be searched or inserted into any project with `otter snippet`.

### Searching Snippets
```bash
otter snippet search math
otter snippet search json
```

### Inserting a Snippet
```bash
# Insert into src/utils.ot at line 10
otter snippet insert math-clamp --file src/utils.ot --at-line 10
```

### Catalog Categories
- **Control Flow:** `cf-for-each`, `cf-count-range`, `cf-while-loop`, `cf-if-otherwise`, `cf-try-otherwise`
- **Text & Strings:** `text-slugify`, `text-pad-left`, `text-split-join`, `text-replace-all`
- **Math & Numeric:** `math-clamp`, `math-lerp`, `math-map-range`, `math-calc-average`
- **Lists & Collections:** `list-contains-check`, `list-deduplicate`, `list-filter-items`, `list-sort-reverse`
- **File System & I/O:** `file-read-safe`, `file-write-text`, `file-json-storage`, `file-csv-parse`, `file-append-log`
- **Data & Structures:** `data-custom-type`, `data-object-thing`, `conv-number-format`, `conv-bytes-hex`
- **Date & Time:** `date-diff-days`, `date-add-days`
- **Networking & Database:** `web-http-json-response`, `http-get-request`, `db-sqlite-query`, `db-sqlite-transaction`
- **Validation:** `val-email-check`, `val-numeric-range`
- **Games & Interactive:** `game-actor-damage`, `game-cooldown-system`, `game-inventory-capacity`

---

## 3. Blueprints

Blueprints generate complete, ready-to-use subsystem code directly into your project:

```bash
# List available blueprints
otter blueprint list

# Apply game combat blueprint
otter blueprint apply otter-official/game-combat --out src/combat.ot
```

Available Blueprints:
- `otter-official/console-calculator`: Interactive command-line evaluator with arithmetic engine.
- `otter-official/file-settings`: Configuration management with JSON persistence.
- `otter-official/game-combat`: Turn-based damage, health pools, and victory rewards.
- `otter-official/text-cleanup`: Automated normalization, whitespace trimming, and slug generation.

---

## 4. Application Recipes

Recipes scaffold full, multi-file application architectures:

```bash
# Apply contact manager recipe
otter recipe apply otter-official/contact-manager --target ./contacts-app
```

Available Recipes:
- `otter-official/contact-manager`: Desktop CRUD address book with search and export.
- `otter-official/rpg-adventure`: Turn-based RPG dungeon crawler with equipment and leveling.
- `otter-official/task-tracker`: Priority task list with status filters and disk persistence.
- `otter-official/web-portal`: Full-stack web server with HTML/JSON routing.
