# Otter Component Authoring Guide (Phase 11 Milestone 4)

This guide is a practical, end-to-end tutorial for authoring, validating, testing, packaging, and composing reusable third-party **Otter Source Packs** using **Component Contract Schema v2** and the **Deterministic Offline Composition Planner** in Otter 1.0.1.

Every file, manifest, workflow, and CLI command in this tutorial is continuously verified by [`tests/docs/test_component_authoring_guide.py`](../tests/docs/test_component_authoring_guide.py) against a freshly packaged Otter distribution outside the repository.

---

## Prerequisites & Core Concepts

- **Frozen Otter 1.0.1 Compatibility:** Packs are plain Otter 1.0.1 `.ot` source files—no new keywords, no custom syntax, and no runtime cloud/AI dependencies.
- **Dynamic Scoping Isolation:** In Otter 1.0.1, variable assignments inside a function resolve against caller scope. Every Schema v2 Pack must declare a unique `variablePrefix` matching `^_[a-z0-9]{2,4}_$` and prefix all internal local variables with it.
  - Reserved orchestrator prefix: `_orch_`
  - Official pack prefixes: `_mu_` (`math-utils`), `_tu_` (`text-utils`), `_lu_` (`list-utils`), `_fh_` (`file-helpers`), `_ge_` (`game-entity`)
  - Our third-party pack in this tutorial will use **`_bg_`** (`acme-tools/budget-guard`).
- **Structural vs. Behavioral Validation:** `otter pack verify --contracts` performs offline **structural AST verification** (manifest schema, exported function existence, parameter count, parameter names, `use` dependency declarations, and `variablePrefix` enforcement). Declared return types, side effects, and error modes are machine-readable contract specifications for composition planning and must be verified with behavioral tests.

---

## Step 1 — Create a New Pack Directory Layout

Create a local library workspace outside the repository (or inside `~/.otter/library`) seeded with the standard library catalog (`snippets/` and `packs/otter-official/`), then scaffold your third-party Pack under `packs/<publisher>/<name>/`:

```bash
mkdir -p my-otter-library/packs/acme-tools/budget-guard/{src,tests,examples}
```

Required and recommended layout:

```text
my-otter-library/
├── snippets/catalog.json              # Required for Otter library root discovery
└── packs/
    ├── otter-official/                # Official Schema v2 packs (copied from bundled library)
    └── acme-tools/
        └── budget-guard/
            ├── otter.pack.json        # Schema v2 component contract manifest
            ├── src/
            │   └── main.ot            # Public exported Otter 1.0.1 operations
            ├── tests/
            │   └── test.ot            # Behavioral + scoping isolation assertions
            └── examples/
                └── demo.ot            # Standalone runnable usage demo
```

Naming rules enforced by `otter pack verify --contracts`:
- `publisher` and `name`: lowercase kebab-case (`^[a-z0-9]+(-[a-z0-9]+)*$`).
- `version`: valid SemVer (`1.0.0`).
- `entry`: relative path under `src/` ending in `.ot` (e.g., `src/main.ot`), no `..` traversal.

---

## Step 2 — Write Otter 1.0.1 Functions with Safe Variable Prefixes

Save the following implementation to `packs/acme-tools/budget-guard/src/main.ot`.

Notice two important Otter 1.0.1 authoring patterns:
1. **Prefix every internal local variable** with `_bg_` (`_bg_clamped`, `_bg_pct`, `_bg_ok`, `_bg_remaining`, `_bg_summary`). Object field names inside `_bg_summary has ...` are properties on the created `thing`, not caller-scope variables.
2. **Use explicit statement-call syntax (`fn arg1 arg2 into _bg_var`)** when calling operations provided by a transitive dependency (`otter-official/math-utils::clamp` and `calcPercentage`). Because `src/main.ot` is parsed standalone during offline contract validation without hardcoding relative `use` paths to another pack, `fn ... into target` lets the Otter 1.0.1 parser recognize external calls unambiguously.

