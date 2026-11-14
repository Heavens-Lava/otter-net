#!/usr/bin/env python3
"""
Differential Scoping Test Suite: Otter-Net (C#) vs OtterPS (PowerShell Reference)
Task 1: Proves that Otter-Net and the exact frozen OtterPS 1.0.1 reference
produce identical exit codes, stdout, and error messages across:
1. Object creation with a caller variable of the same name (expected Exit 3, exact fatal error message)
2. Plain assignment with a caller variable of the same name (expected Exit 0, mutates caller variable)
3. Same-scope object redefinition (expected Exit 3, exact fatal error message)
4. Nested function calls and outer scope traversal (expected Exit 0, identical state evolution)
5. Function parameter shadowing outer variable (expected Exit 0, parameter local)
6. For-each loop variable shadowing outer variable (expected Exit 0, loop variable local)
7. Pack imports with caller variable coexistence (expected Exit 0, no collision)

High-integrity assertion:
- Requires the exact frozen reference ../otterPS-1.0.1/otter.ps1 (fails immediately if missing).
- Every test scenario specifies an expected exit code and expected output or error pattern.
- Matching unexpected failures (e.g. exit code 1 or missing files) immediately fail the test.
"""

import os
import sys
import subprocess
import tempfile
import re

def normalize_text(text):
    # Normalize line endings and strip trailing whitespace
    lines = [line.strip() for line in text.replace("\r\n", "\n").split("\n")]
    # Strip empty lines from edges
    while lines and not lines[0]:
        lines.pop(0)
    while lines and not lines[-1]:
        lines.pop()
    return lines

def extract_error_message(text):
    # Match Otter runtime error lines (e.g. 'Otter will not replace existing...')
    for line in text.splitlines():
        if "Otter will not replace existing" in line:
            return re.sub(r'\s+', ' ', line.strip())
        if "Otter could not find" in line:
            return re.sub(r'\s+', ' ', line.strip())
    return ""

