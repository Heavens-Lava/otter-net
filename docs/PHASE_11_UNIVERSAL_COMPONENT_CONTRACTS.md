# Phase 11 Architecture Proposal: Universal Reusable Component Contracts & Composition Planner

**Author:** Otter Core Engineering Team  
**Status:** Proposal / Draft for Review  
**Target Milestone:** Phase 11  
**Repository:** `Heavens-Lava/otter-net`  
**Prerequisite:** PR #13 (`feature/trustworthy-composition`, commit `ab0def3`)  

---

## Executive Summary

Otter 1.0.1 provides an exact compatibility port of the frozen PowerShell reference language alongside a foundational library of 5 official Packs, 4 Blueprints, 4 Recipes, and 38 Snippets. In PR #13, we established a trustworthy composition engine featuring:
- Path containment and symbolic-link defense.
- Isolated two-phase staging and byte-for-byte verified compensating rollback.
- Deterministic failure injection testing (recovering from mid-commit failures and post-pack-install interruptions).
- Honest capability discovery reporting.

However, existing reusable components remain limited in composability:
1. Components are wired together primarily through hardcoded application Recipes rather than dynamic capability matching.
2. Manifests declare broad tags (`"capabilities": ["files"]`) rather than formal machine-readable contracts detailing operation signatures, parameter types, return values, side effects, and required interfaces.
3. Otter 1.0.1's caller-scope dynamic scoping necessitates manual variable prefixing (`_ge_`, `_mu_`, `_tu_`, `_lu_`, `_fh_`) to prevent caller variable corruption.

**Phase 11 Goal:** Establish **Universal Machine-Readable Component Contracts** and a **Deterministic Offline Composition Planner** that enables developers to compose arbitrary independent Packs, Blueprints, and custom user components into new software systems without requiring pre-baked Recipes or cloud/AI dependencies.

---

## PART 1 — Comprehensive Audit of Existing Building Blocks

### 1.1 Official Source Packs (5 Packs)