<!-- doc-extract: packs/acme-tools/budget-guard/src/main.ot -->
```otter
# acme-tools/budget-guard - Reusable Budget Threshold & Reporting Pack
# Depends on otter-official/math-utils (clamp, calcPercentage)
# Internal variables use '_bg_' prefix for Otter 1.0.1 dynamic scoping safety.

to calcUtilizationPercent spentAmount budgetLimit
    if budgetLimit is at most 0
        return 0
    .
    clamp spentAmount 0 budgetLimit into _bg_clamped
    calcPercentage _bg_clamped budgetLimit into _bg_pct
    return _bg_pct
.

to isWithinBudget spentAmount budgetLimit
    if budgetLimit is at most 0
        return false
    .
    if spentAmount is greater than budgetLimit
        return false
    .
    return true
.

to buildBudgetSummary label spentAmount budgetLimit
    calcUtilizationPercent spentAmount budgetLimit into _bg_pct
    isWithinBudget spentAmount budgetLimit into _bg_ok
    _bg_rawRem is budgetLimit minus spentAmount
    clamp _bg_rawRem 0 budgetLimit into _bg_remaining
    _bg_summary has
        department is label
        spent is spentAmount
        limit is budgetLimit
        remaining is _bg_remaining
        utilizationPercent is _bg_pct
        withinBudget is _bg_ok
    .
    return _bg_summary
.
```

---

## Step 3 & Step 6 — Declare Schema v2 Operation Contracts and Pack Dependencies

Save the Schema v2 manifest to `packs/acme-tools/budget-guard/otter.pack.json`.

Key contract fields:
- `"schemaVersion": 2` enables full contract verification and composition planning.
- `"variablePrefix": "_bg_"` guarantees scoping isolation and allows the planner to verify prefix disjointness across composed packs.
- `"provides.operations"` must match every exported function's exact **name**, **parameter count**, and **parameter names** in `src/main.ot`.
- `"requires.packages"` (and `"dependencies"` for backward-compatible tooling) declares the dependency on `"otter-official/math-utils": "^1.0.1"`. When any workflow uses `acme-tools/budget-guard`, the composition planner automatically resolves `otter-official/math-utils` transitively and emits its `use` import first.
- `"sideEffects"` and `"errorMode"` must be internally consistent (e.g., do not combine `"none"` with active side effects, and do not declare `"returns-false"` on an operation whose return type is `"number"`).

<!-- doc-extract: packs/acme-tools/budget-guard/otter.pack.json -->
```json
{
  "schemaVersion": 2,
  "name": "budget-guard",
  "publisher": "acme-tools",
  "version": "1.0.0",
  "kind": "pack",
  "description": "Reusable budget threshold evaluation, utilization percentage, and structured summary reporting.",
  "entry": "src/main.ot",
  "license": "MIT",
  "requiresOtter": ">=1.0.1",
  "platforms": [
    "console",
    "desktop",
    "web"
  ],
  "variablePrefix": "_bg_",
  "provides": {
    "capabilities": [
      "finance.budget",
      "finance.reporting"
    ],
    "operations": [
      {
        "name": "calcUtilizationPercent",
        "description": "Clamps spentAmount into [0, budgetLimit] and computes utilization percentage (0 to 100).",
        "parameters": [
          { "name": "spentAmount", "type": "number" },
          { "name": "budgetLimit", "type": "number" }
        ],
        "returns": { "type": "number" },
        "sideEffects": ["none"],
        "errorMode": "returns-fallback"
      },
      {
        "name": "isWithinBudget",
        "description": "Returns true when budgetLimit is positive and spentAmount does not exceed budgetLimit.",
        "parameters": [
          { "name": "spentAmount", "type": "number" },
          { "name": "budgetLimit", "type": "number" }
        ],
        "returns": { "type": "boolean" },
        "sideEffects": ["none"],
        "errorMode": "returns-false"
      },
      {
        "name": "buildBudgetSummary",
        "description": "Builds a structured JSON-compatible thing summarizing budget spend, remaining balance, and status.",
        "parameters": [
          { "name": "label", "type": "text" },
          { "name": "spentAmount", "type": "number" },
          { "name": "budgetLimit", "type": "number" }
        ],
        "returns": { "type": "thing" },
        "sideEffects": ["none"],
        "errorMode": "returns-fallback"
      }
    ]
  },
  "requires": {
    "capabilities": [],
    "packages": {
      "otter-official/math-utils": "^1.0.1"
    }
  },
  "dependencies": {
    "otter-official/math-utils": "^1.0.1"
  },
  "tags": [
    "finance",
    "budget",
    "threshold",
    "reporting"
  ],
  "examples": [
    "examples/demo.ot"
  ]
}
```

