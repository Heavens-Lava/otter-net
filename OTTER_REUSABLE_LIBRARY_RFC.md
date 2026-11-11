# Otter Reusable Library — Feature RFC

Status: PROPOSED / not implemented  
Date: 2026-11-10  
Target: Otter language, native C# engine, Studio IDE, future website registry

## 1. Source of truth and reviewed repositories

- C# implementation: https://github.com/Heavens-Lava/otter-net (main)
- Reference implementation: https://github.com/Heavens-Lava/otterPS (master)
- Existing specification: `otterPS/rules.md`, `otterPS/docs/GRAMMAR.md`, `otterPS/SPEC-DECISIONS.md` (must be reviewed before language changes)
- C# entry points: `src/Otter.Language/Lexer.cs`, `Parser.cs`, `Syntax.cs`; `src/Otter.Cli/Modules.cs`; `src/Otter.Runtime/Interpreter.cs`, `Scope.cs`, `Values.cs`
- Studio: `otterPS/otter-studio/js/project/package-module-engine.js`, `js/language/otter-language-service.js`, `js/navigation/symbol-index.js`, `js/components/editor.js`

Important: The C# repository README states that `otter-net` must implement **the same language** as otterPS. Therefore a syntax change must be approved and specified in the canonical Otter language specification, then implemented and parity-tested in both engines. This RFC does not authorize editing frozen files.

## 2. Vision

Write once, reuse everywhere *that the declared platform capabilities permit*. Developers discover and reuse a small Snippet, a structured Pack, or a multi-Pack Kit from Otter Studio or the Otter website.

### Terms

- **Snippet**: insertable source template; not a new runtime type.
- **Pack**: a versioned distributable unit containing ordinary Otter source, metadata, examples, tests and optional assets. A Pack is **not** synonymous with a class/type.
- **Kit**: curated group of Packs and templates, installed as a unit.
- **Registry**: catalog and artifact distribution service, initially local-only, later online.
- **Capability**: permission/platform dependency, e.g. files, network, UI, database.

## 3. Non-negotiable compatibility

1. Preserve `use "relative/path.ot"`, including source-order expansion, deduplication and circular import diagnostics.
2. Preserve `a Person has ...` and `person is a Person` and `name of person`.
3. Never use `.` for member access. Period remains an optional block terminator, and decimals retain their existing lexical behavior.
4. Preserve `to name arguments` and `say`, `for each`, `is`, `of` and indentation semantics.
5. Do not silently introduce `pack Enemy`, `new Enemy`, `goblin.takeDamage(25)`, `end`, or JavaScript-style `import`: these are **not** current Otter grammar.
6. Existing `use` syntax remains the only import syntax in phase 1. Package-name imports require a separate approved grammar decision.
7. Runtime/compiler internals must provide general capabilities, not hardcoded enemy, login or app-specific behavior.
8. A feature is complete only after a real `.ot` program reaches it through the supported user-facing CLI or Studio path.

## 4. Recommended implementation phases

### Phase 0 — Audit and baseline

- Read canonical `SPEC-DECISIONS.md`, reserved words, module tests, and the latest parser and CLI code.
- Inventory existing `use` handling, user-defined types, scopes, Studio package manager, search and test architecture.
- Capture baseline conformance tests in both engines and avoid changes to the language at this stage.
- Write down which Studio package features actually work end-to-end, rather than assuming they are connected because a module exists.

### Phase 1 — Local Snippets and source Packs, **no new grammar**

- Add a versioned library manifest, local search index, install command and lockfile.
- Install each Pack into a deterministic project-local directory such as `.otter/packages/<publisher>/<name>/<version>/`.
- Resolve a manifest entry point to an existing Otter source file and load it through the existing `use "..."` mechanism.
- Keep the package manager separate from the parser; avoid rewriting imports until compatibility is proven.
- Add IDE search across snippets, manifests, descriptions, tags and example source; preview, insert snippet, install Pack and show dependency tree.
- Ship 10–20 curated Snippets and 3–5 real source Packs first. Start with arithmetic, text, loops, list utilities and file helpers; only ship enemy/game or login packs when required engine capabilities exist.

### Phase 2 — Named package imports (requires spec approval)

Proposed syntax (NOT VALIDATED):

```otter
use "community/enemy@1.2.0"
```

Alternative: keep `use "relative/path.ot"` and have the package manager generate a local alias/shim. Select one approach after assessing ambiguous paths, portability and parser behavior. Do not treat this syntax as existing functionality.

### Phase 3 — Richer reusable behavior (requires spec approval)

Existing Otter custom types contain properties. Before proposing member actions or events, decide whether composition of existing functions and types is sufficient. If new syntax is needed, design an AST and runtime model that handles per-instance state, initialization, encapsulation, event subscription, cleanup and errors. Do not invent syntax in production code before approving it in the canonical specification.

### Phase 4 — Community registry and website

- Search, publisher namespaces, semantic versions, verified ownership, documentation, ratings/reporting, package integrity and licenses.
- Private/official/community sources, provenance, dependency and vulnerability warnings.
- Authenticated publish, moderation, rate limits, malware review, revocation and secure download.
- No arbitrary package code execution during indexing or preview.

## 5. Proposed package manifest (design, not current schema)

```json
{
  "schemaVersion": 1,
  "name": "enemy-health",
  "publisher": "otter-official",
  "version": "0.1.0",
  "kind": "pack",
  "description": "Reusable health and damage utilities",
  "entry": "src/main.ot",
  "license": "SEE-LICENSE-IN-PACKAGE",
  "platforms": ["console"],
  "capabilities": [],
  "dependencies": {},
  "tags": ["games", "health", "damage"],
  "examples": ["examples/demo.ot"]
}
```