| Component & Version | Purpose & Capabilities | Public Functions / API | Inputs & Outputs | Dependencies | Platforms | Known Limitations | Test Coverage | Reusability & Composability |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **`otter-official/math-utils`**<br>`v1.0.1` | Core numeric operations, boundary limits, and interpolation. | `clamp`<br>`lerp`<br>`mapRange`<br>`minVal`<br>`maxVal`<br>`calcAverage`<br>`calcSum`<br>`calcPercentage`<br>`roundVal`<br>`roundUpVal`<br>`roundDownVal` | **Inputs:** `number` parameters.<br>**Outputs:** `number` scalar return. | None | `console`, `desktop`, `web` | Lacks arbitrary-precision math. Division by zero in `mapRange` returns `outMin`. | 100% automated test + mutation coverage (17/17). | **High.** Completely stateless, zero side effects, composable across all domains. |
| **`otter-official/text-utils`**<br>`v1.0.1` | Text manipulation, string formatting, padding, and slugification. | `repeatText`<br>`padLeft`<br>`padRight`<br>`normalizeSpaces`<br>`slugify`<br>`toUpper`<br>`toLower`<br>`isEqualIgnoreCase`<br>`isNonEmpty`<br>`replaceText`<br>`containsText`<br>`startsWithText`<br>`endsWithText`<br>`splitText`<br>`joinText` | **Inputs:** `text`, `targetLen`, `delimiter`.<br>**Outputs:** `text`, `list` (for split), `boolean`. | None | `console`, `desktop`, `web` | `slugify` uses ASCII character replacements; does not handle Unicode diacritics. | 100% automated test + mutation coverage. | **High.** Pure utility functions; cleanly composable with UI and storage. |
| **`otter-official/list-utils`**<br>`v1.0.1` | Collection utilities: filtering, deduplication, counting, searching, slicing. | `hasItem`<br>`dedupeList`<br>`countMatches`<br>`joinWithPrefix`<br>`removeItemFromList`<br>`findFirstMatch`<br>`sortList`<br>`reverseList`<br>`calcListSum`<br>`sliceList` | **Inputs:** `list`, `target`, `prefix`, `indices`.<br>**Outputs:** `boolean`, `list`, `number`, `any` (or `gone`). | None | `console`, `desktop`, `web` | In-memory operations only. Sort relies on Otter native string/number ordering. | 100% automated test + mutation coverage. | **High.** Operates on native Otter lists; composable across data layers. |
| **`otter-official/file-helpers`**<br>`v1.0.1` | Safe filesystem I/O, error-trapping file reads, and JSON serialization. | `fileExistsSafe`<br>`readOrFallback`<br>`writeSafe`<br>`appendSafe`<br>`deleteFileSafe`<br>`toJsonSafe`<br>`fromJsonSafe`<br>`readJsonObject`<br>`readJsonObjectSafe`<br>`writeJsonObject` | **Inputs:** `filePath` (text), `content` (text/thing), `fallbackValue`.<br>**Outputs:** `boolean`, `text`, `thing` (or `gone`). | None | `console`, `desktop` | Unavailable in sandboxed Web runtime (`Otter.Web`). Does not support binary stream I/O. | 100% automated test + mutation coverage. | **High.** Standard persistence provider for CLI and desktop apps. |
| **`otter-official/game-entity`**<br>`v1.1.0` | Actor state, RPG stats, inventory containers, equipment slots, cooldowns, and persistence. | `createActor`<br>`createItem`<br>`createStackableItem`<br>`createInventory`<br>`createEquipment`<br>`createCooldown`<br>`applyDamage`<br>`healActor`<br>`reviveActor`<br>`gainExperience`<br>`addItemToInventory`<br>`consumeItemFromInventory`<br>`equipItem`<br>`unequipItem`<br>`useSkill`<br>`saveActorToFile`<br>`loadActorFromFile` | **Inputs:** Object instances (`Actor`, `InventoryItem`, `Equipment`), primitives.<br>**Outputs:** Mutated instances, `boolean` status, `number`. | `otter-official/math-utils` | `console`, `desktop`, `web` | Large monolithic pack mixing domain data structures (`Actor`) with file I/O. Uses dynamic scoping prefixes (`_ge_`). | 100% automated test + mutation coverage. | **Medium.** Excellent for games; slightly coupled to file I/O for state saving. |

---

### 1.2 Reusable Blueprints (4 Blueprints)

| Blueprint & Version | Purpose | Included Packs | Inputs / Configurations | Outputs | Composability Evaluation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`otter-official/console-calculator`**<br>`v1.0.0` | Connects arithmetic evaluation with terminal input loop. | `math-utils` | Operation mode, operand precision. | Calculated result printed to stdout. | **Low.** Designed as a complete terminal utility rather than an embeddable component. |
| **`otter-official/file-settings`**<br>`v1.0.0` | Configuration file loader with default schema fallback. | `file-helpers` | `configPath`, default settings object. | Validated in-memory configuration `thing`. | **High.** Clean integration pattern for any application requiring persistent settings. |
| **`otter-official/game-combat`**<br>`v1.0.0` | Turn-based actor combat loop with damage and reward calculation. | `game-entity`, `math-utils` | Player stats, enemy stats, turn timeout. | Combat victory/defeat boolean, XP delta. | **Medium.** Usable across game engines, but depends directly on `Actor` data shape. |
| **`otter-official/text-cleanup`**<br>`v1.0.0` | Batch text sanitizer, whitespace normalizer, and slugifier. | `text-utils` | Raw input text array, casing options. | Normalized text collection. | **High.** Can be inserted into ingestion or search pipelines. |

---

### 1.3 Application Recipes (4 Recipes)