---

## Step 4 — Write Behavioral Tests, Scoping Isolation Tests, and Examples

### 4.1 Behavioral & Caller-Scope Isolation Tests (`tests/test.ot`)

Good Pack tests verify both functional behavior (normal paths + boundary conditions) and Otter 1.0.1 caller-scope preservation. Save to `packs/acme-tools/budget-guard/tests/test.ot`:

<!-- doc-extract: packs/acme-tools/budget-guard/tests/test.ot -->
```otter
# Behavioral & scoping isolation tests for acme-tools/budget-guard
use "../../../otter-official/math-utils/src/main.ot"
use "../src/main.ot"

say "Running acme-tools/budget-guard behavioral tests..."

# Pre-populate common caller variables to prove zero scoping leaks
result is "CALLER_RESULT"
total is 12345
summary is "CALLER_SUMMARY"
remaining is 99999
pct is 55555

# 1. calcUtilizationPercent (normal, over-budget clamp, invalid limit)
u1 is calcUtilizationPercent 750 1000
if u1 is not 75
    fail with "calcUtilizationPercent failed: expected 75"
.
u2 is calcUtilizationPercent 1500 1000
if u2 is not 100
    fail with "calcUtilizationPercent clamp failed: expected 100"
.
u3 is calcUtilizationPercent 50 0
if u3 is not 0
    fail with "calcUtilizationPercent zero-limit failed: expected 0"
.

# 2. isWithinBudget
if isWithinBudget 750 1000 is not true
    fail with "isWithinBudget failed: expected true"
.
if isWithinBudget 1200 1000 is not false
    fail with "isWithinBudget over-limit failed: expected false"
.
if isWithinBudget 0 0 is not false
    fail with "isWithinBudget zero-limit failed: expected false"
.

# 3. buildBudgetSummary
rep is buildBudgetSummary "Engineering" 750 1000
if department of rep is not "Engineering"
    fail with "buildBudgetSummary failed: department mismatch"
.
if remaining of rep is not 250
    fail with "buildBudgetSummary failed: expected remaining 250"
.
if utilizationPercent of rep is not 75
    fail with "buildBudgetSummary failed: expected utilizationPercent 75"
.
if withinBudget of rep is not true
    fail with "buildBudgetSummary failed: expected withinBudget true"
.

# 4. Assert caller variables remained untouched
if result is not "CALLER_RESULT"
    fail with "SCOPING LEAK: caller 'result' modified"
.
if total is not 12345
    fail with "SCOPING LEAK: caller 'total' modified"
.
if summary is not "CALLER_SUMMARY"
    fail with "SCOPING LEAK: caller 'summary' modified"
.
if remaining is not 99999
    fail with "SCOPING LEAK: caller 'remaining' modified"
.
if pct is not 55555
    fail with "SCOPING LEAK: caller 'pct' modified"
.

say "All acme-tools/budget-guard tests passed!"
```

### 4.2 Standalone Example (`examples/demo.ot`)

Save to `packs/acme-tools/budget-guard/examples/demo.ot`:

<!-- doc-extract: packs/acme-tools/budget-guard/examples/demo.ot -->
```otter
# Demo for acme-tools/budget-guard
use "../../../otter-official/math-utils/src/main.ot"
use "../src/main.ot"

report is buildBudgetSummary "Platform Infra" 820 1000
say "Department:" department of report
say "Utilization (%):" utilizationPercent of report
say "Remaining:" remaining of report
say "Within Budget:" withinBudget of report
```