`entry` and `examples` must be confined to the package directory (reject `..` traversal and unsafe symlinks). Version ranges, canonical publisher IDs, signatures and checksums require a separate formal schema decision. A package lockfile must pin exact versions and content digests.

## 6. Valid existing-syntax illustrations

### Existing reusable type

```otter
a Enemy has
    name
    health
.

goblin is a Enemy
name of goblin is "Forest Goblin"
health of goblin is 150
say name of goblin
```

**Note:** Check whether the current runtime accepts the indefinite article and exact type initialization shown; this example follows the documented type grammar, but was not executed in this review.

### Existing source import

```otter
use "lib/enemy.ot"
```

### Existing function

```otter
to greet name
    say "Hello" name
.

greet "World"
```

### Existing loop

```otter
for each item in items
    say item
.
```

Do not advertise proposed method/event syntax as already executable.

## 7. C# implementation map

| Concern | Inspect first | Expected work |
|---|---|---|
| Lexing | `src/Otter.Language/Lexer.cs` | None for phase 1; new tokens only after grammar approval |
| Parsing | `src/Otter.Language/Parser.cs`, `Syntax.cs` | Preserve current `use`; add nodes only for approved grammar |
| CLI imports | `src/Otter.Cli/Modules.cs` | Resolve installed Pack entry points safely, maintain file import behavior |
| Runtime | `src/Otter.Runtime/Interpreter.cs`, `Scope.cs`, `Values.cs` | No special Pack object required for phase 1 |
| Studio | `otterPS/otter-studio/js/project/package-module-engine.js` | Audit existing package features and extend rather than duplicate |
| IDE search | `otterPS/otter-studio/js/language/otter-language-service.js`, `js/navigation/symbol-index.js` | Index local snippets, package metadata, and symbols |
| Tests | `otter-net/tests`, `otterPS/tests`, Studio scripts | Unit, conformance and CLI end-to-end tests |

## 8. Search UX specification

Search should accept literal names and natural-language queries: `for loop`, `convert number to text`, `enemy damage`, `sql query`, `button click`. Results show title, kind, version, publisher, license, platform support, capabilities, dependencies and example preview. Snippets have **Insert**; Packs have **Install**; Kits have **Install kit**. Ranking favors exact name, official verified matches, compatible platform and well-documented examples. Start with deterministic local token matching; semantic/AI search is optional later.

## 9. Security and correctness

- Packages are untrusted, even when searchable.
- Validate manifests, file paths, archives, publisher identity and checksum before install.
- Never execute scripts merely to generate previews or indexes.
- Display required capabilities before installation; enforce permissions at runtime where supported, rather than claiming manifest metadata alone provides a sandbox.
- Use parameterized SQL in database examples; never embed user input in query text.
- Use reproducible lockfiles and deterministic dependency resolution; detect cycles and version conflicts.
- Keep registry network failures from breaking offline/local development.
- Avoid broad claims of cross-platform support until the same Pack is tested on each declared target.

## 10. Acceptance criteria

### Phase 1 done when:

- [ ] Existing Otter language and import tests pass unchanged.
- [ ] A developer can author a Pack with an existing `.ot` source entry point and a manifest.
- [ ] Studio searches and previews local Packs and Snippets without running them.
- [ ] Installing a Pack creates deterministic files and a lockfile.
- [ ] A second project can install and execute the Pack using supported import behavior.
- [ ] Duplicate installs are idempotent; missing/cyclic dependencies produce actionable errors.
- [ ] A corrupted archive, traversal path, and checksum mismatch are rejected.
- [ ] Offline searches and installs from local cache work.
- [ ] Native C# CLI and PowerShell reference implementation agree on the resulting program's output and errors.
- [ ] An actual user-facing Studio and CLI workflow is demonstrated end-to-end.

### Regression tests

- `name of person` still resolves and assigns correctly.
- `.` still terminates blocks, not member access.
- `use "relative/path.ot"` retains resolution, deduplication and cycle diagnostics.
- Imported functions respect scope and existing call syntax.
- No Pack installation modifies application-specific runtime behavior.

## 11. First implementation tickets

1. **Audit**: inventory C# and PowerShell module resolution and Studio package features; document confirmed gaps.
2. **Schema**: specify and validate package manifest and local registry index; add tests.
3. **Installer**: implement safe project-local install, lockfile and deterministic resolution.
4. **Snippet catalog**: implement searchable snippets with insert action, preview and source attribution.
5. **Pack integration**: install one simple reusable Otter source Pack and execute via the normal CLI.
6. **Parity and E2E**: run C# and reference engine conformance, plus Studio user-flow tests.
7. **Language proposal**: only after phase 1, submit a separate decision for named imports and richer type behavior.

## 12. Open decisions for project owner

- Should Pack remain purely a distribution unit, with existing `a Type has` as the object system? **Recommended yes.**
- Should named imports be a new `use` form or generated source-file aliases? **Defer until phase 1 works.**
- Is the registry public, curated, or both? **Start curated, add community publishing with review.**
- Which target is first: console, web, desktop, or games? **Start console for language/runtime proof, then expand.**
- How are package capabilities enforced in each runtime? **Must be specified before untrusted third-party execution.**

## 13. Explicit non-goals for phase 1

No new `pack` keyword; no dot notation; no new `end` keyword; no new event grammar; no C# reflection-based plugin loading from arbitrary packages; no automatic online execution; no registry monetization; no claim of universal cross-platform compatibility.