| Recipe & Version | Target | Purpose | Composed Packs & Blueprints | Reusable Sub-elements | Composability Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`otter-official/contact-manager`**<br>`v1.0.0` | `desktop` | Directory with record validation and JSON persistence. | `file-helpers`, `text-utils` | Email validation, contact persistence. | End-to-end template; cannot currently be embedded as a sub-view. |
| **`otter-official/rpg-adventure`**<br>`v1.0.0` | `console` | Complete text RPG with dungeon exploration, combat, and saves. | `game-combat`, `game-entity`, `math-utils`, `file-helpers` | Save/load routine, inventory interaction. | Monolithic application demo; demonstrates composition of all 5 packs. |
| **`otter-official/task-tracker`**<br>`v1.0.0` | `console` | Todo task list with priorities, completion toggle, and savefile. | `file-settings`, `file-helpers`, `text-utils` | Priority sorting, task state serializer. | End-to-end application; useful reference for CRUD operations. |
| **`otter-official/web-portal`**<br>`v1.0.0` | `web` | HTTP microservice serving JSON endpoints and status routes. | `file-helpers`, `text-utils` | Route dispatcher, JSON response helper. | Couples HTTP server loop with template files; needs modular HTTP pack. |

---

### 1.4 Snippet Catalog (38 Snippets)

The 38 snippets in `library/snippets/` provide single-function templates across 11 domains:
- **Control Flow:** `cf-for-each`, `cf-count-range`, `cf-while-loop`, `cf-if-otherwise`, `cf-try-otherwise`
- **Text:** `text-slugify`, `text-pad-left`, `text-split-join`, `text-replace-all`
- **Math:** `math-clamp`, `math-lerp`, `math-map-range`, `math-calc-average`
- **Collections:** `list-contains-check`, `list-deduplicate`, `list-filter-items`, `list-sort-reverse`
- **Files & Storage:** `file-read-safe`, `file-write-text`, `file-json-storage`, `file-csv-parse`, `file-append-log`
- **Data Types:** `data-custom-type`, `data-object-thing`
- **Game Mechanics:** `game-actor-damage`, `game-cooldown-system`, `game-inventory-capacity`
- **Networking & Web:** `web-http-json-response`, `http-get-request`
- **Database:** `db-sqlite-query`, `db-sqlite-transaction`
- **Conversions & Dates:** `conv-number-format`, `conv-bytes-hex`, `date-diff-days`, `date-add-days`
- **Validation:** `val-email-check`, `val-numeric-range`
- **UI:** `ui-page-layout`

---

### 1.5 Identified Gaps, Duplication & Architectural Incompatibilities

1. **Semantic Duplication Between Snippets and Packs:**
   - `math-clamp` snippet duplicates `math-utils.clamp`.
   - `text-slugify` snippet duplicates `text-utils.slugify`.
   - `file-json-storage` snippet duplicates `file-helpers.readJsonObject` / `writeJsonObject`.
   - *Resolution:* Snippets should be classified as zero-dependency inline micro-utilities, while Packs represent versioned, pinned modules.
2. **Missing Operation Contracts in Manifests:**
   - Pack manifests declare `"capabilities": ["files"]` as loose string tags, but do not specify exported function names, parameter types, or return shapes.
   - The composition engine is forced to inspect `.ot` ASTs directly or hardcode function mappings.
3. **Dynamic Scoping Collision Risks:**
   - In Otter 1.0.1, variable declarations in called functions exist in the caller's scope.
   - If two different third-party packs use the same temporary variable name (e.g. `temp` or `result`), they corrupt each other's state. Official packs currently resolve this via manual prefixes (`_fh_`, `_ge_`), but there is no compiler or manifest contract enforcing this.
4. **Error Handling Divergence:**
   - Some functions return `gone` on error (`readJsonObject`).
   - Others return `false` on error (`writeSafe`).
   - Others return a caller-supplied `fallbackValue` (`readOrFallback`).
   - *Resolution:* Standard contracts must declare explicit error behaviors.

---

## PART 2 — Universal Component Contract Specification (Schema v2)

To enable automatic capability matching and type-safe composition, we extend `otter.pack.json` to **Component Contract Schema v2**. This schema is backward-compatible with v1 manifests.