Run both directly with the Otter CLI:

```bash
otter run my-otter-library/packs/acme-tools/budget-guard/tests/test.ot
otter run my-otter-library/packs/acme-tools/budget-guard/examples/demo.ot
```

### 4.3 Negative Tests (Verifying Contract Diagnostics)

Before publishing a Pack, verify that contract violations are caught deterministically. Common failure cases and their exact CLI diagnostic codes:

| Violation | Expected Exit Code | Diagnostic Code |
| :--- | :---: | :--- |
| Unprefixed local variable (e.g., `temp is 1` inside a `_bg_` pack) | `2` | `VARIABLE_COLLISION_RISK` |
| Contract parameter name differs from `src/main.ot` AST parameter | `2` | `PARAMETER_NAME_MISMATCH` |
| Operation declared in `otter.pack.json` but missing in `src/main.ot` | `2` | `MISSING_FUNCTION` |
| `"errorMode": "returns-false"` on a `"number"` return type | `2` | `MISLEADING_CONTRACT` |
| Two composed packs share the same `variablePrefix` (or use `_orch_`) | non-zero | `PREFIX_COLLISION` |

Example broken source that triggers `VARIABLE_COLLISION_RISK` (unprefixed `temp` local variable):

<!-- doc-extract: negative/unprefixed_main.ot -->
```otter
to calcUtilizationPercent spentAmount budgetLimit
    temp is spentAmount
    return temp
.
```

---

## Step 5 — Validate and Inspect Contracts with the Otter CLI

Run structural AST validation and inspect the public contract surface:

```bash
# 1. Human-readable contract verification
otter pack verify my-otter-library/packs/acme-tools/budget-guard --contracts

# 2. Machine-readable JSON verification (for CI pipelines)
otter pack verify my-otter-library/packs/acme-tools/budget-guard --contracts --json

# 3. Inspect exported operations, types, variablePrefix, and validation scope
otter pack contract my-otter-library/packs/acme-tools/budget-guard
```

Expected highlights:
- `Contract: [OK] (3 operations verified)`
- `Variable Prefix: _bg_`
- `Validation Scope:   Structural AST` (explicitly noting that return types, side effects, and error modes are declared specifications).

---

## Step 7 — Create a Declarative Multi-Pack Workflow

Create `workflows/budget-report.workflow.json` composing your third-party Pack (`acme-tools/budget-guard`) with official Packs (`otter-official/text-utils` and `otter-official/file-helpers`).

Notice that `otter-official/math-utils` does **not** need to be invoked directly in `steps`: because `acme-tools/budget-guard` declares `"otter-official/math-utils": "^1.0.1"` in `requires.packages`, the planner automatically adds it to `composedPacks`, validates its prefix (`_mu_`), and emits its `use` import before `acme-tools/budget-guard`.