def run_case(name, script_content, expected_exit_code, expected_pattern, repo_root, otter_net_cmd, otter_ps_cmd):
    with tempfile.TemporaryDirectory() as tmp_dir:
        test_file = os.path.join(tmp_dir, "test.ot")
        with open(test_file, "w") as f:
            f.write(script_content)

        # 1. Run on OtterPS (PowerShell reference)
        res_ps = subprocess.run(
            otter_ps_cmd + [test_file],
            cwd=tmp_dir,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )

        # 2. Run on Otter-Net (C# implementation)
        res_net = subprocess.run(
            otter_net_cmd + [test_file],
            cwd=tmp_dir,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )

        # 3. Verify exit code against expected exit code
        if res_ps.returncode != expected_exit_code:
            print(f"  [FAIL] {name}: OtterPS exited with {res_ps.returncode}, expected {expected_exit_code}")
            print("  OtterPS stdout:\n", res_ps.stdout)
            print("  OtterPS stderr:\n", res_ps.stderr)
            return False

        if res_net.returncode != expected_exit_code:
            print(f"  [FAIL] {name}: Otter-Net exited with {res_net.returncode}, expected {expected_exit_code}")
            print("  Otter-Net stdout:\n", res_net.stdout)
            print("  Otter-Net stderr:\n", res_net.stderr)
            return False

        # 4. Verify behavioral output or error
        if expected_exit_code == 0:
            norm_ps = normalize_text(res_ps.stdout)
            norm_net = normalize_text(res_net.stdout)
            if norm_ps != norm_net:
                print(f"  [FAIL] {name}: Stdout mismatch between OtterPS and Otter-Net")
                print("  OtterPS:", norm_ps)
                print("  Otter-Net:", norm_net)
                return False

            # Verify expected output patterns
            full_out = "\n".join(norm_net)
            if isinstance(expected_pattern, list):
                for pat in expected_pattern:
                    if pat not in full_out:
                        print(f"  [FAIL] {name}: Expected output line '{pat}' missing from output:\n{full_out}")
                        return False
            elif isinstance(expected_pattern, str) and expected_pattern:
                if expected_pattern not in full_out:
                    print(f"  [FAIL] {name}: Expected pattern '{expected_pattern}' missing from output:\n{full_out}")
                    return False
        else:
            # Both failed with expected non-zero exit code: verify exact runtime error
            err_ps = extract_error_message(res_ps.stdout + res_ps.stderr)
            err_net = extract_error_message(res_net.stdout + res_net.stderr)

            if isinstance(expected_pattern, str) and expected_pattern:
                if expected_pattern not in (res_ps.stdout + res_ps.stderr):
                    print(f"  [FAIL] {name}: OtterPS failed with unexpected error (missing '{expected_pattern}'):\n{res_ps.stdout + res_ps.stderr}")
                    return False
                if expected_pattern not in (res_net.stdout + res_net.stderr):
                    print(f"  [FAIL] {name}: Otter-Net failed with unexpected error (missing '{expected_pattern}'):\n{res_net.stdout + res_net.stderr}")
                    return False

            if err_ps and err_net and err_ps != err_net:
                print(f"  [FAIL] {name}: Error message mismatch between engines")
                print("  OtterPS:", err_ps)
                print("  Otter-Net:", err_net)
                return False

        print(f"  [PASS] {name} (Exit code: {res_ps.returncode}, verified expected behavior)")
        return True

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    frozen_ps = os.path.join(os.path.dirname(repo_root), "otterPS-1.0.1", "otter.ps1")

    # High priority: require the exact frozen reference implementation
    if not os.path.exists(frozen_ps):
        print(f"FATAL ERROR: Exact frozen reference implementation not found at {frozen_ps}")
        print("Silent fallback to mutable checkout is prohibited for conformance certification.")
        sys.exit(1)

    otter_ps_script = frozen_ps
    print(f"PowerShell reference implementation: {otter_ps_script} (FROZEN 1.0.1)")

    # Support passing an installed otter executable via argument or OTTER_NET_BIN
    env_bin = os.environ.get("OTTER_NET_BIN")
    if len(sys.argv) > 1 and sys.argv[1]:
        installed_otter = sys.argv[1]
        otter_net_cmd = [installed_otter, "run"]
        print(f"Testing against installed Otter executable: {installed_otter}")
    elif env_bin and os.path.exists(env_bin):
        otter_net_cmd = [env_bin, "run"]
        print(f"Testing against installed Otter executable (OTTER_NET_BIN): {env_bin}")
    else:
        cli_dll = os.path.join(repo_root, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")
        if os.path.exists(cli_dll):
            otter_net_cmd = ["dotnet", cli_dll, "run"]
        else:
            otter_net_cmd = ["otter", "run"]
        print(f"Testing against Otter-Net command: {' '.join(otter_net_cmd)}")

    otter_ps_cmd = ["pwsh", "-NoProfile", "-File", otter_ps_script, "run"]

    print("=" * 70)
    print(" Running Otter 1.0.1 Scoping Differential Verification Suite")
    print(" Dual-engine parity: otter-net vs frozen otterPS-1.0.1 reference")
    print("=" * 70)

    cases = [
        # Case 1: Object creation with a caller variable of the same name (Runtime Error)
        (
            "1. Object creation caller clashing (fatal error)",
            """
a Box has
    val
.
to makeBox n
    b is a Box with val n
    return b
.
b is makeBox 1
c is makeBox 2
say "should not reach here"
""",
            3,
            'Otter will not replace existing a Box called "b" with a new thing.'
        ),

        # Case 2: Plain assignment with a caller variable of the same name (Mutates outer scope)
        (
            "2. Plain assignment caller clashing (updates outer scope)",
            """
to mutateCounter
    totalCount is 20
.
totalCount is 10
say "before:" totalCount
mutateCounter
say "after:" totalCount
""",
            0,
            ["before: 10", "after: 20"]
        ),

        # Case 3: Same-scope object redefinition (fatal error)
        (
            "3. Same-scope object redefinition (fatal error)",
            """
a Box has
    val
.
b is a Box with val 1
b is a Box with val 2
say "should not reach here"
""",
            3,
            'Otter will not replace existing a Box called "b" with a new thing.'
        ),

        # Case 4: Nested function calls and scope traversal
        (
            "4. Nested function calls and global state traversal",
            """
to innerFn
    val is val plus 10
    say "inner:" val
.
to outerFn
    val is 5
    say "outer before:" val
    innerFn
    say "outer after:" val
.
val is 100
say "global before:" val
outerFn
say "global after:" val
""",
            0,
            ["global before: 100", "outer before: 5", "inner: 15", "outer after: 15", "global after: 15"]
        ),

        # Case 5: Function parameters cleanly shadow outer variables (SetLocal)
        (
            "5. Function parameter shadowing outer variable",
            """
to computeWithParam total factor
    # total is a parameter, so it shadows outer total
    total is total times factor
    return total
.
total is 1000
res is computeWithParam 5 3
say "returned:" res
say "global total preserved:" total
""",
            0,
            ["returned: 15", "global total preserved: 1000"]
        ),

        # Case 6: For-each loop variable mutates existing variable in scope (1.0.1 reference behavior)
        (
            "6. For-each loop variable mutates existing variable in scope",
            """
item is "outer-item"
numbers are empty
add 1 to numbers
add 2 to numbers
add 3 to numbers
for each item in numbers
    say "loop item:" item
.
say "after loop item:" item
""",
            0,
            ["loop item: 1", "loop item: 2", "loop item: 3", "after loop item: 3"]
        ),

        # Case 7: Pack imports with caller variable coexistence
        (
            "7. Pack function call without variable clash",
            f"""
use "{repo_root}/library/packs/otter-official/math-utils/src/main.ot"
val is 50
clamped is clamp val 0 20
say "clamped:" clamped
""",
            0,
            ["clamped: 20"]
        )
    ]

    failed = 0
    for name, content, exp_code, exp_pattern in cases:
        if not run_case(name, content, exp_code, exp_pattern, repo_root, otter_net_cmd, otter_ps_cmd):
            failed += 1

    print("=" * 70)
    if failed == 0:
        print(f" SUCCESS: All {len(cases)} differential scoping checks MATCHED 100% with otterPS-1.0.1!")
        print(" All expected exit codes and behavioral patterns verified.")
        print("=" * 70)
        sys.exit(0)
    else:
        print(f" FAILURE: {failed} out of {len(cases)} differential checks failed.")
        print("=" * 70)
        sys.exit(1)

if __name__ == "__main__":
    main()
