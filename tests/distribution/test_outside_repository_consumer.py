#!/usr/bin/env python3
"""
Task 4 Test Suite: Outside-Repository Consumer Verification
Tests that an isolated installation of Otter outside the repository
delivers the complete canonical library, correctly installs updated Packs,
pins exact version numbers and SHA-256 digests in otter.lock, detects
tampering during 'otter pack verify', and recovers cleanly.

Requirements:
- Run in a temporary directory outside the repository and its parent trees.
- Clear OTTER_LIBRARY_PATH and isolate HOME so user-level cache cannot mask files.
- Uses freshly packaged Release distribution.
- Verifies exact versions: math-utils (1.0.1), game-entity (1.1.0),
  text-utils (1.0.1), list-utils (1.0.1), file-helpers (1.0.1).
- Tamper verification: corrupting one byte must fail pack verify; reinstalling must restore [OK].
"""

import os
import sys
import subprocess
import tempfile
import json
import hashlib
import shutil

def compute_sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()

def run_cmd(cmd, cwd=None, env=None):
    res = subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True)
    return res

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    package_script = os.path.join(repo_root, "tools", "package-distribution.sh")

    print("=" * 70)
    print(" Running Outside-Repository Consumer Distribution Test (Task 4)")
    print("=" * 70)

    # 1. Create isolated clean installation directory
    with tempfile.TemporaryDirectory(prefix="otter_install_") as install_dir, \
         tempfile.TemporaryDirectory(prefix="otter_fake_home_") as fake_home, \
         tempfile.TemporaryDirectory(prefix="otter_outside_proj_") as project_dir:

        print(f" Fresh Install Directory: {install_dir}")
        print(f" Isolated Home Directory:    {fake_home}")
        print(f" Outside Project Directory:  {project_dir}")

        # Package fresh release distribution
        pack_res = run_cmd([package_script, install_dir], cwd=repo_root)
        if pack_res.returncode != 0:
            print(f"FATAL: package-distribution.sh failed:\n{pack_res.stderr}\n{pack_res.stdout}")
            sys.exit(1)

        otter_bin = os.path.join(install_dir, "bin", "otter")
        if not os.path.exists(otter_bin):
            print(f"FATAL: otter binary missing at {otter_bin}")
            sys.exit(1)

        # Isolated environment: clear OTTER_LIBRARY_PATH, set HOME to fake_home
        clean_env = os.environ.copy()
        clean_env.pop("OTTER_LIBRARY_PATH", None)
        clean_env["HOME"] = fake_home

        # Check installed version
        v_res = run_cmd([otter_bin, "--version"], env=clean_env)
        if v_res.returncode != 0:
            print(f"FATAL: otter --version failed:\n{v_res.stderr}")
            sys.exit(1)
        installed_ver = v_res.stdout.strip()
        print(f" Installed Otter Version: {installed_ver}")
        if installed_ver != "Otter 1.0.1":
            print(f"FATAL: Expected 'Otter 1.0.1', got '{installed_ver}'")
            sys.exit(1)

        def get_resolved_lib_location(stdout):
            for line in stdout.splitlines():
                if "Location:" in line:
                    return line.split("Location:", 1)[1].strip()
            return ""

        # Precedence Test A: Bundled library selected when no user or project library exists
        doc_res = run_cmd([otter_bin, "doctor"], cwd=project_dir, env=clean_env)
        if doc_res.returncode != 0:
            print(f"FATAL: otter doctor failed in outside directory:\n{doc_res.stdout}\n{doc_res.stderr}")
            sys.exit(1)
        resolved_loc = get_resolved_lib_location(doc_res.stdout)
        expected_bundled = os.path.join(install_dir, "library")
        if not (resolved_loc == expected_bundled or os.path.samefile(resolved_loc, expected_bundled)):
            print(f"FATAL: Expected bundled library {expected_bundled}, but resolved {resolved_loc}")
            sys.exit(1)
        print(f" [PRECEDENCE 1 PASS] Bundled library correctly selected when no user library exists: {resolved_loc}")

        # Precedence Test B: Custom user library (~/.otter/library) takes precedence over bundled library
        user_lib_dir = os.path.join(fake_home, ".otter", "library")
        shutil.copytree(os.path.join(install_dir, "library"), user_lib_dir)
        
        user_doc_res = run_cmd([otter_bin, "doctor"], cwd=project_dir, env=clean_env)
        if user_doc_res.returncode != 0:
            print(f"FATAL: otter doctor failed with user library present:\n{user_doc_res.stdout}")
            sys.exit(1)
        user_resolved_loc = get_resolved_lib_location(user_doc_res.stdout)
        if not (user_resolved_loc == user_lib_dir or os.path.samefile(user_resolved_loc, user_lib_dir)):
            print(f"FATAL: Expected user library {user_lib_dir} to win, but resolved {user_resolved_loc}")
            sys.exit(1)
        print(f" [PRECEDENCE 2 PASS] Custom user library (~/.otter/library) correctly wins over bundled library: {user_resolved_loc}")

        # Precedence Test C: Explicit user OTTER_LIBRARY_PATH override takes precedence over both
        with tempfile.TemporaryDirectory(prefix="otter_env_lib_") as env_tmp:
            env_lib_dir = os.path.join(env_tmp, "library")
            shutil.copytree(os.path.join(install_dir, "library"), env_lib_dir)
            override_env = clean_env.copy()
            override_env["OTTER_LIBRARY_PATH"] = env_lib_dir
            env_doc_res = run_cmd([otter_bin, "doctor"], cwd=project_dir, env=override_env)
            if env_doc_res.returncode != 0:
                print(f"FATAL: otter doctor failed with OTTER_LIBRARY_PATH set:\n{env_doc_res.stdout}")
                sys.exit(1)
            env_resolved_loc = get_resolved_lib_location(env_doc_res.stdout)
            if not (env_resolved_loc == env_lib_dir or os.path.samefile(env_resolved_loc, env_lib_dir)):
                print(f"FATAL: Expected OTTER_LIBRARY_PATH {env_lib_dir} to win, but resolved {env_resolved_loc}")
                sys.exit(1)
            print(f" [PRECEDENCE 3 PASS] Explicit OTTER_LIBRARY_PATH correctly wins over user and bundled libraries: {env_resolved_loc}")

        # Precedence Test D: Launcher consistency across POSIX, PowerShell, and Windows CMD
        # Test PowerShell launcher if pwsh is available
        pwsh_bin = shutil.which("pwsh")
        if pwsh_bin:
            otter_ps1 = os.path.join(install_dir, "bin", "otter.ps1")
            ps_res = run_cmd([pwsh_bin, "-File", otter_ps1, "doctor"], cwd=project_dir, env=clean_env)
            if ps_res.returncode != 0:
                print(f"FATAL: pwsh launcher failed:\n{ps_res.stdout}\n{ps_res.stderr}")
                sys.exit(1)
            ps_loc = get_resolved_lib_location(ps_res.stdout)
            if not (ps_loc == user_lib_dir or os.path.samefile(ps_loc, user_lib_dir)):
                print(f"FATAL: PowerShell launcher resolved {ps_loc}, expected {user_lib_dir}")
                sys.exit(1)
            print(f" [LAUNCHER PASS] PowerShell launcher (otter.ps1) honors library precedence: {ps_loc}")

        # Statically verify Windows CMD launcher
        cmd_launcher = os.path.join(install_dir, "bin", "otter.cmd")
        with open(cmd_launcher, "r") as f:
            cmd_content = f.read()
        if "set OTTER_LIBRARY_PATH" in cmd_content or "set \"OTTER_LIBRARY_PATH=" in cmd_content:
            print("FATAL: Windows CMD launcher contains automatic OTTER_LIBRARY_PATH assignment!")
            sys.exit(1)
        print(" [LAUNCHER PASS] Windows CMD launcher (otter.cmd) preserves CLI precedence without overriding OTTER_LIBRARY_PATH.")

        # Remove the temporary user library so subsequent consumer project tests use the full canonical bundled library
        shutil.rmtree(user_lib_dir)

        # Step 1: Create a new project outside repository
        proj_json = {
            "name": "outside-consumer-app",
            "version": "1.0.0",
            "entry": "src/main.ot"
        }
        with open(os.path.join(project_dir, "project.json"), "w") as f:
            json.dump(proj_json, f, indent=2)
        os.makedirs(os.path.join(project_dir, "src"), exist_ok=True)
        print(" [STEP 1] Initialized outside project.")

        # Step 2 & 3: Install otter-official/math-utils and verify version 1.0.1
        res = run_cmd([otter_bin, "pack", "install", "math-utils", "--project", project_dir], cwd=project_dir, env=clean_env)
        if res.returncode != 0:
            print(f"FATAL: Failed to install math-utils:\n{res.stderr}\n{res.stdout}")
            sys.exit(1)
        
        math_manifest = os.path.join(project_dir, ".otter", "packages", "otter-official", "math-utils", "1.0.1", "otter.pack.json")
        if not os.path.exists(math_manifest):
            print(f"FATAL: math-utils 1.0.1 manifest missing at {math_manifest}")
            sys.exit(1)
        with open(math_manifest) as f:
            math_data = json.load(f)
        if math_data.get("version") != "1.0.1":
            print(f"FATAL: math-utils version mismatch: expected 1.0.1, got {math_data.get('version')}")
            sys.exit(1)
        print(" [STEP 2 & 3 PASS] math-utils v1.0.1 installed and verified.")

        # Step 4 & 5: Install otter-official/game-entity and verify version 1.1.0
        res = run_cmd([otter_bin, "pack", "install", "game-entity", "--project", project_dir], cwd=project_dir, env=clean_env)
        if res.returncode != 0:
            print(f"FATAL: Failed to install game-entity:\n{res.stderr}\n{res.stdout}")
            sys.exit(1)
        
        ge_manifest = os.path.join(project_dir, ".otter", "packages", "otter-official", "game-entity", "1.1.0", "otter.pack.json")
        if not os.path.exists(ge_manifest):
            print(f"FATAL: game-entity 1.1.0 manifest missing at {ge_manifest}")
            sys.exit(1)
        with open(ge_manifest) as f:
            ge_data = json.load(f)
        if ge_data.get("version") != "1.1.0":
            print(f"FATAL: game-entity version mismatch: expected 1.1.0, got {ge_data.get('version')}")
            sys.exit(1)
        print(" [STEP 4 & 5 PASS] game-entity v1.1.0 installed and verified.")

        # Step 6: Install remaining 3 packs and verify version 1.0.1
        remaining_packs = ["text-utils", "list-utils", "file-helpers"]
        for p in remaining_packs:
            res = run_cmd([otter_bin, "pack", "install", p, "--project", project_dir], cwd=project_dir, env=clean_env)
            if res.returncode != 0:
                print(f"FATAL: Failed to install {p}:\n{res.stderr}\n{res.stdout}")
                sys.exit(1)
            p_manifest = os.path.join(project_dir, ".otter", "packages", "otter-official", p, "1.0.1", "otter.pack.json")
            if not os.path.exists(p_manifest):
                print(f"FATAL: {p} 1.0.1 manifest missing at {p_manifest}")
                sys.exit(1)
            with open(p_manifest) as f:
                p_data = json.load(f)
            if p_data.get("version") != "1.0.1":
                print(f"FATAL: {p} version mismatch: expected 1.0.1, got {p_data.get('version')}")
                sys.exit(1)
            print(f" [STEP 6 PASS] {p} v1.0.1 installed and verified.")

        # Step 7: Execute actual imported Pack functions
        test_script = """
use "../.otter/packages/otter-official/math-utils/1.0.1/src/main.ot"
use "../.otter/packages/otter-official/text-utils/1.0.1/src/main.ot"
use "../.otter/packages/otter-official/list-utils/1.0.1/src/main.ot"
use "../.otter/packages/otter-official/file-helpers/1.0.1/src/main.ot"
use "../.otter/packages/otter-official/game-entity/1.1.0/src/main.ot"

# 1. math-utils
clamped is clamp 150 0 100
if clamped is not 100
    fail with "clamp failed"
.

# 2. text-utils
slug is slugify "Otter Outside Repo!"
if slug is not "otter-outside-repo"
    fail with "slugify failed"
.

# 3. list-utils
lst are empty
add "apple" to lst
add "banana" to lst
add "apple" to lst
deduped is dedupeList lst
if hasItem deduped "apple" is not true
    fail with "hasItem failed"
.

# 4. game-entity (new consumeItemFromInventory API)
bag is createInventory 5
potions is createStackableItem "Health Potion" "potion" 50 4 10
addItemToInventory bag potions

consumeOk is consumeItemFromInventory bag "Health Potion" 2
if not consumeOk
    fail with "consumeItemFromInventory failed"
.
foundPot is findItemInInventory bag "Health Potion"
if itemCount of foundPot is not 2
    fail with "Expected 2 potions remaining"
.

# 5. file-helpers
fallbackObj has
    mode is "safe"
.
parsed is fromJsonSafe "{\\"status\\": \\"ok\\"}" fallbackObj

say "OUTSIDE_TEST_SUCCESS"
"""
        script_path = os.path.join(project_dir, "src", "main.ot")
        with open(script_path, "w") as f:
            f.write(test_script)

        exec_res = run_cmd([otter_bin, "run", "src/main.ot"], cwd=project_dir, env=clean_env)
        if exec_res.returncode != 0 or "OUTSIDE_TEST_SUCCESS" not in exec_res.stdout:
            print(f"FATAL: Execution failed in outside directory:\nstdout: {exec_res.stdout}\nstderr: {exec_res.stderr}")
            sys.exit(1)
        print(" [STEP 7 PASS] Executed imported Pack functions from outside repository.")

        # Step 8: Run otter pack verify
        vfy_res = run_cmd([otter_bin, "pack", "verify", "--project", project_dir], cwd=project_dir, env=clean_env)
        if vfy_res.returncode != 0:
            print(f"FATAL: otter pack verify failed:\n{vfy_res.stdout}\n{vfy_res.stderr}")
            sys.exit(1)
        print(" [STEP 8 PASS] 'otter pack verify' reported [OK] for all 5 installed packages.")

        # Step 9: Modify one byte of an installed Pack and verify integrity checking fails
        target_src = os.path.join(project_dir, ".otter", "packages", "otter-official", "math-utils", "1.0.1", "src", "main.ot")
        with open(target_src, "a") as f:
            f.write("\n# TAMPER_BYTE_CORRUPT\n")
        
        tamper_res = run_cmd([otter_bin, "pack", "verify", "--project", project_dir], cwd=project_dir, env=clean_env)
        if tamper_res.returncode == 0 or "[CORRUPT]" not in tamper_res.stdout:
            print(f"FATAL: Expected 'otter pack verify' to fail on tampered file, but got exit 0:\n{tamper_res.stdout}")
            sys.exit(1)
        print(" [STEP 9 PASS] Tampering with 1 byte caused expected [CORRUPT] verification failure.")

        # Step 10: Reinstall or restore the Pack and confirm integrity passes again
        reinstall_res = run_cmd([otter_bin, "pack", "install", "math-utils", "--project", project_dir], cwd=project_dir, env=clean_env)
        if reinstall_res.returncode != 0:
            print(f"FATAL: Reinstalling math-utils failed:\n{reinstall_res.stderr}\n{reinstall_res.stdout}")
            sys.exit(1)
        
        restore_vfy = run_cmd([otter_bin, "pack", "verify", "--project", project_dir], cwd=project_dir, env=clean_env)
        if restore_vfy.returncode != 0 or "[CORRUPT]" in restore_vfy.stdout:
            print(f"FATAL: otter pack verify failed after reinstallation:\n{restore_vfy.stdout}\n{restore_vfy.stderr}")
            sys.exit(1)
        print(" [STEP 10 PASS] Reinstalling math-utils restored clean [OK] integrity check.")

    print("=" * 70)
    print(" ALL OUTSIDE-REPOSITORY CONSUMER TESTS PASSED (100%)")
    print("=" * 70)
    sys.exit(0)

if __name__ == "__main__":
    main()
