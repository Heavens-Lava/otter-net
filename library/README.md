# Otter Reusable Library & Pack Architecture

Implemented in strict accordance with [OTTER_REUSABLE_LIBRARY_RFC.md](file:///Users/jeffreymacy/Projects/otter-net/OTTER_REUSABLE_LIBRARY_RFC.md).

The Otter Reusable Library provides a zero-friction, modular ecosystem for sharing and discovering Otter code templates (**Snippets**) and versioned packages (**Packs**), preserving complete backward compatibility with the canonical Otter 1.0.1 language specification.

---

## Architecture Overview

```
library/
├── schema/
│   ├── pack.schema.json         # JSON schema for otter.pack.json (RFC Section 5)
│   └── snippet.schema.json      # JSON schema for snippet definitions
├── snippets/
│   └── catalog.json             # 24 Curated, validated Otter snippets across 8 categories
├── packs/
│   └── otter-official/
│       ├── math-utils/          # clamp, lerp, mapRange, minVal, maxVal, calcAverage
│       ├── text-utils/          # slugify, padLeft, padRight, repeatText
│       ├── list-utils/          # hasItem, dedupeList, countMatches, joinWithPrefix
│       ├── file-helpers/        # readOrFallback, writeSafe, readJsonObject, writeJsonObject
│       └── game-entity/         # Actor entity, damage calculation, healing, level progression
├── SNIPPETS.md                  # Developer catalog reference with complete templates & examples
└── README.md                    # This document
```

---

## 1. Non-Negotiable Language Compatibility

In accordance with Section 3 of `OTTER_REUSABLE_LIBRARY_RFC.md`:
1. **No new grammar in Phase 1:** All snippets and packs use standard Otter 1.0.1 syntax.
2. **`use "relative/path.ot"`:** The standard import mechanism is preserved without modification.
3. **Block termination:** The period `.` terminates blocks (`for each`, `if/otherwise`, `to ...`), never member access.
4. **Property access:** Idiomatic `property of object` syntax is strictly enforced.
5. **No foreign paradigms:** Avoids `new`, JavaScript-style `import`, or unapproved OOP method syntax.

---

## 2. Curated Reusable Snippets

The snippet catalog in [library/snippets/catalog.json](file:///Users/jeffreymacy/Projects/otter-net/library/snippets/catalog.json) provides 24 curated snippets:

| Category | Snippets |
|---|---|
| **Control Flow** | `cf-for-each`, `cf-count-range`, `cf-while-loop`, `cf-if-otherwise`, `cf-try-otherwise` |
| **Text & Strings** | `text-slugify`, `text-pad-left`, `text-split-join`, `text-replace-all` |
| **Math & Numbers** | `math-clamp`, `math-lerp`, `math-map-range`, `math-calc-average` |
| **Collections & Lists** | `list-contains-check`, `list-deduplicate`, `list-filter-items` |
| **Files & I/O** | `file-read-safe`, `file-write-text`, `file-json-storage` |
| **Data & Records** | `data-custom-type`, `data-object-thing`, `db-sqlite-query` |
| **Game & Simulation** | `game-actor-damage` |
| **Web & Network** | `web-http-json-response` |

Full reference and templates are available in [library/SNIPPETS.md](file:///Users/jeffreymacy/Projects/otter-net/library/SNIPPETS.md).

---

## 3. Official Source Packs (Phase 1)

All packs are located under `library/packs/otter-official/` and contain an `otter.pack.json` manifest, standard `src/main.ot` entry point, `examples/demo.ot`, and `tests/test.ot`:

1. **`math-utils`**: Essential mathematical utilities (`clamp`, `lerp`, `mapRange`, `minVal`, `maxVal`, `calcAverage`).
2. **`text-utils`**: Text manipulation and formatting (`slugify`, `padLeft`, `padRight`, `repeatText`).
3. **`list-utils`**: Collection inspection and filtering (`hasItem`, `dedupeList`, `countMatches`, `joinWithPrefix`).
4. **`file-helpers`**: Resilient file I/O and JSON storage (`readOrFallback`, `writeSafe`, `readJsonObject`, `writeJsonObject`).
5. **`game-entity`**: RPG/game state mechanics (`Actor` definition, `applyDamage`, `restoreHealth`, `levelUp`).

---

## 4. CLI & Management Tooling

Use [tools/otter-library.ps1](file:///Users/jeffreymacy/Projects/otter-net/tools/otter-library.ps1) to search, inspect, insert snippets, or install packs:

```bash
# Natural-language search across snippets and packs
pwsh tools/otter-library.ps1 search "for loop"
pwsh tools/otter-library.ps1 search "enemy damage"
pwsh tools/otter-library.ps1 search "sql query"

# List all snippets by category
pwsh tools/otter-library.ps1 list

# Show detailed snippet info and preview
pwsh tools/otter-library.ps1 show math-clamp

# Insert a snippet into target file
pwsh tools/otter-library.ps1 insert math-clamp src/app.ot

# Install a pack locally into .otter/packages and generate otter.lock
pwsh tools/otter-library.ps1 install math-utils
```

---

## 5. Automated Verification

Automated test harnesses run every snippet example and pack test suite through the native Otter engine:

```bash
# Run snippet validation suite
pwsh tools/test-snippets.ps1

# Run pack test suite
pwsh tools/test-packs.ps1
```