### 2.1 Component Manifest Schema (`library/schema/component-contract.schema.json`)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "OtterComponentContract",
  "description": "Universal Machine-Readable Contract for Otter Reusable Building Blocks (Phase 11)",
  "type": "object",
  "required": [
    "schemaVersion",
    "name",
    "publisher",
    "version",
    "kind",
    "description",
    "entry",
    "license",
    "requiresOtter",
    "platforms",
    "provides",
    "requires"
  ],
  "properties": {
    "schemaVersion": { "type": "integer", "enum": [2] },
    "name": { "type": "string", "pattern": "^[a-z0-9]+(-[a-z0-9]+)*$" },
    "publisher": { "type": "string", "pattern": "^[a-z0-9]+(-[a-z0-9]+)*$" },
    "version": { "type": "string", "pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$" },
    "kind": { "type": "string", "enum": ["pack", "blueprint", "recipe"] },
    "description": { "type": "string" },
    "entry": { "type": "string", "pattern": "^src/.*\\.ot$" },
    "license": { "type": "string" },
    "requiresOtter": { "type": "string", "description": "Semver constraint on Otter runtime" },
    "platforms": {
      "type": "array",
      "items": { "type": "string", "enum": ["console", "desktop", "web", "all"] }
    },
    "variablePrefix": {
      "type": "string",
      "pattern": "^_[a-z0-9]{2,4}_$",
      "description": "Guaranteed namespace prefix for all internal variables to prevent caller-scope collisions in Otter 1.0.1"
    },
    "provides": {
      "type": "object",
      "description": "Capabilities and public operations exported by this component",
      "properties": {
        "capabilities": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Domain capabilities provided (e.g. 'persistence.json', 'game.entity')"
        },
        "operations": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name", "description", "parameters", "returns", "sideEffects", "errorMode"],
            "properties": {
              "name": { "type": "string" },
              "description": { "type": "string" },
              "parameters": {
                "type": "array",
                "items": {
                  "type": "object",
                  "required": ["name", "type"],
                  "properties": {
                    "name": { "type": "string" },
                    "type": { "type": "string", "enum": ["text", "number", "boolean", "list", "thing", "any", "object"] },
                    "objectType": { "type": "string" },
                    "optional": { "type": "boolean", "default": false },
                    "default": { "type": ["string", "number", "boolean", "null"] }
                  }
                }
              },
              "returns": {
                "type": "object",
                "required": ["type"],
                "properties": {
                  "type": { "type": "string", "enum": ["text", "number", "boolean", "list", "thing", "any", "object", "void"] },
                  "objectType": { "type": "string" },
                  "nullable": { "type": "boolean", "default": false }
                }
              },
              "sideEffects": {
                "type": "array",
                "items": { "type": "string", "enum": ["none", "filesystem:read", "filesystem:write", "network:http", "console:output", "state:mutation"] }
              },
              "errorMode": {
                "type": "string",
                "enum": ["returns-gone", "returns-false", "returns-fallback", "throws-error"]
              }
            }
          }
        },
        "types": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name", "fields"],
            "properties": {
              "name": { "type": "string" },
              "fields": {
                "type": "array",
                "items": {
                  "type": "object",
                  "required": ["name", "type"],
                  "properties": {
                    "name": { "type": "string" },
                    "type": { "type": "string" }
                  }
                }
              }
            }
          }
        }
      }
    },
    "requires": {
      "type": "object",
      "description": "Dependencies, runtime services, and host capabilities required",
      "properties": {
        "capabilities": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Required external capabilities (e.g. 'runtime.filesystem', 'data.validation')"
        },
        "packages": {
          "type": "object",
          "additionalProperties": { "type": "string" }
        }
      }
    },
    "contracts": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["operation", "inputFixture", "expectedOutput"],
        "properties": {
          "operation": { "type": "string" },
          "inputFixture": { "type": "array" },
          "expectedOutput": { "type": ["string", "number", "boolean", "object", "null"] }
        }
      }
    }
  }
}
```

---

## PART 3 — The Deterministic Composition Planner

```
+---------------------------------------------------------------------------------------------------------+
|                                     DETERMINISTIC COMPOSITION PIPELINE                                  |
+---------------------------------------------------------------------------------------------------------+
|  1. Intent Analysis   -->  Tokenize requested intent into domain requirements                           |
|  2. Capability DAG    -->  Expand requirements into required capabilities & data flow                   |
|  3. Library Matcher   -->  Query local component index for matching providers                           |
|  4. Contract Bindings -->  Verify parameter types, platform constraints & variable prefixes             |
|  5. Gap Detection     -->  Check for unsupported capabilities (honest abort if missing)                 |
|  6. Graph Generation  -->  Construct dependency DAG and topological execution order                     |
|  7. Code Synthesis    -->  Synthesize glue code connecting outputs -> inputs                           |
|  8. Static Validation -->  Run LanguageService AST check & type assertion                               |
|  9. Safe Apply        -->  Two-phase staging, sibling replacement, verified transactional commit         |
+---------------------------------------------------------------------------------------------------------+
```

### 3.1 Planner Pipeline Stages

1. **Intent Interpretation & Decomposition:**
   - Normalizes input query: extracts keywords, platform targets (`desktop`, `web`, `console`), and capability tokens (e.g. `"persistence"`, `"database"`, `"search"`, `"validation"`).
   - Generates a `RequestedCapabilitySet`.
2. **Catalog Index Query (100% Offline & Deterministic):**
   - Loads manifests from bundled standard library, user home library (`~/.otter/library`), and local project library.
   - Filters out components incompatible with target platform or runtime version (`requiresOtter`).
3. **Capability Resolution & Provider Selection:**
   - For each requested capability, selects components that satisfy the capability.
   - Prefers specific Packs over generic Snippets; prefers smaller, dedicated Packs over large monolithic recipes.
4. **Port Binding & Type Alignment:**
   - Matches operation output types to downstream operation parameter types.
   - If an output returns `thing` and a consumer requires `list`, planner inserts an adapter node (e.g. extracting a field or calling `splitText`).
5. **Collision & Scoping Safety Verification:**
   - Checks that all participating components have declared non-conflicting `variablePrefix` values, ensuring zero variable collisions under Otter 1.0.1 dynamic scoping.
6. **Code Synthesis & Source Preview Generation:**
   - Emits clean, idiomatic Otter 1.0.1 source code importing packs, initializing data structures, and connecting inputs to outputs.
   - Formats proposal into `composition-proposal.schema.json` with an explicit `capabilityCoverage` matrix.
7. **Two-Phase Staged Application:**
   - Staged in `.otter/.staging_<guid>`, validated with `otter check`, and committed via atomic sibling moves (`.tmp_<guid>`).

---

## PART 4 — Three Real-World Composition Case Studies

### Case Study 1: Business Application
**Goal:** Contact Management + Persistent Storage + Search + Data Validation.

```
+------------------------+      +------------------------+      +------------------------+
|   val-email-check      | ---> |  otter-official/       | ---> |  otter-official/       |
|   (Format assertion)   |      |  text-utils (Search)   |      |  file-helpers (Store)  |
+------------------------+      +------------------------+      +------------------------+
```

- **Reused Existing Components:**
  - `otter-official/file-helpers` (`readJsonObjectSafe`, `writeJsonObject`)
  - `otter-official/text-utils` (`containsText`, `isEqualIgnoreCase`, `normalizeSpaces`)
  - `val-email-check` (snippet)
- **Missing Capabilities in Repository:**
  - Structured schema record validation (e.g., telephone format, postal code, date boundaries).
  - Multi-field indexed query capability.
- **Runtime Primitives Needed:**
  - None required; fully expressible in Otter 1.0.1 source code.
- **Generated Composition Wiring:**
  ```otter
  # Composed Business Contact Manager
  use "otter-official/file-helpers"
  use "otter-official/text-utils"

  to addContact contactsList name email phone
      if not containsText email "@"
          return false
      .
      _cm_card is a thing
      name of _cm_card is normalizeSpaces name
      email of _cm_card is email
      phone of _cm_card is phone
      add _cm_card to contactsList
      writeJsonObject "contacts.json" contactsList
      return true
  .

  to searchContacts contactsList term
      _cm_matches are empty
      for each _cm_c in contactsList
          if containsText (name of _cm_c) term or containsText (email of _cm_c) term
              add _cm_c to _cm_matches
          .
      .
      return _cm_matches
  .
  ```

---

### Case Study 2: Game Development Application
**Goal:** Character Entities + Inventory Capacity + Quest State Machine + Persistence.

```
+------------------------+      +------------------------+      +------------------------+
|  otter-official/       | ---> | game-inventory-capacity| ---> |  otter-official/       |
|  game-entity (Actor)   |      | (Bag limits & stacks)  |      |  file-helpers (Save)   |
+------------------------+      +------------------------+      +------------------------+
```

- **Reused Existing Components:**
  - `otter-official/game-entity` (`createActor`, `applyDamage`, `healActor`, `createItem`)
  - `game-inventory-capacity` (snippet)
  - `otter-official/math-utils` (`clamp`, `calcAverage`)
  - `otter-official/file-helpers` (`writeJsonObject`, `readJsonObjectSafe`)
- **Missing Capabilities in Repository:**
  - Discrete Quest Graph / Finite State Machine (quests have prerequisites, stages, active/completed states).
  - Dialogue tree branch evaluator.
- **Runtime Primitives Needed:**
  - None; game state machines can be implemented as pure Otter record structures.
- **Generated Composition Wiring:**
  - Actor is created via `createActor`.
  - Inventory items are managed through `addItemToInventory` with capacity constraints from `game-inventory-capacity`.
  - State snapshot is serialized into JSON via `writeJsonObject`.

---

### Case Study 3: Web Microservice Application
**Goal:** HTTP Routing + Request Validation + Persistent Records + Structured JSON Responses.

```
+------------------------+      +------------------------+      +------------------------+
|  Otter.Web Host        | ---> |  otter-official/       | ---> |  otter-official/       |
|  (HTTP Ingestion)      |      |  text-utils (Validate) |      |  file-helpers (Store)  |
+------------------------+      +------------------------+      +------------------------+
```

- **Reused Existing Components:**
  - `web-http-json-response` (snippet)
  - `otter-official/text-utils` (`splitText`, `isNonEmpty`)
  - `otter-official/file-helpers` (`readJsonObjectSafe`, `writeJsonObject`)
- **Missing Capabilities in Repository:**
  - Reusable HTTP client and server abstraction pack (`otter-official/http-server`, `otter-official/http-client`).
  - URL query string and header parsing.
- **Runtime Primitives Needed:**
  - `Otter.Web` runtime host primitives already exist in `src/Otter.Web/`, but currently expose raw route callbacks. A dedicated `http-router` Pack is needed to provide higher-level middleware and parameter extraction.

---

## PART 5 — Prioritized Library Expansion Roadmap

We prioritize building **25 foundational, deeply interoperable Packs** across 11 key development areas. We explicitly distinguish between **pure Otter source libraries** and **native C# runtime integrations**.

### 5.1 Category Roadmap & Implementation Matrix

| Development Area | Planned Packs | Implementation Layer | Why It Matters |
| :--- | :--- | :--- | :--- |
| **1. Data & Persistence** | `sqlite-client`<br>`structured-records`<br>`file-csv`<br>`config-store` | **Hybrid:** C# runtime host binding for SQLite; Otter wrappers for CSV and config parsing. | Unlocks persistent database-backed apps, data pipelines, and CLI tools. |
| **2. Web & Networking** | `http-client`<br>`http-router`<br>`url-parser`<br>`json-rpc` | **Hybrid:** C# runtime `HttpClient` integration; Otter routing and request dispatchers. | Enables API consumption, microservices, and external webhook integrations. |
| **3. Collections & Algorithms** | `hash-map`<br>`priority-queue`<br>`set-utils` | **Pure Otter:** Uses native objects and lists with algorithmic indexers. | Essential for complex business logic, pathfinding, and graph evaluation. |
| **4. Validation & Types** | `schema-validator`<br>`type-assertions` | **Pure Otter:** Structural pattern matchers and regex assertions. | Ensures clean boundaries when composing untrusted external inputs. |
| **5. Desktop & UI Controls** | `ui-form-controls`<br>`ui-state-store` | **Hybrid:** `Otter.Desktop` native window host bindings with declarative Otter layouts. | Allows building rich interactive desktop tools without raw terminal loops. |
| **6. Games & Simulations** | `state-machine`<br>`quest-system`<br>`grid-map` | **Pure Otter:** Pure state tree and grid pathfinding algorithms. | Powers interactive simulations, RPGs, and turn-based games. |
| **7. Automation & CLI** | `cli-args`<br>`task-scheduler` | **Pure Otter:** Command-line flag parsing and cron-like timer loops. | Unlocks scriptable devops and automation workflows. |
| **8. Security & Cryptography** | `crypto-hash`<br>`token-auth` | **Hybrid:** C# host bindings to `System.Security.Cryptography`. | Password hashing, HMAC signatures, and session token generation. |
| **9. Logging & Diagnostics** | `structured-logger`<br>`metric-counters` | **Pure Otter:** Log levels, timestamp formatting, and metrics aggregation. | Production debugging and runtime telemetry. |
| **10. AI & Model Connectors** | `ai-prompt-builder`<br>`structured-output-parser` | **Pure Otter:** Schema-based prompt templates and JSON extraction. | Offline deterministic preparation of structured prompts and response validation. |
| **11. Data Visualization** | `ascii-charts`<br>`table-formatter` | **Pure Otter:** Terminal charts, histograms, and aligned ASCII tables. | Instant visual feedback for data analysis scripts. |

---

### 5.2 The First 12 Foundational Packs to Implement

We recommend implementing these first 12 components in Phase 12 based on immediate reuse potential, zero risk to frozen Otter 1.0.1 semantics, and high composability:

1. **`otter-official/cli-args` (Pure Otter):** POSIX command-line argument parser (`--flag`, `-o`, `--key=value`).
2. **`otter-official/schema-validator` (Pure Otter):** Field presence, type, range, and format validation.
3. **`otter-official/structured-logger` (Pure Otter):** Level-based logging (`DEBUG`, `INFO`, `WARN`, `ERROR`) with JSON output.
4. **`otter-official/file-csv` (Pure Otter):** Delimited text parsing and CSV serialization.
5. **`otter-official/hash-map` (Pure Otter):** Key-value dictionary abstraction with fast lookups.
6. **`otter-official/state-machine` (Pure Otter):** Deterministic Finite State Machine (FSM) for workflows, games, and UI state.
7. **`otter-official/http-client` (Hybrid):** GET/POST HTTP client wrapper over runtime primitives.
8. **`otter-official/sqlite-client` (Hybrid):** Embedded SQL query execution and parameterized statements.
9. **`otter-official/crypto-hash` (Hybrid):** SHA-256, SHA-512, and HMAC generation for data integrity.
10. **`otter-official/table-formatter` (Pure Otter):** Aligned terminal table rendering for CLI output.
11. **`otter-official/config-store` (Pure Otter):** Hierarchical settings manager merging defaults, files, and environment variables.
12. **`otter-official/quest-system` (Pure Otter):** Objective tracking, prerequisite trees, and completion hooks for simulation games.

---

## PART 6 — Recommended Phase 11 Implementation Milestones

To execute Phase 11 methodically without regression:

- **Milestone 1 — Component Contract Schema & Tooling:**
  - Ship `library/schema/component-contract.schema.json`.
  - Add `otter pack contract <id>` and `otter pack verify --contracts` commands to validate component interfaces against their manifests.
  - Upgrade the 5 official packs to Schema v2 with formal operation signatures.
- **Milestone 2 — Dynamic Multi-Component Composition Planner:**
  - Implement the capability dependency graph solver in `ComposeCommand.cs`.
  - Enable assembling multiple arbitrary Packs into a proposal without requiring a pre-existing Recipe manifest.
  - Implement port-to-port parameter binding and automatic adapter generation.
- **Milestone 3 — Verification Test Suite & Sandboxed Benchmarking:**
  - Build automated integration tests proving that combining arbitrary packs produces verified, executable, and safe Otter applications outside the repository.
  - Validate that Otter 1.0.1 dynamic scoping isolation remains unbroken across all generated compositions.
- **Milestone 4 — Developer Documentation & Component Authoring Guide:**
  - Publish `docs/COMPONENT_AUTHORING_GUIDE.md` teaching third-party developers how to author, test, and publish composable Otter Packs with machine-readable contracts.
