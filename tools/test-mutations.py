#!/usr/bin/env python3
"""
tools/test-mutations.py - Automated Mutation Testing Suite for Otter Reusable Library
Deliberately mutates functions in each official pack to return incorrect values
and asserts that the pack's behavioral test suite fails with a non-zero exit code.
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

def test_mutation(otter_cmd, source_path, test_path, original_target, mutated_replacement, test_name):
    with open(source_path, "r", encoding="utf-8") as f:
        original_code = f.read()

    if original_target not in original_code:
        print(f"  [ERROR] Target code snippet '{original_target}' not found in {source_path}")
        return False

    mutated_code = original_code.replace(original_target, mutated_replacement, 1)
    with open(source_path, "w", encoding="utf-8") as f:
        f.write(mutated_code)

    try:
        rc, out, err = run_cmd(otter_cmd + [test_path])
        # A proper behavioral test suite MUST FAIL (rc != 0) when code is mutated
        if rc != 0:
            print(f"  [PASS] {test_name}: Mutation caught (exit code {rc})")
            return True
        else:
            print(f"  [FAIL] {test_name}: Mutation NOT caught! Test falsely passed exit code 0.")
            print(f"         Output: {out[:120]}")
            return False
    finally:
        with open(source_path, "w", encoding="utf-8") as f:
            f.write(original_code)

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.dirname(script_dir)
    otter_cmd = get_otter_cmd(root_dir)

    print("=" * 70)
    print(" Running Otter Reusable Library Mutation Testing Suite (Milestone 2)")
    print(f" Otter Executable: {' '.join(otter_cmd)}")
    print("=" * 70)

    packs_dir = os.path.join(root_dir, "library", "packs", "otter-official")
    mutations = [
        # math-utils
        (
            os.path.join(packs_dir, "math-utils", "src", "main.ot"),
            os.path.join(packs_dir, "math-utils", "tests", "test.ot"),
            "return value",
            "return 999",
            "math-utils: clamp returns 999"
        ),
        (
            os.path.join(packs_dir, "math-utils", "src", "main.ot"),
            os.path.join(packs_dir, "math-utils", "tests", "test.ot"),
            "return total divided by count",
            "return 0",
            "math-utils: calcAverage returns 0"
        ),
        (
            os.path.join(packs_dir, "math-utils", "src", "main.ot"),
            os.path.join(packs_dir, "math-utils", "tests", "test.ot"),
            "return fraction times 100",
            "return 0",
            "math-utils: calcPercentage returns 0"
        ),

        # text-utils
        (
            os.path.join(packs_dir, "text-utils", "src", "main.ot"),
            os.path.join(packs_dir, "text-utils", "tests", "test.ot"),
            "return padding plus text",
            "return text",
            "text-utils: padLeft ignores padding"
        ),
        (
            os.path.join(packs_dir, "text-utils", "src", "main.ot"),
            os.path.join(packs_dir, "text-utils", "tests", "test.ot"),
            'replace " " with "-" in clean',
            'replace " " with "+" in clean',
            "text-utils: slugify uses '+' instead of '-'"
        ),
        (
            os.path.join(packs_dir, "text-utils", "src", "main.ot"),
            os.path.join(packs_dir, "text-utils", "tests", "test.ot"),
            "return uppercase of text",
            "return text",
            "text-utils: toUpper returns original text"
        ),

        # list-utils
        (
            os.path.join(packs_dir, "list-utils", "src", "main.ot"),
            os.path.join(packs_dir, "list-utils", "tests", "test.ot"),
            "return true\n    .\n    return false",
            "return false\n    .\n    return false",
            "list-utils: hasItem always returns false"
        ),
        (
            os.path.join(packs_dir, "list-utils", "src", "main.ot"),
            os.path.join(packs_dir, "list-utils", "tests", "test.ot"),
            "add item to result",
            "# dropped add",
            "list-utils: dedupeList drops additions"
        ),
        (
            os.path.join(packs_dir, "list-utils", "src", "main.ot"),
            os.path.join(packs_dir, "list-utils", "tests", "test.ot"),
            "total is total plus item",
            "total is total",
            "list-utils: calcListSum does not accumulate"
        ),

        # file-helpers
        (
            os.path.join(packs_dir, "file-helpers", "src", "main.ot"),
            os.path.join(packs_dir, "file-helpers", "tests", "test.ot"),
            "return fallbackValue\n.",
            'return "WRONG_FALLBACK"\n.',
            "file-helpers: readOrFallback returns corrupt fallback"
        ),
        (
            os.path.join(packs_dir, "file-helpers", "src", "main.ot"),
            os.path.join(packs_dir, "file-helpers", "tests", "test.ot"),
            "return target\n    otherwise\n        return fallbackValue",
            "return target\n    otherwise\n        return gone",
            "file-helpers: fromJsonSafe returns gone on error"
        ),

        # game-entity
        (
            os.path.join(packs_dir, "game-entity", "src", "main.ot"),
            os.path.join(packs_dir, "game-entity", "tests", "test.ot"),
            "isAlive of actor is false\n        return 0",
            "isAlive of actor is true\n        return 0",
            "game-entity: applyDamage keeps dead actor alive"
        ),
        (
            os.path.join(packs_dir, "game-entity", "src", "main.ot"),
            os.path.join(packs_dir, "game-entity", "tests", "test.ot"),
            "newMax is curMax plus 25",
            "newMax is curMax",
            "game-entity: levelUp does not increase max health"
        ),
        (
            os.path.join(packs_dir, "game-entity", "src", "main.ot"),
            os.path.join(packs_dir, "game-entity", "tests", "test.ot"),
            "if curCount is at least cap\n        return false",
            "if curCount is at least cap\n        return true",
            "game-entity: addItemToInventory allows capacity overflow"
        ),
    ]

    passed = 0
    failed = 0

    for src, tst, orig, mut, name in mutations:
        if test_mutation(otter_cmd, src, tst, orig, mut, name):
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
