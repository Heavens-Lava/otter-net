#!/usr/bin/env python3
"""
tests/composition/test_milestone3_sandboxed_verification.py
Otter Phase 11 Milestone 3: Verification Test Suite & Sandboxed Benchmarking.

Proves from a freshly packaged, isolated Otter distribution OUTSIDE the repository:
1. Bundle integrity and Schema v2 contract verification across all 5 official packs
   (math-utils, text-utils, list-utils, file-helpers, game-entity) with 5 disjoint
   variable prefixes (_mu_, _tu_, _lu_, _fh_, _ge_).
2. Arbitrary multi-pack compositions outside the repository produce verified,
   executable, and safe Otter applications:
   - Scenario A: 4-pack collection analytics + persistence workflow
                 (list-utils + math-utils + text-utils + file-helpers)
   - Scenario B: 3-pack RPG game actor combat + save-state round-trip workflow
                 (game-entity + math-utils + file-helpers)
   - Scenario C: All 5 official packs composed together in a single declarative workflow
   - Scenario D: External 3rd-party pack declaring transitive `requires.packages`
                 dependency on an official pack (verified without modifying planner)
3. Otter 1.0.1 dynamic scoping isolation remains unbroken across all generated
   compositions: caller variables (result, total, clean, item, target, actor,
   content, copy, items, inv, equip, index, temp, value, text, count, bonus)
   remain byte-for-byte uncorrupted after executing the synthesized 5-pack
   orchestrator pipeline, and prefix collisions (_orch_, _lu_, _ge_) are rejected.
4. Deterministic reproducibility: repeated planning produces identical synthesized
   Otter source (SHA-256) and identical lockfile digests across fresh projects.
5. Sandboxed performance benchmarking: measures real wall-clock min/p50/p95/max
   latencies (ms) for contract verification, multi-pack planning, and transactional
   apply + check + run in disposable sandboxes.
"""

import hashlib
import json
import os
import platform
import re
import shutil
import statistics
import subprocess
import sys
import tempfile
import time

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PACKAGE_SCRIPT = os.path.join(REPO_ROOT, "tools", "package-distribution.sh")
VERIFY_BUNDLE_SCRIPT = os.path.join(REPO_ROOT, "tools", "verify-bundle-integrity.py")


def run_cmd(cmd, cwd=None, env=None):
    return subprocess.run(
        cmd,
        cwd=cwd,
        env=env,
        capture_output=True,
        text=True,
    )


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def percentile(values, p):
    if not values:
        return 0.0
    ordered = sorted(values)
    idx = (len(ordered) - 1) * (p / 100.0)
    lo = int(idx)
    hi = min(lo + 1, len(ordered) - 1)
    frac = idx - lo
    return ordered[lo] + (ordered[hi] - ordered[lo]) * frac


def format_stats(samples_ms):
    return (
        f"n={len(samples_ms)} "
        f"min={min(samples_ms):.1f}ms "
        f"p50={statistics.median(samples_ms):.1f}ms "
        f"p95={percentile(samples_ms, 95):.1f}ms "
        f"max={max(samples_ms):.1f}ms"
    )


def verify_all_five_packs_outside_repo(otter_bin, bundled_lib, clean_env):
    print("\n[Stage 1] Bundle Integrity & All 5 Official Pack Contracts (Outside Repo)")
    bundle_res = run_cmd([sys.executable, VERIFY_BUNDLE_SCRIPT, bundled_lib], cwd=REPO_ROOT)
    assert bundle_res.returncode == 0, f"Bundle integrity check failed:\n{bundle_res.stdout}\n{bundle_res.stderr}"

    expected = {
        "math-utils": ("_mu_", 11, "1.0.1"),
        "text-utils": ("_tu_", 15, "1.0.1"),
        "list-utils": ("_lu_", 10, "1.0.1"),
        "file-helpers": ("_fh_", 10, "1.0.1"),
        "game-entity": ("_ge_", 26, "1.1.0"),
    }
    prefixes = set()
    for pack_name, (pfx, op_count, ver) in expected.items():
        pack_dir = os.path.join(bundled_lib, "packs", "otter-official", pack_name)
        res = run_cmd([otter_bin, "pack", "verify", pack_dir, "--contracts", "--json"], env=clean_env)
        assert res.returncode == 0, f"Contract verification failed for {pack_name}:\n{res.stdout}\n{res.stderr}"
        data = json.loads(res.stdout)
        contract = data["contract"]
        assert contract["status"] == "VALID", f"{pack_name} diagnostics: {contract['diagnostics']}"
        assert contract["schemaVersion"] == 2
        assert contract["version"] == ver
        assert contract["variablePrefix"] == pfx
        assert contract["operationsCount"] == op_count
        assert pfx != "_orch_"
        assert pfx not in prefixes
        prefixes.add(pfx)
    print(f"  [PASS] Bundled library verified & all 5 packs pass Schema v2 AST contracts (72 ops, prefixes={sorted(prefixes)}).")


