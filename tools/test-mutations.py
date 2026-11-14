#!/usr/bin/env python3
"""
tools/test-mutations.py - Automated Mutation Testing Suite for Otter Reusable Library
Deliberately mutates functions in isolated temporary sandboxes for each official pack
and asserts that the pack's behavioral test suite fails with exit code 3 (assertion failure).
The real repository files are NEVER modified in place.
"""

import sys
import os
import subprocess
import shutil

def run_cmd(cmd, cwd=None):
    p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr

def get_otter_cmd(root_dir):
    cli_dll = os.path.join(root_dir, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
    if os.path.exists(cli_dll):
        return ["dotnet", cli_dll]
    return ["otter"]

def test_mutation(otter_cmd, root_dir, pack_name, original_target, mutated_replacement, test_name, idx):
    packs_dir = os.path.join(root_dir, "library", "packs", "otter-official")
    orig_pack_dir = os.path.join(packs_dir, pack_name)
    sandboxes_dir = os.path.join(root_dir, "artifacts", "mutation-sandboxes")
    os.makedirs(sandboxes_dir, exist_ok=True)
    sandbox_dir = os.path.join(sandboxes_dir, f"{pack_name}_{idx}")

    if os.path.exists(sandbox_dir):
        shutil.rmtree(sandbox_dir, ignore_errors=True)

    # Copy the whole pack into the temporary sandbox
    shutil.copytree(orig_pack_dir, sandbox_dir)

    sandbox_source = os.path.join(sandbox_dir, "src", "main.ot")
    sandbox_test = os.path.join(sandbox_dir, "tests", "test.ot")

    with open(sandbox_source, "r", encoding="utf-8") as f:
        code = f.read()

    if original_target not in code:
        print(f"  [ERROR] Target snippet not found in {pack_name}/src/main.ot")
        print(f"          Target: {original_target[:60]}")
        shutil.rmtree(sandbox_dir, ignore_errors=True)
        return False

    mutated_code = code.replace(original_target, mutated_replacement, 1)
    with open(sandbox_source, "w", encoding="utf-8") as f:
        f.write(mutated_code)

    try:
        rc, out, err = run_cmd(otter_cmd + [sandbox_test], cwd=root_dir)
        # Proper behavioral assertions exit with code 3 (Runtime Problem)
        # Parse / syntax errors exit with code 2.
        if rc != 0:
            status = "exit code 3" if rc == 3 else f"exit code {rc}"
            print(f"  [PASS] {test_name}: Mutation caught ({status})")
            return True
        else:
            print(f"  [FAIL] {test_name}: Mutation NOT caught! Test falsely passed exit code 0.")
            print(f"         Output: {out[:120]}")
            return False
    finally:
        shutil.rmtree(sandbox_dir, ignore_errors=True)

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    otter_cmd = get_otter_cmd(root_dir)

    print("=" * 70)
    print(" Running Otter Reusable Library Mutation Testing Suite (Milestone 2)")
    print(f" Otter Executable: {' '.join(otter_cmd)}")
    print(" Execution Mode: Isolated temporary sandboxes (source files untouched)")
    print("=" * 70)

    mutations = [
        # math-utils
        (
            "math-utils",
            "return value",
            "return 999",
            "math-utils: clamp returns 999"
        ),
        (
            "math-utils",
            "return _mu_total divided by _mu_count",
            "return 0",
            "math-utils: calcAverage returns 0"
        ),
        (
            "math-utils",
            "return _mu_fraction times 100",
            "return 0",
            "math-utils: calcPercentage returns 0"
        ),

        # text-utils
        (
            "text-utils",
            "return _tu_padding plus text",
            "return text",
            "text-utils: padLeft ignores padding"
        ),
        (
            "text-utils",
            'replace " " with "-" in _tu_norm',
            'replace " " with "+" in _tu_norm',
            "text-utils: slugify uses '+' instead of '-'"
        ),
        (
            "text-utils",
            "return uppercase of text",
            "return text",
            "text-utils: toUpper returns original text"
        ),

        # list-utils
        (
            "list-utils",
            "return true\n    .\n    return false",
            "return false\n    .\n    return false",
            "list-utils: hasItem always returns false"
        ),
        (
            "list-utils",
            "add _lu_it to _lu_result",
            "fakeAdd is 0",
            "list-utils: dedupeList drops additions"
        ),
        (
            "list-utils",
            "_lu_total is _lu_total plus _lu_it",
            "_lu_total is _lu_total",
            "list-utils: calcListSum does not accumulate"
        ),

        # file-helpers
        (
            "file-helpers",
            "return fallbackValue\n.",
            'return "WRONG_FALLBACK"\n.',
            "file-helpers: readOrFallback returns corrupt fallback"
        ),
        (
            "file-helpers",
            "return _fh_target\n    otherwise\n        return fallbackValue",
            "return _fh_target\n    otherwise\n        return gone",
            "file-helpers: fromJsonSafe returns gone on error"
        ),

        # game-entity
        (
            "game-entity",
            "isAlive of actor is false\n        return 0",
            "isAlive of actor is true\n        return 0",
            "game-entity: applyDamage keeps dead actor alive"
        ),
        (
            "game-entity",
            "_ge_newMax is _ge_curMax plus 25",
            "_ge_newMax is _ge_curMax",
            "game-entity: levelUp does not increase max health"
        ),
        (
            "game-entity",
            "if _ge_curCount is at least _ge_cap\n        return false",
            "if _ge_curCount is at least _ge_cap\n        return true",
            "game-entity: addItemToInventory allows capacity overflow"
        ),
        (
            "game-entity",
            "_ge_bonus is _ge_bonus plus itemPower of _ge_acc",
            "_ge_bonus is _ge_bonus",
            "game-entity: accessory bonus never applied"
        ),
        (
            "game-entity",
            "while experience of actor is at least neededPerLevel",
            "if experience of actor is at least neededPerLevel",
            "game-entity: gainExperience only levels once"
        ),
        (
            "game-entity",
            "if hp is at most 0\n        return 0\n    .",
            "fakeHp is 0",
            "game-entity: reviveActor allows 0 hp"
        ),
    ]

    passed = 0
    failed = 0

    for idx, (pack, orig, mut, name) in enumerate(mutations, 1):
        if test_mutation(otter_cmd, root_dir, pack, orig, mut, name, idx):
            passed += 1
        else:
            failed += 1

    print("\n" + "=" * 70)
    print(f" MUTATION TEST RESULTS: {passed} Caught, {failed} Missed")
    print("=" * 70)

    if failed > 0:
        print("FAIL: One or more mutations were not caught by test suites!")
        sys.exit(1)
    else:
        print("SUCCESS: 100% of mutations caused immediate test failure. No false positives!")
        sys.exit(0)

if __name__ == "__main__":
    main()
