# RFC: Lexical Function Scoping and Local Variable Declarations for Otter

**Document:** `docs/PROPOSAL_FUNCTION_SCOPING.md`  
**Status:** Proposal / Draft (Awaiting User Review)  
**Target Release:** Otter 1.1 / Otter 2.0  
**Author:** Otter Language & Runtime Team  

---

## 1. Abstract

In Otter 1.0.1, variables declared and assigned inside functions share lexical traversal semantics with the global environment (`OtterEnvironment.Set`). Except for function parameters and for-each loop variables (which use `SetLocal`), any assignment `x is value` or object instantiation `x is a Thing` searches up the scope chain. If a variable of the same name exists in an enclosing or global scope, Otter mutates that existing variable or terminates with a fatal runtime error: `"Otter will not replace existing <Type> called <Name> with a new thing."`

This document proposes formal lexical function scoping for a future Otter release. It introduces explicit local declarations (`local <name> is <expr>`), isolates object definitions inside function bodies, defines nested closure behavior, and provides an opt-in migration path to protect existing 1.0.1 programs.

---

## 2. Motivation: Why Current 1.0.1 Scoping is Dangerous

### 2.1 The Accidental Mutation Hazard
In Otter 1.0.1, assignments inside a function do not create local variables if the identifier already exists in an outer scope. Consider:

```otter
to calculateBatchMetrics scores
    total is 0
    for each s in scores
        total is total plus s
    .
    return total
.

# Caller script
total is 500000    # High-value ledger balance
result is calculateBatchMetrics [10, 20, 30]
say total          # Outputs: 60! The caller's $500,000 balance was erased.
```

Because `total` was defined in the outer scope, `calculateBatchMetrics` silently overwrote the caller's variable. The caller has no defense against this without knowing the private internal variable names of every function and library they call.

### 2.2 The Object Factory Collision Hazard
Under Decision D52, Otter prohibits replacing an existing object with a new one:
```otter
to createBox val
    b is a Box with value val
    return b
.

b is createBox 10
c is createBox 20   # RUNTIME ERROR (Exit code 3):
                    # "Otter will not replace existing a Box called "b" with a new thing."
```
When `createBox 20` executes, `b` already exists in the caller scope. Because `createBox` does not isolate `b`, the interpreter detects `scope.Has("b")` and aborts the entire program.

### 2.3 The Limitations of Naming Conventions (Task 2 Finding)
In Phase 9, official packs adopted distinctive internal prefixes (`_mu_`, `_tu_`, `_ge_`, etc.) to mitigate common collisions (`result`, `total`, `item`). However, naming conventions **cannot provide guaranteed scope isolation**. If a caller happens to use the prefixed identifier, or if two libraries share an internal name, the program crashes or corrupts data (demonstrated in `tests/scoping/test_unavoidable_collision_limitation.ot`):

```otter
_factory_box is "vital_data"
# Calling a third-party factory that happens to use `_factory_box` triggers:
# "Otter will not replace existing some text called "_factory_box" with a new thing."
```

Guaranteed isolation requires language-level lexical scoping.

---

## 3. Proposed Specification

### 3.1 Local Variable Declarations (`local`)
Introduce the `local` keyword inside function blocks:

```otter
to calculateSum items
    local total is 0
    for each item in items
        total is total plus item
    .
    return total
.
```

**Semantics:**
- `local <name> is <expr>` allocates `<name>` strictly within the current activation record (`scope.SetLocal`).
- Any subsequent assignment to `<name>` within that function body resolves to the local slot, even if an outer variable with the same name exists.
- The local variable is destroyed when the function call stack frame pops.

### 3.2 Object-Definition Semantics Inside Functions
Object definition statements (`x is a <Type>`) executing inside a function body shall bind locally by default:

```otter
to makeHero heroName
    hero is a Actor with name heroName
    return hero
.
```

**Semantics:**
- Inside a function, `hero is a Actor` allocates `hero` in the local activation record (`scope.SetLocal`).
- The D52 replacement check (`scope.Has`) is evaluated against `scope.HasLocal` only. Same-scope redefinition (`hero is a Actor; hero is a Actor` in the same block) remains a fatal error, but outer-scope instances are safely shadowed.
- Outside functions (top-level script execution), `scope.Has` continues to protect global singletons and UI resources.

### 3.3 Ordinary Assignment Semantics
For bare assignments `name is <expr>` inside a function:

- **Option A (Lexical by Default - Recommended for Otter 2.0):**
  If `name` is neither a parameter nor previously declared with `local`, the first assignment implicitly creates a function-local variable. Outer variables cannot be mutated unless explicitly declared with `outer <name> is <expr>`.
- **Option B (Explicit Local - Backward Compatible for Otter 1.1):**
  Bare `name is <expr>` searches enclosing scopes (1.0.1 behavior). Developers use `local name is <expr>` whenever local isolation is desired.

### 3.4 Parameter Scope
Function parameters continue to use `SetLocal`. A parameter always shadows any outer variable of the same name:
```otter
to scaleValue total factor
    total is total times factor    # Mutates the local parameter, outer 'total' untouched
    return total
.
```

### 3.5 Nested Functions and Closures
Functions declared inside another function capture the lexical environment of their parent rather than jumping directly to `GlobalEnvironment`:

```otter
to makeCounter initialVal
    local count is initialVal
    to increment
        count is count plus 1
        return count
    .
    return increment
.
```

### 3.6 Explicit Mutation of Outer Variables (`outer`)
When a function legitimately needs to modify a state variable in its enclosing or global scope, it must do so explicitly:

```otter
activeJobs is 0

to startJob
    # Explicit outer mutation avoids accidental collisions
    outer activeJobs is activeJobs plus 1
.
```

---

## 4. Backward Compatibility & Migration Strategy

To adhere to the principle that Otter 1.0.1 is a frozen reference:

1. **Manifest Opt-In (`otter.json`):**
   ```json
   {
     "name": "my-app",
     "version": "1.0.0",
     "languageVersion": "2.0"
   }
   ```
   If `languageVersion` is `"1.0"` or absent, the compiler operates in strict 1.0.1 compatibility mode.
2. **Deprecation Diagnostics in Otter 1.1:**
   In 1.1, the LSP and compiler emit informational warnings when a function assignment mutates an outer-scope variable:
   `Warning (W104): Assignment to 'total' inside function modifies variable in caller scope. Use 'local total is ...' to isolate.`
3. **Library Modernization:**
   With lexical scoping, official packs can eliminate defensive prefixes (`_mu_`, `_tu_`, `_ge_`) and use clean identifiers without risk of caller collision.

---

## 5. Summary Table

| Behavior | Otter 1.0.1 (Frozen) | Proposed Otter 2.0 |
| :--- | :--- | :--- |
| **Local declaration** | Not supported | `local x is <expr>` |
| **Object creation in function** | Traverses scope; throws if caller has name | Local to function activation record |
| **Caller variable of same name** | Overwritten or causes crash | Preserved and shadowed |
| **Outer mutation** | Implicit (dangerous) | Explicit via `outer x is <expr>` |
| **Parameters** | Local (`SetLocal`) | Local (`SetLocal`) |
| **Nested closures** | Parent is Global Environment | Lexical closure over parent frame |