<!-- doc-extract: workflows/budget-report.workflow.json -->
```json
{
  "workflowVersion": 1,
  "id": "budget-report-pipeline",
  "name": "Third-Party Budget Guard Reporting Workflow",
  "description": "Normalizes a department label with text-utils, computes a budget summary and utilization percent with acme-tools/budget-guard (transitively using math-utils), persists JSON via file-helpers, and formats utilization via number_to_text.",
  "target": "console",
  "composedPacks": [
    "otter-official/text-utils",
    "acme-tools/budget-guard",
    "otter-official/file-helpers"
  ],
  "pipelineInputs": [
    {
      "name": "rawDepartment",
      "type": "text",
      "default": "   Engineering   Cloud   ",
      "description": "Unnormalized department name"
    },
    {
      "name": "spentAmount",
      "type": "number",
      "default": 750,
      "description": "Current spend amount"
    },
    {
      "name": "budgetLimit",
      "type": "number",
      "default": 1000,
      "description": "Maximum allowed budget"
    },
    {
      "name": "reportFile",
      "type": "text",
      "default": "budget_report.json",
      "description": "Output JSON report path"
    }
  ],
  "steps": [
    {
      "id": "step1_clean_label",
      "pack": "otter-official/text-utils",
      "operation": "normalizeSpaces",
      "inputs": {
        "text": { "fromInput": "rawDepartment" }
      }
    },
    {
      "id": "step2_summary",
      "pack": "acme-tools/budget-guard",
      "operation": "buildBudgetSummary",
      "inputs": {
        "label": { "fromStep": "step1_clean_label", "port": "returns" },
        "spentAmount": { "fromInput": "spentAmount" },
        "budgetLimit": { "fromInput": "budgetLimit" }
      }
    },
    {
      "id": "step3_save",
      "pack": "otter-official/file-helpers",
      "operation": "writeJsonObject",
      "inputs": {
        "filePath": { "fromInput": "reportFile" },
        "obj": { "fromStep": "step2_summary", "port": "returns" }
      },
      "orderAfter": [
        "step2_summary"
      ]
    },
    {
      "id": "step4_reload",
      "pack": "otter-official/file-helpers",
      "operation": "readJsonObjectSafe",
      "inputs": {
        "filePath": { "fromInput": "reportFile" },
        "fallbackValue": { "value": null }
      },
      "orderAfter": [
        "step3_save"
      ]
    },
    {
      "id": "step5_util_pct",
      "pack": "acme-tools/budget-guard",
      "operation": "calcUtilizationPercent",
      "inputs": {
        "spentAmount": { "fromInput": "spentAmount" },
        "budgetLimit": { "fromInput": "budgetLimit" }
      }
    },
    {
      "id": "step6_format_pct",
      "pack": "otter-official/text-utils",
      "operation": "padLeft",
      "inputs": {
        "text": {
          "fromStep": "step5_util_pct",
          "port": "returns",
          "adapter": "number_to_text"
        },
        "targetLen": { "value": 4 },
        "padChar": { "value": "0" }
      }
    }
  ],
  "output": {
    "fromStep": "step6_format_pct",
    "port": "returns"
  }
}
```

### Available Verified Built-In Adapters

When connecting ports of different types, specify one of the verified native Otter 1.0.1 adapters:

| Adapter ID | From Type | To Type | Generated Otter 1.0.1 Statements |
| :--- | :--- | :--- | :--- |
| `number_to_text` | `number` | `text` | `convert <src> to json into <tgt>` |
| `boolean_to_text` | `boolean` | `text` | `convert <src> to json into <tgt>` |
| `list_to_text` | `list` | `text` | `convert <src> to json into <tgt>` |
| `text_to_json_object` | `text` | `thing` | `convert <src> from json into <tgt>` |
| `json_object_to_text` | `thing` | `text` | `convert <src> to json into <tgt>` |
| `json_object_wrap_slug` | `text` | `thing` | `<tgt> is a thing` + `slug of <tgt> is <src>` |
| `json_object_wrap_value` | `any` | `thing` | `<tgt> is a thing` + `value of <tgt> is <src>` |

---

## Step 8 — Preview, Explain, and Apply the Composition

Initialize a clean consumer project outside the repository, point `OTTER_LIBRARY_PATH` at `my-otter-library` (or place the library at `~/.otter/library`), inspect the DAG and synthesized Otter source, and apply transactionally:

```bash
export OTTER_LIBRARY_PATH="$PWD/my-otter-library"

# Create a fresh console project
otter new console budget-demo
cd budget-demo

# 1. Explain participating contracts, topological DAG, Mermaid diagram, and scoping analysis
otter compose plan --workflow ../workflows/budget-report.workflow.json --explain

# 2. Preview synthesized Otter 1.0.1 orchestrator code before writing any files
otter compose plan --workflow ../workflows/budget-report.workflow.json --preview

# 3. Apply transactionally (installs all 4 packs, updates otter.lock, writes src/main.ot)
otter compose plan --workflow ../workflows/budget-report.workflow.json --apply --yes
```

---