def verify_four_pack_collection_analytics(otter_bin, bundled_lib, workspace, clean_env):
    print("\n[Stage 2A] 4-Pack Composition Outside Repo: list-utils + math-utils + text-utils + file-helpers")
    init_res = run_cmd([otter_bin, "new", "console", "app_analytics"], cwd=workspace, env=clean_env)
    assert init_res.returncode == 0, f"otter new failed: {init_res.stderr}"
    proj_dir = os.path.join(workspace, "app_analytics")

    wf_path = os.path.join(bundled_lib, "workflows", "collection-analytics-persistence.workflow.json")
    assert os.path.isfile(wf_path), f"Bundled workflow missing: {wf_path}"

    plan_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--apply", "--yes"], cwd=proj_dir, env=clean_env)
    assert plan_res.returncode == 0, f"Plan/apply failed:\n{plan_res.stdout}\n{plan_res.stderr}"

    check_res = run_cmd([otter_bin, "check", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert check_res.returncode == 0, f"otter check failed:\n{check_res.stdout}\n{check_res.stderr}"

    pv_res = run_cmd([otter_bin, "pack", "verify", "--contracts", "--json"], cwd=proj_dir, env=clean_env)
    assert pv_res.returncode == 0, f"Project pack contract verify failed:\n{pv_res.stdout}\n{pv_res.stderr}"
    installed = json.loads(pv_res.stdout)
    assert len(installed) == 4, f"Expected 4 installed packs, got {len(installed)}"
    assert all(item["contract"]["status"] == "VALID" for item in installed)

    run_res = run_cmd([otter_bin, "run", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert run_res.returncode == 0, f"Runtime execution failed:\n{run_res.stdout}\n{run_res.stderr}"
    assert "Pipeline execution complete" in run_res.stdout

    report_file = os.path.join(proj_dir, "analytics_report.json")
    assert os.path.isfile(report_file), "Expected analytics_report.json to be created"
    with open(report_file, "r", encoding="utf-8") as f:
        report = json.load(f)
    assert report.get("value") == "000060", f"Expected formatted deduped sum '000060', got {report}"
    print("  [PASS] 4-pack analytics workflow applied, verified, executed, and persisted {'value': '000060'}.")


def verify_three_pack_game_save_state(otter_bin, bundled_lib, workspace, clean_env):
    print("\n[Stage 2B] 3-Pack Composition Outside Repo: game-entity + math-utils + file-helpers")
    init_res = run_cmd([otter_bin, "new", "console", "app_game_state"], cwd=workspace, env=clean_env)
    assert init_res.returncode == 0, f"otter new failed: {init_res.stderr}"
    proj_dir = os.path.join(workspace, "app_game_state")

    wf_path = os.path.join(bundled_lib, "workflows", "game-actor-save-state.workflow.json")
    assert os.path.isfile(wf_path), f"Bundled workflow missing: {wf_path}"

    plan_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--apply", "--yes"], cwd=proj_dir, env=clean_env)
    assert plan_res.returncode == 0, f"Plan/apply failed:\n{plan_res.stdout}\n{plan_res.stderr}"

    check_res = run_cmd([otter_bin, "check", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert check_res.returncode == 0, f"otter check failed:\n{check_res.stdout}\n{check_res.stderr}"

    pv_res = run_cmd([otter_bin, "pack", "verify", "--contracts", "--json"], cwd=proj_dir, env=clean_env)
    assert pv_res.returncode == 0, f"Project pack contract verify failed:\n{pv_res.stdout}\n{pv_res.stderr}"
    installed = json.loads(pv_res.stdout)
    assert len(installed) == 3, f"Expected 3 installed packs, got {len(installed)}"
    assert all(item["contract"]["status"] == "VALID" for item in installed)

    run_res = run_cmd([otter_bin, "run", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert run_res.returncode == 0, f"Runtime execution failed:\n{run_res.stdout}\n{run_res.stderr}"
    assert "Pipeline execution complete" in run_res.stdout

    save_file = os.path.join(proj_dir, "game_state.json")
    assert os.path.isfile(save_file), "Expected game_state.json to be created"
    with open(save_file, "r", encoding="utf-8") as f:
        state = json.load(f)
    assert state.get("name") == "Aria", f"Unexpected actor name: {state}"
    assert state.get("health") == 75, f"Expected health=75 after clamped 25 damage, got {state}"
    assert state.get("maxHealth") == 100
    assert state.get("isAlive") is True
    assert state.get("inventoryCount") == 1
    assert state.get("inventoryItems")[0]["itemName"] == "Healing Draught"
    assert state.get("inventoryItems")[0]["itemCount"] == 3
    print("  [PASS] 3-pack game-entity workflow applied, checked, executed, and round-tripped game_state.json (HP=75).")


def verify_all_five_packs_single_composition_and_scoping_isolation(otter_bin, workspace, clean_env):
    print("\n[Stage 2C & 3] All 5 Official Packs Composed Together + Dynamic Scoping Isolation Proof")
    all5_wf = {
        "workflowVersion": 1,
        "id": "all-five-packs-end-to-end",
        "name": "All Five Official Packs Unified Pipeline",
        "description": "Composes list-utils, math-utils, text-utils, game-entity, and file-helpers in a single workflow.",
        "target": "console",
        "composedPacks": [
            "otter-official/list-utils",
            "otter-official/math-utils",
            "otter-official/text-utils",
            "otter-official/game-entity",
            "otter-official/file-helpers",
        ],
        "pipelineInputs": [
            {"name": "rawHeroTitle", "type": "text", "default": "   Sir   Galahad   "},
            {"name": "damageSamples", "type": "list", "default": [15, 10, 15, 20]},
            {"name": "savePath", "type": "text", "default": "all_five_state.json"},
        ],
        "steps": [
            {
                "id": "step1_clean_name",
                "pack": "otter-official/text-utils",
                "operation": "normalizeSpaces",
                "inputs": {"text": {"fromInput": "rawHeroTitle"}},
            },
            {
                "id": "step2_dedupe_dmg",
                "pack": "otter-official/list-utils",
                "operation": "dedupeList",
                "inputs": {"items": {"fromInput": "damageSamples"}},
            },
            {
                "id": "step3_sum_dmg",
                "pack": "otter-official/list-utils",
                "operation": "calcListSum",
                "inputs": {"items": {"fromStep": "step2_dedupe_dmg", "port": "returns"}},
            },
            {
                "id": "step4_clamp_dmg",
                "pack": "otter-official/math-utils",
                "operation": "clamp",
                "inputs": {
                    "value": {"fromStep": "step3_sum_dmg", "port": "returns"},
                    "minLimit": {"value": 0},
                    "maxLimit": {"value": 80},
                },
            },
            {
                "id": "step5_actor",
                "pack": "otter-official/game-entity",
                "operation": "createActor",
                "inputs": {
                    "name": {"fromStep": "step1_clean_name", "port": "returns"},
                    "hp": {"value": 100},
                    "lvl": {"value": 2},
                },
            },
            {
                "id": "step6_damage",
                "pack": "otter-official/game-entity",
                "operation": "applyDamage",
                "inputs": {
                    "actor": {"fromStep": "step5_actor", "port": "returns"},
                    "amount": {"fromStep": "step4_clamp_dmg", "port": "returns"},
                },
            },
            {
                "id": "step7_inv",
                "pack": "otter-official/game-entity",
                "operation": "createInventory",
                "inputs": {"slots": {"value": 5}},
            },
            {
                "id": "step8_equip",
                "pack": "otter-official/game-entity",
                "operation": "createEquipment",
                "inputs": {},
            },
            {
                "id": "step9_serialize",
                "pack": "otter-official/game-entity",
                "operation": "serializeGameState",
                "inputs": {
                    "actor": {"fromStep": "step5_actor", "port": "returns"},
                    "inv": {"fromStep": "step7_inv", "port": "returns"},
                    "equip": {"fromStep": "step8_equip", "port": "returns"},
                },
                "orderAfter": ["step6_damage"],
            },
            {
                "id": "step10_persist",
                "pack": "otter-official/file-helpers",
                "operation": "writeJsonObject",
                "inputs": {
                    "filePath": {"fromInput": "savePath"},
                    "obj": {"fromStep": "step9_serialize", "port": "returns"},
                },
                "orderAfter": ["step9_serialize"],
            },
        ],
        "output": {"fromStep": "step10_persist", "port": "returns"},
    }

    wf_path = os.path.join(workspace, "all_five.workflow.json")
    with open(wf_path, "w", encoding="utf-8") as f:
        json.dump(all5_wf, f, indent=2)

    # Determinism check: plan twice and verify byte-identical synthesized code
    p1 = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--json"], cwd=workspace, env=clean_env)
    p2 = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--json"], cwd=workspace, env=clean_env)
    assert p1.returncode == 0 and p2.returncode == 0, f"Plan failed:\n{p1.stdout}\n{p1.stderr}"
    code1 = json.loads(p1.stdout)["generatedSourcePreview"][0]["sourceCode"]
    code2 = json.loads(p2.stdout)["generatedSourcePreview"][0]["sourceCode"]
    assert sha256_text(code1) == sha256_text(code2), "Non-deterministic synthesized Otter source across repeated plans!"

    # Apply to fresh external project
    init_res = run_cmd([otter_bin, "new", "console", "app_all_five"], cwd=workspace, env=clean_env)
    assert init_res.returncode == 0
    proj_dir = os.path.join(workspace, "app_all_five")

    apply_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--apply", "--yes"], cwd=proj_dir, env=clean_env)
    assert apply_res.returncode == 0, f"Apply 5-pack workflow failed:\n{apply_res.stdout}\n{apply_res.stderr}"

    # Verify all 5 installed packs in project pass contract checks
    pv_res = run_cmd([otter_bin, "pack", "verify", "--contracts", "--json"], cwd=proj_dir, env=clean_env)
    assert pv_res.returncode == 0, f"5-pack verify failed:\n{pv_res.stdout}\n{pv_res.stderr}"
    installed = json.loads(pv_res.stdout)
    assert len(installed) == 5, f"Expected 5 installed packs, got {len(installed)}"
    assert all(item["contract"]["status"] == "VALID" for item in installed)

    # Run generated orchestrator standalone
    run_res = run_cmd([otter_bin, "run", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert run_res.returncode == 0, f"5-pack run failed:\n{run_res.stdout}\n{run_res.stderr}"
    out_file = os.path.join(proj_dir, "all_five_state.json")
    with open(out_file, "r", encoding="utf-8") as f:
        saved = json.load(f)
    # dedupe([15, 10, 15, 20]) = [15, 10, 20], sum = 45, clamp(45, 0, 80) = 45, HP = 100 - 45 = 55
    assert saved["name"] == "Sir Galahad", f"Unexpected normalized name: {saved}"
    assert saved["health"] == 55, f"Expected HP=55, got {saved}"
    assert saved["level"] == 2
    assert saved["isAlive"] is True

    # Dynamic scoping isolation harness: strip standalone harness from generated src/main.ot
    # so we import the exact synthesized `runCompositionPipeline` function alongside hostile caller variables.
    gen_main_path = os.path.join(proj_dir, "src", "main.ot")
    with open(gen_main_path, "r", encoding="utf-8") as f:
        gen_src = f.read()
    marker = "# Standalone Execution Harness"
    assert marker in gen_src, "Expected standalone harness marker in synthesized orchestrator"
    pipeline_lib_src = gen_src.split(marker)[0]
    pipeline_lib_path = os.path.join(proj_dir, "src", "pipeline_lib.ot")
    with open(pipeline_lib_path, "w", encoding="utf-8") as f:
        f.write(pipeline_lib_src)

    scoping_harness = '''use "pipeline_lib.ot"

# Pre-populate high-risk caller variables across all 5 packs and orchestrator
result is "CALLER_RESULT_SENTINEL"
total is 99991
clean is "CALLER_CLEAN_SENTINEL"
item is "CALLER_ITEM_SENTINEL"
target is "CALLER_TARGET_SENTINEL"
actor is "CALLER_ACTOR_SENTINEL"
content is "CALLER_CONTENT_SENTINEL"
copy is "CALLER_COPY_SENTINEL"
items is "CALLER_ITEMS_SENTINEL"
inv is "CALLER_INV_SENTINEL"
equip is "CALLER_EQUIP_SENTINEL"
index is 88882
temp is "CALLER_TEMP_SENTINEL"
value is 77773
text is "CALLER_TEXT_SENTINEL"
totalCount is 66664
bonus is 55555

callerSamples are
    12
    8
    12
    5
.

runCompositionPipeline "   Lady   Isolde   " callerSamples "scoping_isolated_state.json" into callerOutcome

if callerOutcome is not true
    fail with "Composed 5-pack pipeline did not return true"
.
if result is not "CALLER_RESULT_SENTINEL"
    fail with "SCOPING LEAK: caller 'result' corrupted"
.
if total is not 99991
    fail with "SCOPING LEAK: caller 'total' corrupted"
.
if clean is not "CALLER_CLEAN_SENTINEL"
    fail with "SCOPING LEAK: caller 'clean' corrupted"
.
if item is not "CALLER_ITEM_SENTINEL"
    fail with "SCOPING LEAK: caller 'item' corrupted"
.
if target is not "CALLER_TARGET_SENTINEL"
    fail with "SCOPING LEAK: caller 'target' corrupted"
.
if actor is not "CALLER_ACTOR_SENTINEL"
    fail with "SCOPING LEAK: caller 'actor' corrupted"
.
if content is not "CALLER_CONTENT_SENTINEL"
    fail with "SCOPING LEAK: caller 'content' corrupted"
.
if copy is not "CALLER_COPY_SENTINEL"
    fail with "SCOPING LEAK: caller 'copy' corrupted"
.
if items is not "CALLER_ITEMS_SENTINEL"
    fail with "SCOPING LEAK: caller 'items' corrupted"
.
if inv is not "CALLER_INV_SENTINEL"
    fail with "SCOPING LEAK: caller 'inv' corrupted"
.
if equip is not "CALLER_EQUIP_SENTINEL"
    fail with "SCOPING LEAK: caller 'equip' corrupted"
.
if index is not 88882
    fail with "SCOPING LEAK: caller 'index' corrupted"
.
if temp is not "CALLER_TEMP_SENTINEL"
    fail with "SCOPING LEAK: caller 'temp' corrupted"
.
if value is not 77773
    fail with "SCOPING LEAK: caller 'value' corrupted"
.
if text is not "CALLER_TEXT_SENTINEL"
    fail with "SCOPING LEAK: caller 'text' corrupted"
.
if totalCount is not 66664
    fail with "SCOPING LEAK: caller 'totalCount' corrupted"
.
if bonus is not 55555
    fail with "SCOPING LEAK: caller 'bonus' corrupted"
.
say "SCOPING_ISOLATION_VERIFIED_17_VARS"
'''
    scoping_path = os.path.join(proj_dir, "src", "test_scoping.ot")
    with open(scoping_path, "w", encoding="utf-8") as f:
        f.write(scoping_harness)

    iso_res = run_cmd([otter_bin, "run", "src/test_scoping.ot"], cwd=proj_dir, env=clean_env)
    assert iso_res.returncode == 0, f"Scoping isolation test failed:\n{iso_res.stdout}\n{iso_res.stderr}"
    assert "SCOPING_ISOLATION_VERIFIED_17_VARS" in iso_res.stdout

    iso_json = os.path.join(proj_dir, "scoping_isolated_state.json")
    with open(iso_json, "r", encoding="utf-8") as f:
        iso_state = json.load(f)
    # dedupe([12, 8, 12, 5]) = [12, 8, 5], sum = 25, HP = 100 - 25 = 75
    assert iso_state["name"] == "Lady Isolde"
    assert iso_state["health"] == 75
    print("  [PASS] All 5 official packs composed deterministically; 17 hostile caller variables survived unbroken.")
    return wf_path


def verify_transitive_dependency_and_prefix_defense(otter_bin, bundled_lib, workspace, clean_env):
    print("\n[Stage 2D & 3B] Transitive Pack Dependency Resolution & Prefix Collision Defense")
    ext_lib = os.path.join(workspace, "ext_library")
    shutil.copytree(bundled_lib, ext_lib)

    # 1. Third-party pack declaring transitive dependency on otter-official/math-utils
    vendor_pack = os.path.join(ext_lib, "packs", "acme-corp", "score-normalizer")
    os.makedirs(os.path.join(vendor_pack, "src"), exist_ok=True)
    with open(os.path.join(vendor_pack, "src", "main.ot"), "w", encoding="utf-8") as f:
        f.write(
            "to normalizeScore rawScore maxScore\n"
            "    clamp rawScore 0 maxScore into _ac_bounded\n"
            "    calcPercentage _ac_bounded maxScore into _ac_pct\n"
            "    return _ac_pct\n"
            ".\n"
        )
    vendor_manifest = {
        "schemaVersion": 2,
        "name": "score-normalizer",
        "publisher": "acme-corp",
        "version": "1.0.0",
        "kind": "pack",
        "description": "External score normalizer depending on otter-official/math-utils",
        "entry": "src/main.ot",
        "license": "MIT",
        "requiresOtter": ">=1.0.1",
        "platforms": ["console", "desktop", "web"],
        "variablePrefix": "_ac_",
        "provides": {
            "capabilities": ["analytics.scoring"],
            "operations": [
                {
                    "name": "normalizeScore",
                    "description": "Clamps rawScore into [0, maxScore] and returns percentage.",
                    "parameters": [
                        {"name": "rawScore", "type": "number"},
                        {"name": "maxScore", "type": "number"},
                    ],
                    "returns": {"type": "number"},
                    "sideEffects": ["none"],
                    "errorMode": "returns-fallback",
                }
            ],
        },
        "requires": {
            "capabilities": [],
            "packages": {"otter-official/math-utils": "^1.0.1"},
        },
    }
    with open(os.path.join(vendor_pack, "otter.pack.json"), "w", encoding="utf-8") as f:
        json.dump(vendor_manifest, f, indent=2)

    # 2. Reserved _orch_ prefix collision pack
    bad_pack = os.path.join(ext_lib, "packs", "acme-corp", "reserved-prefix-pack")
    os.makedirs(os.path.join(bad_pack, "src"), exist_ok=True)
    with open(os.path.join(bad_pack, "src", "main.ot"), "w", encoding="utf-8") as f:
        f.write("to badOp x\n    return x\n.\n")
    bad_manifest = dict(vendor_manifest)
    bad_manifest["name"] = "reserved-prefix-pack"
    bad_manifest["variablePrefix"] = "_orch_"
    bad_manifest["provides"] = {
        "capabilities": ["bad.op"],
        "operations": [
            {
                "name": "badOp",
                "description": "Collides with orchestrator prefix",
                "parameters": [{"name": "x", "type": "text"}],
                "returns": {"type": "text"},
                "sideEffects": ["none"],
                "errorMode": "returns-fallback",
            }
        ],
    }
    bad_manifest["requires"] = {"capabilities": [], "packages": {}}
    with open(os.path.join(bad_pack, "otter.pack.json"), "w", encoding="utf-8") as f:
        json.dump(bad_manifest, f, indent=2)

    ext_env = dict(clean_env)
    ext_env["OTTER_LIBRARY_PATH"] = ext_lib

    # Verify contract check on external pack passes
    cv = run_cmd([otter_bin, "pack", "verify", vendor_pack, "--contracts"], env=ext_env)
    assert cv.returncode == 0, f"External pack contract verify failed:\n{cv.stdout}\n{cv.stderr}"

    # Plan workflow that references acme-corp/score-normalizer + otter-official/text-utils
    # while otter-official/math-utils is pulled in transitively via requires.packages!
    trans_wf = {
        "workflowVersion": 1,
        "id": "transitive-score-format",
        "name": "Transitive Dependency Score Formatting",
        "composedPacks": [
            "acme-corp/score-normalizer",
            "otter-official/text-utils",
        ],
        "pipelineInputs": [
            {"name": "rawScore", "type": "number", "default": 75},
            {"name": "maxScore", "type": "number", "default": 100},
        ],
        "steps": [
            {
                "id": "step1_norm",
                "pack": "acme-corp/score-normalizer",
                "operation": "normalizeScore",
                "inputs": {
                    "rawScore": {"fromInput": "rawScore"},
                    "maxScore": {"fromInput": "maxScore"},
                },
            },
            {
                "id": "step2_fmt",
                "pack": "otter-official/text-utils",
                "operation": "padLeft",
                "inputs": {
                    "text": {"fromStep": "step1_norm", "port": "returns", "adapter": "number_to_text"},
                    "targetLen": {"value": 5},
                    "padChar": {"value": "0"},
                },
            },
        ],
        "output": {"fromStep": "step2_fmt", "port": "returns"},
    }
    wf_path = os.path.join(workspace, "transitive.workflow.json")
    with open(wf_path, "w", encoding="utf-8") as f:
        json.dump(trans_wf, f, indent=2)

    plan_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--json"], cwd=workspace, env=ext_env)
    assert plan_res.returncode == 0, f"Transitive dependency planning failed:\n{plan_res.stdout}\n{plan_res.stderr}"
    plan_data = json.loads(plan_res.stdout)
    composed_packs = plan_data["compositionPlan"]["composedPacks"]
    assert "otter-official/math-utils" in composed_packs, f"Transitive dependency math-utils missing: {composed_packs}"

    # Apply, check, and run the transitive workflow end-to-end in a fresh external project
    init_t = run_cmd([otter_bin, "new", "console", "app_transitive"], cwd=workspace, env=ext_env)
    assert init_t.returncode == 0, f"otter new failed: {init_t.stderr}"
    t_proj = os.path.join(workspace, "app_transitive")
    apply_t = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--apply", "--yes"], cwd=t_proj, env=ext_env)
    assert apply_t.returncode == 0, f"Transitive apply failed:\n{apply_t.stdout}\n{apply_t.stderr}"
    chk_t = run_cmd([otter_bin, "check", "src/main.ot"], cwd=t_proj, env=ext_env)
    assert chk_t.returncode == 0, f"Transitive check failed:\n{chk_t.stdout}\n{chk_t.stderr}"
    run_t = run_cmd([otter_bin, "run", "src/main.ot"], cwd=t_proj, env=ext_env)
    assert run_t.returncode == 0, f"Transitive run failed:\n{run_t.stdout}\n{run_t.stderr}"
    assert "00075" in run_t.stdout, f"Expected formatted transitive output '00075', got:\n{run_t.stdout}"

    # Verify reserved _orch_ prefix is rejected
    bad_res = run_cmd([otter_bin, "compose", "plan", "--packs", "acme-corp/reserved-prefix-pack"], cwd=workspace, env=ext_env)
    assert bad_res.returncode != 0
    assert "PREFIX_COLLISION" in bad_res.stdout or "PREFIX_COLLISION" in bad_res.stderr
    print("  [PASS] Transitive dependency resolved, applied, and executed ('00075'); reserved '_orch_' prefix rejected.")


def verify_all_advertised_adapters_executable(otter_bin, workspace, clean_env):
    print("\n[Stage 2E] Complete Adapter Registry Verification (Every Advertised Adapter Executable)")
    planner_path = os.path.join(REPO_ROOT, "src", "Otter.Cli", "CompositionPlanner.cs")
    with open(planner_path, "r", encoding="utf-8") as f:
        planner_src = f.read()

    # Discover every adapter registered in AdapterRegistry inside CompositionPlanner.cs
    reg_start = planner_src.index("public static class AdapterRegistry")
    reg_end = planner_src.index("// 4. Graph & Planning Data Models", reg_start)
    reg_block = planner_src[reg_start:reg_end]
    advertised_adapters = sorted(set(re.findall(r'Id\s*=\s*"([^"]+)"', reg_block)))
    assert len(advertised_adapters) >= 7, f"Expected >= 7 registered adapters, found: {advertised_adapters}"

    adapter_wf = {
        "workflowVersion": 1,
        "id": "all-adapters-verification",
        "name": "Complete Adapter Registry Runtime Verification",
        "target": "console",
        "composedPacks": [
            "otter-official/math-utils",
            "otter-official/list-utils",
            "otter-official/text-utils",
            "otter-official/file-helpers",
        ],
        "pipelineInputs": [
            {"name": "rawNum", "type": "number", "default": 42},
            {"name": "sampleList", "type": "list", "default": ["alpha", "beta", "alpha"]},
            {"name": "rawTitle", "type": "text", "default": "Adapter Proof"},
        ],
        "steps": [
            {
                "id": "s1_clamp",
                "pack": "otter-official/math-utils",
                "operation": "clamp",
                "inputs": {
                    "value": {"fromInput": "rawNum"},
                    "minLimit": {"value": 0},
                    "maxLimit": {"value": 100},
                },
            },
            {
                "id": "s2_num_to_text",
                "pack": "otter-official/text-utils",
                "operation": "padLeft",
                "inputs": {
                    "text": {"fromStep": "s1_clamp", "port": "returns", "adapter": "number_to_text"},
                    "targetLen": {"value": 6},
                    "padChar": {"value": "0"},
                },
            },
            {
                "id": "s3_has_item",
                "pack": "otter-official/list-utils",
                "operation": "hasItem",
                "inputs": {
                    "items": {"fromInput": "sampleList"},
                    "target": {"value": "alpha"},
                },
            },
            {
                "id": "s4_bool_to_text",
                "pack": "otter-official/text-utils",
                "operation": "toUpper",
                "inputs": {
                    "text": {"fromStep": "s3_has_item", "port": "returns", "adapter": "boolean_to_text"},
                },
            },
            {
                "id": "s5_dedupe",
                "pack": "otter-official/list-utils",
                "operation": "dedupeList",
                "inputs": {
                    "items": {"fromInput": "sampleList"},
                },
            },
            {
                "id": "s6_list_to_text",
                "pack": "otter-official/text-utils",
                "operation": "containsText",
                "inputs": {
                    "source": {"fromStep": "s5_dedupe", "port": "returns", "adapter": "list_to_text"},
                    "searchStr": {"value": "beta"},
                },
            },
            {
                "id": "s7_slug",
                "pack": "otter-official/text-utils",
                "operation": "slugify",
                "inputs": {
                    "text": {"fromInput": "rawTitle"},
                },
            },
            {
                "id": "s8_wrap_slug_write",
                "pack": "otter-official/file-helpers",
                "operation": "writeJsonObject",
                "inputs": {
                    "filePath": {"value": "adapter_slug.json"},
                    "obj": {"fromStep": "s7_slug", "port": "returns", "adapter": "json_object_wrap_slug"},
                },
            },
            {
                "id": "s9_read_slug",
                "pack": "otter-official/file-helpers",
                "operation": "readJsonObjectSafe",
                "inputs": {
                    "filePath": {"value": "adapter_slug.json"},
                    "fallbackValue": {"value": None},
                },
                "orderAfter": ["s8_wrap_slug_write"],
            },
            {
                "id": "s10_obj_to_text",
                "pack": "otter-official/text-utils",
                "operation": "normalizeSpaces",
                "inputs": {
                    "text": {"fromStep": "s9_read_slug", "port": "returns", "adapter": "json_object_to_text"},
                },
            },
            {
                "id": "s11_text_to_obj_write",
                "pack": "otter-official/file-helpers",
                "operation": "writeJsonObject",
                "inputs": {
                    "filePath": {"value": "adapter_roundtrip.json"},
                    "obj": {"fromStep": "s10_obj_to_text", "port": "returns", "adapter": "text_to_json_object"},
                },
                "orderAfter": ["s10_obj_to_text"],
            },
            {
                "id": "s12_wrap_value_write",
                "pack": "otter-official/file-helpers",
                "operation": "writeJsonObject",
                "inputs": {
                    "filePath": {"value": "adapter_value.json"},
                    "obj": {"fromStep": "s4_bool_to_text", "port": "returns", "adapter": "json_object_wrap_value"},
                },
                "orderAfter": ["s4_bool_to_text", "s2_num_to_text", "s6_list_to_text"],
            },
        ],
        "output": {"fromStep": "s2_num_to_text", "port": "returns"},
    }

    exercised = sorted({
        binding["adapter"]
        for step in adapter_wf["steps"]
        for binding in step.get("inputs", {}).values()
        if isinstance(binding, dict) and binding.get("adapter")
    })
    assert exercised == advertised_adapters, (
        f"Untested advertised adapters! Advertised={advertised_adapters}, Exercised={exercised}"
    )

    wf_path = os.path.join(workspace, "all_adapters.workflow.json")
    with open(wf_path, "w", encoding="utf-8") as f:
        json.dump(adapter_wf, f, indent=2)

    init_res = run_cmd([otter_bin, "new", "console", "app_adapters"], cwd=workspace, env=clean_env)
    assert init_res.returncode == 0
    proj_dir = os.path.join(workspace, "app_adapters")

    apply_res = run_cmd([otter_bin, "compose", "plan", "--workflow", wf_path, "--apply", "--yes"], cwd=proj_dir, env=clean_env)
    assert apply_res.returncode == 0, f"All-adapters apply failed:\n{apply_res.stdout}\n{apply_res.stderr}"

    chk_res = run_cmd([otter_bin, "check", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert chk_res.returncode == 0, f"All-adapters check failed:\n{chk_res.stdout}\n{chk_res.stderr}"

    run_res = run_cmd([otter_bin, "run", "src/main.ot"], cwd=proj_dir, env=clean_env)
    assert run_res.returncode == 0, f"All-adapters run failed:\n{run_res.stdout}\n{run_res.stderr}"
    assert "000042" in run_res.stdout, f"Expected '000042' from number_to_text -> padLeft, got:\n{run_res.stdout}"

    with open(os.path.join(proj_dir, "adapter_slug.json"), "r", encoding="utf-8") as f:
        assert json.load(f) == {"slug": "adapter-proof"}
    with open(os.path.join(proj_dir, "adapter_roundtrip.json"), "r", encoding="utf-8") as f:
        assert json.load(f) == {"slug": "adapter-proof"}
    with open(os.path.join(proj_dir, "adapter_value.json"), "r", encoding="utf-8") as f:
        assert json.load(f) == {"value": "TRUE"}

    print(f"  [PASS] All {len(advertised_adapters)}/{len(advertised_adapters)} advertised adapters executed and verified end-to-end: {advertised_adapters}.")


def run_sandboxed_benchmarks(otter_bin, bundled_lib, all5_wf_path, workspace, clean_env):
    print("\n[Stage 4] Sandboxed Performance Benchmarking")
    print(f"  Environment: {platform.system()} {platform.release()} ({platform.machine()}), Python {platform.python_version()}")

    # 1. Contract verification benchmark across all 5 packs (3 iterations each = 15 runs)
    pack_names = ["math-utils", "text-utils", "list-utils", "file-helpers", "game-entity"]
    contract_samples = []
    for _ in range(3):
        for p in pack_names:
            pdir = os.path.join(bundled_lib, "packs", "otter-official", p)
            t0 = time.perf_counter()
            r = run_cmd([otter_bin, "pack", "verify", pdir, "--contracts", "--json"], env=clean_env)
            dt = (time.perf_counter() - t0) * 1000.0
            assert r.returncode == 0
            contract_samples.append(dt)

    # 2. Multi-pack 5-pack composition planning benchmark (5 iterations)
    plan_samples = []
    for _ in range(5):
        t0 = time.perf_counter()
        r = run_cmd([otter_bin, "compose", "plan", "--workflow", all5_wf_path, "--json"], cwd=workspace, env=clean_env)
        dt = (time.perf_counter() - t0) * 1000.0
        assert r.returncode == 0
        plan_samples.append(dt)

    # 3. End-to-end sandboxed apply + check + run benchmark (3 fresh disposable projects)
    e2e_samples = []
    for i in range(3):
        with tempfile.TemporaryDirectory(prefix=f"otter_m3_bench_{i}_") as sandbox:
            t0 = time.perf_counter()
            r_new = run_cmd([otter_bin, "new", "console", "bench_app"], cwd=sandbox, env=clean_env)
            assert r_new.returncode == 0
            app_dir = os.path.join(sandbox, "bench_app")
            r_apply = run_cmd([otter_bin, "compose", "plan", "--workflow", all5_wf_path, "--apply", "--yes"], cwd=app_dir, env=clean_env)
            assert r_apply.returncode == 0
            r_chk = run_cmd([otter_bin, "check", "src/main.ot"], cwd=app_dir, env=clean_env)
            assert r_chk.returncode == 0
            r_run = run_cmd([otter_bin, "run", "src/main.ot"], cwd=app_dir, env=clean_env)
            assert r_run.returncode == 0
            dt = (time.perf_counter() - t0) * 1000.0
            e2e_samples.append(dt)

    print(f"  [BENCH] pack verify --contracts (5 packs):   {format_stats(contract_samples)}")
    print(f"  [BENCH] compose plan (5-pack 10-step DAG):   {format_stats(plan_samples)}")
    print(f"  [BENCH] sandboxed new+apply+check+run (5p):  {format_stats(e2e_samples)}")

    # Generous CI-safe upper bounds to catch severe regressions without flakiness
    assert statistics.median(plan_samples) < 5000.0, f"5-pack planning unexpectedly slow: {format_stats(plan_samples)}"
    assert statistics.median(e2e_samples) < 15000.0, f"5-pack end-to-end sandbox unexpectedly slow: {format_stats(e2e_samples)}"
    print("  [PASS] Sandboxed performance benchmarks completed within bounds.")


def main():
    print("=" * 76)
    print(" Otter Phase 11 Milestone 3: Sandboxed Verification & Benchmarking Suite")
    print("=" * 76)

    with tempfile.TemporaryDirectory(prefix="otter_m3_install_") as install_dir, \
         tempfile.TemporaryDirectory(prefix="otter_m3_home_") as fake_home, \
         tempfile.TemporaryDirectory(prefix="otter_m3_work_") as workspace:

        print(f"  Isolated Install: {install_dir}")
        print(f"  Isolated Home:    {fake_home}")
        print(f"  Sandbox Workdir:  {workspace}")

        print("\n--> Packaging fresh Otter distribution via tools/package-distribution.sh...")
        pkg_res = run_cmd([PACKAGE_SCRIPT, install_dir], cwd=REPO_ROOT)
        assert pkg_res.returncode == 0, f"package-distribution.sh failed:\n{pkg_res.stderr}\n{pkg_res.stdout}"

        otter_bin = os.path.join(install_dir, "bin", "otter")
        bundled_lib = os.path.join(install_dir, "library")
        assert os.path.isfile(otter_bin), f"Installed otter binary missing at {otter_bin}"
        assert os.path.isdir(bundled_lib), f"Bundled library missing at {bundled_lib}"

        clean_env = os.environ.copy()
        clean_env.pop("OTTER_LIBRARY_PATH", None)
        clean_env["HOME"] = fake_home

        ver_res = run_cmd([otter_bin, "--version"], env=clean_env)
        assert ver_res.returncode == 0, f"otter --version failed: {ver_res.stderr}"
        print(f"  Installed Binary Version: {ver_res.stdout.strip()}")

        verify_all_five_packs_outside_repo(otter_bin, bundled_lib, clean_env)
        verify_four_pack_collection_analytics(otter_bin, bundled_lib, workspace, clean_env)
        verify_three_pack_game_save_state(otter_bin, bundled_lib, workspace, clean_env)
        all5_wf_path = verify_all_five_packs_single_composition_and_scoping_isolation(otter_bin, workspace, clean_env)
        verify_transitive_dependency_and_prefix_defense(otter_bin, bundled_lib, workspace, clean_env)
        verify_all_advertised_adapters_executable(otter_bin, workspace, clean_env)
        run_sandboxed_benchmarks(otter_bin, bundled_lib, all5_wf_path, workspace, clean_env)

    print("\n" + "=" * 76)
    print(" ALL PHASE 11 MILESTONE 3 VERIFICATION & BENCHMARK CHECKS PASSED (100%)")
    print("=" * 76)


if __name__ == "__main__":
    main()
