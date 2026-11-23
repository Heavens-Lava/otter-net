#!/usr/bin/env python3
"""
tests/docs/test_component_authoring_guide.py
Automated Documentation Smoke Test for docs/COMPONENT_AUTHORING_GUIDE.md
(Phase 11 Milestone 4).

Extracts the actual tagged code blocks directly from docs/COMPONENT_AUTHORING_GUIDE.md
and executes all 10 tutorial stages end-to-end against a freshly packaged, isolated
Otter distribution OUTSIDE the repository so guide examples and CLI commands cannot
silently drift out of date.

Verified stages:
1. Extract all required code blocks from docs/COMPONENT_AUTHORING_GUIDE.md.
2. Scaffold third-party pack `acme-tools/budget-guard` in an external library.
3. Run behavioral + scoping isolation tests (`tests/test.ot`) and demo (`examples/demo.ot`).
4. Run negative contract verification checks (`VARIABLE_COLLISION_RISK`,
   `PARAMETER_NAME_MISMATCH`, `MISSING_FUNCTION`, `MISLEADING_CONTRACT`,
   `PREFIX_COLLISION`).
5. Run `otter pack verify --contracts`, `--contracts --json`, and `otter pack contract`.
6. Compose `acme-tools/budget-guard` + `otter-official/text-utils` +
   `otter-official/file-helpers` (with transitive `otter-official/math-utils`) via
   the extracted `workflows/budget-report.workflow.json` (`--explain`, `--preview`,
   `--apply --yes`), proving zero modifications to `CompositionPlanner.cs`.
7. Run `otter check`, `otter pack verify --contracts`, and `otter run src/main.ot`,
   asserting stdout (`0075`) and persisted `budget_report.json`.
8. Package via `otter pack pack --out ...` and dry-run publish via
   `otter pack publish ... --dry-run`.
9. Consume outside the repository via `~/.otter/library` (without `OTTER_LIBRARY_PATH`),
   running `otter pack install`, `otter pack verify --contracts`, `otter check`, and
   `otter run` on the extracted `consumer/app.ot`.
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
GUIDE_PATH = os.path.join(REPO_ROOT, "docs", "COMPONENT_AUTHORING_GUIDE.md")
PACKAGE_SCRIPT = os.path.join(REPO_ROOT, "tools", "package-distribution.sh")


def run_cmd(cmd, cwd=None, env=None):
    return subprocess.run(
        cmd,
        cwd=cwd,
        env=env,
        capture_output=True,
        text=True,
    )


def extract_doc_blocks(md_path):
    with open(md_path, "r", encoding="utf-8") as f:
        text = f.read()

    pattern = re.compile(
        r"<!--\s*doc-extract:\s*([^\s>]+)\s*-->\s*\n```[a-zA-Z0-9_-]*\n(.*?)\n```",
        re.DOTALL,
    )
    blocks = {}
    for match in pattern.finditer(text):
        key = match.group(1).strip()
        content = match.group(2) + "\n"
        assert key not in blocks, f"Duplicate doc-extract key in guide: {key}"
        blocks[key] = content
    return blocks


def write_text(path, content):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)


def main():
    print("=" * 76)
    print(" Otter Phase 11 Milestone 4: Component Authoring Guide Smoke Test")
    print("=" * 76)

    assert os.path.isfile(GUIDE_PATH), f"Guide missing: {GUIDE_PATH}"
    blocks = extract_doc_blocks(GUIDE_PATH)
    required_keys = [
        "packs/acme-tools/budget-guard/src/main.ot",
        "packs/acme-tools/budget-guard/otter.pack.json",
        "packs/acme-tools/budget-guard/tests/test.ot",
        "packs/acme-tools/budget-guard/examples/demo.ot",
        "negative/unprefixed_main.ot",
        "workflows/budget-report.workflow.json",
        "consumer/app.ot",
    ]
    for key in required_keys:
        assert key in blocks, f"Expected doc-extract block '{key}' in {GUIDE_PATH}"
    print(f"  [PASS] Extracted all {len(required_keys)} executable blocks from docs/COMPONENT_AUTHORING_GUIDE.md.")

    # Verify CompositionPlanner.cs has zero hardcoded references to our tutorial pack
    planner_src = open(os.path.join(REPO_ROOT, "src", "Otter.Cli", "CompositionPlanner.cs"), "r", encoding="utf-8").read()
    assert "acme-tools" not in planner_src
    assert "budget-guard" not in planner_src
    assert "buildBudgetSummary" not in planner_src

    with tempfile.TemporaryDirectory(prefix="otter_m4_install_") as install_dir, \
         tempfile.TemporaryDirectory(prefix="otter_m4_home_") as fake_home, \
         tempfile.TemporaryDirectory(prefix="otter_m4_work_") as workspace:

        print("\n--> Packaging fresh isolated Otter distribution...")
        pkg_res = run_cmd([PACKAGE_SCRIPT, install_dir], cwd=REPO_ROOT)
        assert pkg_res.returncode == 0, f"package-distribution.sh failed:\n{pkg_res.stderr}\n{pkg_res.stdout}"

        otter_bin = os.path.join(install_dir, "bin", "otter")
        bundled_lib = os.path.join(install_dir, "library")
        assert os.path.isfile(otter_bin), f"Missing otter binary at {otter_bin}"
        assert os.path.isdir(bundled_lib), f"Missing bundled library at {bundled_lib}"

        base_env = os.environ.copy()
        base_env.pop("OTTER_LIBRARY_PATH", None)
        base_env["HOME"] = fake_home

        # Stage 1 & 2 & 3: Scaffold my-otter-library with bundled catalog + acme-tools/budget-guard
        print("\n[Steps 1-3] Scaffolding external library and acme-tools/budget-guard from guide...")
        my_lib = os.path.join(workspace, "my-otter-library")
        shutil.copytree(bundled_lib, my_lib)
        pack_dir = os.path.join(my_lib, "packs", "acme-tools", "budget-guard")

        for rel in [
            "packs/acme-tools/budget-guard/src/main.ot",
            "packs/acme-tools/budget-guard/otter.pack.json",
            "packs/acme-tools/budget-guard/tests/test.ot",
            "packs/acme-tools/budget-guard/examples/demo.ot",
        ]:
            write_text(os.path.join(my_lib, rel), blocks[rel])

        lib_env = dict(base_env)
        lib_env["OTTER_LIBRARY_PATH"] = my_lib

        # Stage 4: Run behavioral tests and standalone demo
        print("\n[Step 4] Running extracted behavioral tests, scoping assertions, and demo...")
        test_res = run_cmd([otter_bin, "run", os.path.join(pack_dir, "tests", "test.ot")], cwd=workspace, env=lib_env)
        assert test_res.returncode == 0, f"Pack tests/test.ot failed:\n{test_res.stdout}\n{test_res.stderr}"
        assert "All acme-tools/budget-guard tests passed!" in test_res.stdout

        demo_res = run_cmd([otter_bin, "run", os.path.join(pack_dir, "examples", "demo.ot")], cwd=workspace, env=lib_env)
        assert demo_res.returncode == 0, f"Pack examples/demo.ot failed:\n{demo_res.stdout}\n{demo_res.stderr}"
        assert "Platform Infra" in demo_res.stdout
        assert "82" in demo_res.stdout
        print("  [PASS] Extracted tests/test.ot (behavioral + 5 caller vars) and examples/demo.ot executed cleanly.")

        # Stage 4.3: Negative contract diagnostic tests documented in guide
        print("\n[Step 4.3] Verifying documented negative contract diagnostics...")
        with tempfile.TemporaryDirectory(prefix="otter_m4_neg_") as neg_dir:
            # 1. VARIABLE_COLLISION_RISK using extracted broken source
            bad_pfx_dir = os.path.join(neg_dir, "bad_pfx")
            shutil.copytree(pack_dir, bad_pfx_dir)
            write_text(os.path.join(bad_pfx_dir, "src", "main.ot"), blocks["negative/unprefixed_main.ot"])
            r_pfx = run_cmd([otter_bin, "pack", "verify", bad_pfx_dir, "--contracts"], env=lib_env)
            assert r_pfx.returncode == 2 and "VARIABLE_COLLISION_RISK" in r_pfx.stdout, (
                f"Expected VARIABLE_COLLISION_RISK (rc=2), got rc={r_pfx.returncode}:\n{r_pfx.stdout}"
            )

            # 2. PARAMETER_NAME_MISMATCH
            bad_param_dir = os.path.join(neg_dir, "bad_param")
            shutil.copytree(pack_dir, bad_param_dir)
            m = json.loads(blocks["packs/acme-tools/budget-guard/otter.pack.json"])
            m["provides"]["operations"][0]["parameters"][0]["name"] = "wrongParamName"
            write_text(os.path.join(bad_param_dir, "otter.pack.json"), json.dumps(m, indent=2))
            r_param = run_cmd([otter_bin, "pack", "verify", bad_param_dir, "--contracts"], env=lib_env)
            assert r_param.returncode == 2 and "PARAMETER_NAME_MISMATCH" in r_param.stdout

            # 3. MISSING_FUNCTION
            bad_fn_dir = os.path.join(neg_dir, "bad_fn")
            shutil.copytree(pack_dir, bad_fn_dir)
            m2 = json.loads(blocks["packs/acme-tools/budget-guard/otter.pack.json"])
            m2["provides"]["operations"].append({
                "name": "phantomOp",
                "description": "Missing function",
                "parameters": [],
                "returns": {"type": "number"},
                "sideEffects": ["none"],
                "errorMode": "returns-fallback",
            })
            write_text(os.path.join(bad_fn_dir, "otter.pack.json"), json.dumps(m2, indent=2))
            r_fn = run_cmd([otter_bin, "pack", "verify", bad_fn_dir, "--contracts"], env=lib_env)
            assert r_fn.returncode == 2 and "MISSING_FUNCTION" in r_fn.stdout

            # 4. MISLEADING_CONTRACT
            bad_mis_dir = os.path.join(neg_dir, "bad_mis")
            shutil.copytree(pack_dir, bad_mis_dir)
            m3 = json.loads(blocks["packs/acme-tools/budget-guard/otter.pack.json"])
            m3["provides"]["operations"][0]["errorMode"] = "returns-false"
            write_text(os.path.join(bad_mis_dir, "otter.pack.json"), json.dumps(m3, indent=2))
            r_mis = run_cmd([otter_bin, "pack", "verify", bad_mis_dir, "--contracts"], env=lib_env)
            assert r_mis.returncode == 2 and "MISLEADING_CONTRACT" in r_mis.stdout

            # 5. PREFIX_COLLISION against _mu_
            bad_lib = os.path.join(neg_dir, "bad_lib")
            shutil.copytree(my_lib, bad_lib)
            m4 = json.loads(blocks["packs/acme-tools/budget-guard/otter.pack.json"])
            m4["variablePrefix"] = "_mu_"
            write_text(os.path.join(bad_lib, "packs", "acme-tools", "budget-guard", "otter.pack.json"), json.dumps(m4, indent=2))
            bad_lib_env = dict(base_env)
            bad_lib_env["OTTER_LIBRARY_PATH"] = bad_lib
            r_col = run_cmd([otter_bin, "compose", "plan", "--packs", "acme-tools/budget-guard"], cwd=neg_dir, env=bad_lib_env)
            assert r_col.returncode != 0 and ("PREFIX_COLLISION" in r_col.stdout or "PREFIX_COLLISION" in r_col.stderr)

        print("  [PASS] All 5 documented negative diagnostics verified (VARIABLE_COLLISION_RISK, PARAMETER_NAME_MISMATCH, MISSING_FUNCTION, MISLEADING_CONTRACT, PREFIX_COLLISION).")

        # Stage 5: Validate and inspect contracts using Otter CLI
        print("\n[Step 5] Validating contracts via 'otter pack verify --contracts' and 'otter pack contract'...")
        v_txt = run_cmd([otter_bin, "pack", "verify", pack_dir, "--contracts"], env=lib_env)
        assert v_txt.returncode == 0, f"pack verify --contracts failed:\n{v_txt.stdout}\n{v_txt.stderr}"
        assert "Contract: [OK] (3 operations verified)" in v_txt.stdout

        v_json = run_cmd([otter_bin, "pack", "verify", pack_dir, "--contracts", "--json"], env=lib_env)
        assert v_json.returncode == 0
        v_data = json.loads(v_json.stdout)
        assert v_data["contract"]["status"] == "VALID"
        assert v_data["contract"]["variablePrefix"] == "_bg_"
        assert v_data["contract"]["operationsCount"] == 3

        c_txt = run_cmd([otter_bin, "pack", "contract", pack_dir], env=lib_env)
        assert c_txt.returncode == 0
        assert "Variable Prefix: _bg_" in c_txt.stdout
        assert "Validation Scope:   Structural AST" in c_txt.stdout
        print("  [PASS] Contract validation (text + JSON) and contract inspection verified.")

        # Stage 7, 8, 9: Declarative multi-pack workflow preview, explain, apply, check, run
        print("\n[Steps 7-9] Executing extracted multi-pack workflow (explain, preview, apply, check, run)...")
        wf_path = os.path.join(workspace, "workflows", "budget-report.workflow.json")
        write_text(wf_path, blocks["workflows/budget-report.workflow.json"])

        init_res = run_cmd([otter_bin, "new", "console", "budget-demo"], cwd=workspace, env=lib_env)
        assert init_res.returncode == 0, f"otter new failed: {init_res.stderr}"
        demo_proj = os.path.join(workspace, "budget-demo")

        exp_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--explain"], cwd=demo_proj, env=lib_env)
        assert exp_res.returncode == 0, f"--explain failed:\n{exp_res.stdout}\n{exp_res.stderr}"
        assert "acme-tools/budget-guard" in exp_res.stdout
        assert "otter-official/math-utils" in exp_res.stdout
        assert "number_to_text" in exp_res.stdout

        prev_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--preview"], cwd=demo_proj, env=lib_env)
        assert prev_res.returncode == 0, f"--preview failed:\n{prev_res.stdout}\n{prev_res.stderr}"
        assert 'use "${import:otter-official/math-utils}"' in prev_res.stdout
        assert 'use "${import:acme-tools/budget-guard}"' in prev_res.stdout

        apply_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--apply", "--yes"], cwd=demo_proj, env=lib_env)
        assert apply_res.returncode == 0, f"--apply failed:\n{apply_res.stdout}\n{apply_res.stderr}"

        chk_res = run_cmd([otter_bin, "check", "src/main.ot"], cwd=demo_proj, env=lib_env)
        assert chk_res.returncode == 0, f"otter check failed:\n{chk_res.stdout}\n{chk_res.stderr}"

        proj_v = run_cmd([otter_bin, "pack", "verify", "--contracts", "--json"], cwd=demo_proj, env=lib_env)
        assert proj_v.returncode == 0, f"project pack verify failed:\n{proj_v.stdout}\n{proj_v.stderr}"
        installed_packs = json.loads(proj_v.stdout)
        assert len(installed_packs) == 4, f"Expected 4 installed packs (incl. transitive math-utils), got {len(installed_packs)}"
        assert all(p["contract"]["status"] == "VALID" for p in installed_packs)

        run_res = run_cmd([otter_bin, "run", "src/main.ot"], cwd=demo_proj, env=lib_env)
        assert run_res.returncode == 0, f"otter run failed:\n{run_res.stdout}\n{run_res.stderr}"
        assert "0075" in run_res.stdout, f"Expected '0075' in stdout, got:\n{run_res.stdout}"

        report_path = os.path.join(demo_proj, "budget_report.json")
        assert os.path.isfile(report_path), "Expected budget_report.json to be persisted"
        with open(report_path, "r", encoding="utf-8") as f:
            report_data = json.load(f)
        assert report_data == {
            "department": "Engineering Cloud",
            "spent": 750,
            "limit": 1000,
            "remaining": 250,
            "utilizationPercent": 75,
            "withinBudget": True,
        }, f"Unexpected persisted budget_report.json: {report_data}"
        print("  [PASS] Multi-pack workflow applied (4 packs incl. transitive math-utils), verified, executed ('0075'), and persisted exact JSON.")

        # Stage 10.1: Package into .otterpack archive and dry-run publish
        print("\n[Step 10.1] Packaging via 'otter pack pack' and dry-run publishing...")
        dist_dir = os.path.join(workspace, "dist")
        os.makedirs(dist_dir, exist_ok=True)
        archive_path = os.path.join(dist_dir, "acme-tools-budget-guard-1.0.0.otterpack")

        pack_cmd = run_cmd([otter_bin, "pack", "pack", pack_dir, "--output", archive_path], cwd=workspace, env=lib_env)
        assert pack_cmd.returncode == 0, f"otter pack pack failed:\n{pack_cmd.stdout}\n{pack_cmd.stderr}"
        assert os.path.isfile(archive_path), f"Expected archive at {archive_path}"

        pub_cmd = run_cmd([otter_bin, "pack", "publish", archive_path, "--dry-run"], cwd=workspace, env=lib_env)
        assert pub_cmd.returncode == 0, f"otter pack publish --dry-run failed:\n{pub_cmd.stdout}\n{pub_cmd.stderr}"
        print("  [PASS] Archive packaged (including automated pack tests) and publish --dry-run succeeded.")

        # Stage 10.2: Consume outside repository via ~/.otter/library (NO OTTER_LIBRARY_PATH!)
        print("\n[Step 10.2] Consuming pack outside repo via ~/.otter/library (without OTTER_LIBRARY_PATH)...")
        user_lib = os.path.join(fake_home, ".otter", "library")
        shutil.copytree(my_lib, user_lib)

        init_manual = run_cmd([otter_bin, "new", "console", "manual-consumer"], cwd=workspace, env=base_env)
        assert init_manual.returncode == 0
        manual_proj = os.path.join(workspace, "manual-consumer")

        inst1 = run_cmd([otter_bin, "pack", "install", "otter-official/math-utils"], cwd=manual_proj, env=base_env)
        assert inst1.returncode == 0, f"install math-utils failed:\n{inst1.stdout}\n{inst1.stderr}"
        inst2 = run_cmd([otter_bin, "pack", "install", "acme-tools/budget-guard"], cwd=manual_proj, env=base_env)
        assert inst2.returncode == 0, f"install budget-guard failed:\n{inst2.stdout}\n{inst2.stderr}"

        man_v = run_cmd([otter_bin, "pack", "verify", "--contracts"], cwd=manual_proj, env=base_env)
        assert man_v.returncode == 0, f"manual consumer verify failed:\n{man_v.stdout}\n{man_v.stderr}"

        app_ot = os.path.join(manual_proj, "src", "app.ot")
        write_text(app_ot, blocks["consumer/app.ot"])

        chk_man = run_cmd([otter_bin, "check", "src/app.ot"], cwd=manual_proj, env=base_env)
        assert chk_man.returncode == 0, f"manual consumer check failed:\n{chk_man.stdout}\n{chk_man.stderr}"

        run_man = run_cmd([otter_bin, "run", "src/app.ot"], cwd=manual_proj, env=base_env)
        assert run_man.returncode == 0, f"manual consumer run failed:\n{run_man.stdout}\n{run_man.stderr}"
        assert "Manual Consumer Utilization: 60" in run_man.stdout
        assert "Manual Consumer Remaining: 400" in run_man.stdout
        print("  [PASS] Installed & executed acme-tools/budget-guard from ~/.otter/library without OTTER_LIBRARY_PATH.")

    print("\n" + "=" * 76)
    print(" ALL COMPONENT AUTHORING GUIDE SMOKE TESTS PASSED (100%)")
    print("=" * 76)


if __name__ == "__main__":
    main()