## Step 9 — Verify and Run the Generated Application

Validate syntax, verify every installed pack contract in `otter.lock`, and run the composed Otter program:

```bash
# Static syntax check on generated source and resolved pack imports
otter check src/main.ot

# Verify SHA-256 lockfile digests AND Schema v2 contracts for all installed packs
otter pack verify --contracts

# Execute the composed application
otter run src/main.ot
```

Expected results:
- `otter run src/main.ot` prints `Pipeline execution complete. Result:` followed by `0075`.
- `budget_report.json` is written to disk containing:
  ```json
  {
    "department": "Engineering Cloud",
    "spent": 750,
    "limit": 1000,
    "remaining": 250,
    "utilizationPercent": 75,
    "withinBudget": true
  }
  ```

---

## Step 10 — Package and Consume Your Pack Outside the Repository

### 10.1 Package into an `.otterpack` Archive & Dry-Run Publish

`otter pack pack` validates your manifest, checks `.ot` syntax, scans for forbidden binaries, **executes every test under `tests/`**, creates a versioned `.otterpack` archive, and prints its SHA-256 digest:

```bash
otter pack pack my-otter-library/packs/acme-tools/budget-guard \
  --output dist/acme-tools-budget-guard-1.0.0.otterpack

otter pack publish dist/acme-tools-budget-guard-1.0.0.otterpack --dry-run
```

### 10.2 Library Discovery Precedence & Consuming Without `OTTER_LIBRARY_PATH`

Otter resolves libraries in deterministic 5-tier precedence:
1. `OTTER_LIBRARY_PATH` environment variable
2. Project-local `library/` walking up parent directories
3. User library at `~/.otter/library` (or `~/.local/share/otter/library`)
4. Bundled `library/` next to the installed `otter` executable
5. System-wide installation directory

To make `acme-tools/budget-guard` available to every project on your machine without setting `OTTER_LIBRARY_PATH`, install your library overlay into `~/.otter/library` (keeping `snippets/catalog.json` and `packs/otter-official/` intact), then install and verify directly in any standalone project:

```bash
# In any fresh project outside the repository (with ~/.otter/library populated)
otter new console manual-consumer
cd manual-consumer

otter pack install otter-official/math-utils
otter pack install acme-tools/budget-guard
otter pack verify --contracts
```

Then import the pinned installed files in ordinary Otter 1.0.1 source (`src/app.ot`):

<!-- doc-extract: consumer/app.ot -->
```otter
use "../.otter/packages/otter-official/math-utils/1.0.1/src/main.ot"
use "../.otter/packages/acme-tools/budget-guard/1.0.0/src/main.ot"

summary is buildBudgetSummary "Operations" 600 1000
say "Manual Consumer Utilization:" utilizationPercent of summary
say "Manual Consumer Remaining:" remaining of summary
```

Run it with:

```bash
otter check src/app.ot
otter run src/app.ot
```

---

## Pre-Submission Authoring Checklist

- [ ] `schemaVersion` is `2` and `otter.pack.json` matches [`library/schema/component-contract.schema.json`](../library/schema/component-contract.schema.json).
- [ ] `variablePrefix` matches `^_[a-z0-9]{2,4}_$`, is unique (never `_orch_` or an official prefix), and prefixes every internal local variable in `src/main.ot`.
- [ ] `otter pack verify <pack-dir> --contracts` exits `0` with zero diagnostics.
- [ ] `tests/test.ot` covers both normal/edge behaviors and caller-scope variable preservation.
- [ ] All external pack dependencies are listed in `requires.packages` (and `dependencies`), and cross-pack calls use `fn arg1 ... into _pfx_res`.
- [ ] `platforms` accurately reflects runtime support (omit `"web"` if your Pack requires filesystem or native OS I/O).
- [ ] `otter pack pack <pack-dir>` and `otter pack publish <archive> --dry-run` succeed cleanly.
- [ ] A declarative multi-pack workflow applies, passes `otter check`, passes `otter pack verify --contracts`, and produces expected outputs outside the repository.
