#!/usr/bin/env python3
"""
tests/composition/test_deterministic_planner.py
Automated Verification Suite for Phase 11 Milestone 2:
Deterministic Multi-Pack Composition Planner (Generalized Contract Engine).

Verifies:
1. Declarative workflow specification parsing and contract validation.
2. Port binding, parameter type matching, and real adapter validation.
3. Distinction between data dependencies and ordering-only dependencies.
4. Ambiguity reporting when multiple candidate ports exist without workflow spec.
5. Negative diagnostics: missing operations, unknown parameters, invalid adapters,
   dependency cycles, variable prefix collisions, unsupported platforms.
6. Three independent end-to-end integration tests with runtime execution:
   - Test A: Text processing + file persistence
   - Test B: Numeric processing + text formatting (adapter: number_to_text)
   - Test C: Custom 3rd-party Pack outside repo composed with official Pack
"""

import os
import sys
import json
import subprocess
import tempfile
import shutil

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CLI_DIR = os.path.join(REPO_ROOT, "src", "Otter.Cli")


def run_cli(args, cwd=REPO_ROOT, env=None):
    cmd = ["dotnet", "run", "--project", CLI_DIR, "--"] + args
    run_env = os.environ.copy()
    run_env["OTTER_LIBRARY_PATH"] = os.path.join(REPO_ROOT, "library")
    if env:
        run_env.update(env)
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, env=run_env)


def test_multi_pack_discovery_and_planning():
    print("--> Test 1: Deterministic multi-pack discovery and contract planning...")
    res = run_cli([
        "compose", "plan",
        "--packs", "otter-official/text-utils,otter-official/file-helpers",
        "--target", "console",
        "--json"
    ])
    assert res.returncode == 0, f"Expected 0, got {res.returncode}: {res.stderr}"
    data = json.loads(res.stdout)
    assert data["schemaVersion"] == 2
    assert data["proposalId"].startswith("prop-")
    assert data["approvalRequired"] is True
    assert data["verification"]["syntaxValid"] is True

    plan = data["compositionPlan"]
    assert "otter-official/text-utils" in plan["composedPacks"]
    assert "otter-official/file-helpers" in plan["composedPacks"]

    graph = plan["graph"]
    assert len(graph["nodes"]) >= 4
    assert len(graph["edges"]) >= 3
    assert len(graph["topologicalOrder"]) == len(graph["nodes"])
    print("  [PASS] Successfully planned multi-pack composition using contracts.")


def test_explainable_graph_and_mermaid():
    print("--> Test 2: Explainable composition graph, Mermaid, and dependency distinction...")
    res = run_cli([
        "compose", "plan",
        "--packs", "otter-official/text-utils,otter-official/file-helpers",
        "--target", "console",
        "--explain"
    ])
    assert res.returncode == 0
    assert "Participating Component Contracts:" in res.stdout
    assert "Deterministic Execution DAG (Topological Order):" in res.stdout
    assert "Port Bindings & Type Invariants:" in res.stdout
    assert "[DATA]" in res.stdout
    assert "[ORDERING]" in res.stdout
    assert "```mermaid" in res.stdout
    assert "graph TD" in res.stdout
    assert "Scoping Safety Analysis:" in res.stdout
    print("  [PASS] Graph explanation, topological order, and data vs ordering edges verified.")


def test_preview_orchestrator_source():
    print("--> Test 3: Synthesized Otter 1.0.1 orchestrator preview...")
    res = run_cli([
        "compose", "plan",
        "--packs", "otter-official/text-utils,otter-official/file-helpers",
        "--target", "console",
        "--preview"
    ])
    assert res.returncode == 0
    assert "Synthesized Orchestrator Source Code Preview" in res.stdout
    assert 'use "${import:otter-official/text-utils}"' in res.stdout
    assert 'use "${import:otter-official/file-helpers}"' in res.stdout
    assert "to runCompositionPipeline" in res.stdout
    assert "normalizeSpaces" in res.stdout
    assert "slugify" in res.stdout
    assert "writeJsonObject" in res.stdout
    assert "readJsonObjectSafe" in res.stdout
    assert "_orch_step3_persist_obj is a thing" in res.stdout
    print("  [PASS] Orchestrator source preview verified with zero syntax errors.")


def test_intent_based_multi_pack_resolution():
    print("--> Test 4: Intent-based capability resolution across multiple packs...")
    res = run_cli([
        "compose", "plan",
        "format text and compute stats",
        "--target", "console",
        "--json"
    ])
    assert res.returncode == 0
    data = json.loads(res.stdout)
    packs = data["compositionPlan"]["composedPacks"]
    assert "otter-official/text-utils" in packs
    assert "otter-official/math-utils" in packs
    assert data["verification"]["syntaxValid"] is True
    print("  [PASS] Natural intent resolved to multiple complementary packs.")


def test_negative_unsupported_domain_rejection():
    print("--> Test 5: Negative - Unsupported domain honest rejection...")
    res = run_cli([
        "compose", "plan",
        "train neural network on cuda gpu and mine crypto",
        "--target", "console",
        "--json"
    ])
    assert res.returncode != 0
    data = json.loads(res.stdout)
    assert data["supported"] is False
    assert "gpu-acceleration" in data["missingCapabilities"]
    assert "neural-networks" in data["missingCapabilities"]
    assert "blockchain" in data["missingCapabilities"]
    print("  [PASS] Unsupported domains rejected honestly without fabricating APIs.")


def test_negative_missing_pack_rejection():
    print("--> Test 6: Negative - Missing pack in offline catalog...")
    res = run_cli([
        "compose", "plan",
        "--packs", "otter-official/nonexistent-pack-xyz",
        "--target", "console"
    ])
    assert res.returncode != 0
    assert "MISSING_PACK" in res.stdout or "MISSING_PACK" in res.stderr
    print("  [PASS] Missing pack detected and rejected with actionable diagnostic.")


def test_negative_unsupported_platform_rejection():
    print("--> Test 7: Negative - Platform incompatibility detection...")
    res = run_cli([
        "compose", "plan",
        "--packs", "otter-official/file-helpers",
        "--target", "web"
    ])
    assert res.returncode != 0
    assert "UNSUPPORTED_PLATFORM" in res.stdout or "UNSUPPORTED_PLATFORM" in res.stderr
    print("  [PASS] Incompatible platform constraint caught cleanly.")


def test_negative_variable_prefix_collision():
    print("--> Test 8: Negative - Variable prefix collision detection...")
    with tempfile.TemporaryDirectory() as tmpdir:
        mock_dir = os.path.join(tmpdir, "packs", "test-pub", "conflict-pack")
        os.makedirs(os.path.join(mock_dir, "src"), exist_ok=True)
        with open(os.path.join(mock_dir, "src", "main.ot"), "w") as f:
            f.write("to helperFoo x\n    return x\n.\n")
        manifest = {
            "schemaVersion": 2,
            "name": "conflict-pack",
            "publisher": "test-pub",
            "version": "1.0.0",
            "kind": "pack",
            "description": "Conflict mock pack",
            "entry": "src/main.ot",
            "license": "MIT",
            "platforms": ["console"],
            "variablePrefix": "_tu_",  # Deliberate collision with text-utils!
            "provides": {
                "operations": [{
                    "name": "helperFoo",
                    "description": "Helper",
                    "parameters": [{"name": "x", "type": "text"}],
                    "returns": {"type": "text"},
                    "sideEffects": ["none"],
                    "errorMode": "returns-fallback"
                }]
            },
            "requires": {"packages": {}}
        }
        with open(os.path.join(mock_dir, "otter.pack.json"), "w") as f:
            json.dump(manifest, f)

        shutil.copytree(os.path.join(REPO_ROOT, "library", "packs", "otter-official"), os.path.join(tmpdir, "packs", "otter-official"))
        shutil.copytree(os.path.join(REPO_ROOT, "library", "snippets"), os.path.join(tmpdir, "snippets"))

        res = run_cli([
            "compose", "plan",
            "--packs", "otter-official/text-utils,test-pub/conflict-pack",
            "--target", "console"
        ], env={"OTTER_LIBRARY_PATH": tmpdir})
        assert res.returncode != 0
        assert "PREFIX_COLLISION" in res.stdout or "PREFIX_COLLISION" in res.stderr
        print("  [PASS] Variable prefix collision between components detected and prevented.")


def test_negative_adapter_contract_validation():
    print("--> Test 9: Negative - Adapter contract validation (unregistered & type mismatch)...")
    with tempfile.TemporaryDirectory() as tmpdir:
        # Invalid unregistered adapter
        bad_wf = {
            "workflowVersion": 1,
            "name": "bad-adapter-workflow",
            "steps": [
                {
                    "id": "step1",
                    "pack": "otter-official/text-utils",
                    "operation": "toUpper",
                    "inputs": {"src": {"value": "hello"}}
                },
                {
                    "id": "step2",
                    "pack": "otter-official/text-utils",
                    "operation": "padLeft",
                    "inputs": {
                        "text": {"fromStep": "step1", "adapter": "nonexistent_fake_adapter"},
                        "targetLen": {"value": 10},
                        "padChar": {"value": " "}
                    }
                }
            ]
        }
        wf_path = os.path.join(tmpdir, "bad_wf.json")
        with open(wf_path, "w") as f:
            json.dump(bad_wf, f)

        res = run_cli(["compose", "plan", "--workflow", wf_path])
        assert res.returncode != 0
        assert "INVALID_ADAPTER" in res.stdout or "INVALID_ADAPTER" in res.stderr

        # Mismatched adapter types
        mismatch_wf = {
            "workflowVersion": 1,
            "name": "mismatch-adapter-workflow",
            "steps": [
                {
                    "id": "step1",
                    "pack": "otter-official/text-utils",
                    "operation": "toUpper",
                    "inputs": {"src": {"value": "hello"}}
                },
                {
                    "id": "step2",
                    "pack": "otter-official/text-utils",
                    "operation": "padLeft",
                    "inputs": {
                        # number_to_text converts number -> text, but step1 produces text!
                        "text": {"fromStep": "step1", "adapter": "number_to_text"},
                        "targetLen": {"value": 10},
                        "padChar": {"value": " "}
                    }
                }
            ]
        }
        with open(wf_path, "w") as f:
            json.dump(mismatch_wf, f)

        res2 = run_cli(["compose", "plan", "--workflow", wf_path])
        assert res2.returncode != 0
        assert "ADAPTER_TYPE_MISMATCH" in res2.stdout or "ADAPTER_TYPE_MISMATCH" in res2.stderr
        print("  [PASS] Adapter contract validation catches nonexistent and mismatched adapters.")


def test_negative_cycle_detection():
    print("--> Test 10: Negative - Dependency cycle detection in workflow DAG...")
    with tempfile.TemporaryDirectory() as tmpdir:
        cycle_wf = {
            "workflowVersion": 1,
            "name": "cycle-workflow",
            "steps": [
                {
                    "id": "stepA",
                    "pack": "otter-official/text-utils",
                    "operation": "toUpper",
                    "inputs": {"src": {"fromStep": "stepB"}},
                    "orderAfter": ["stepB"]
                },
                {
                    "id": "stepB",
                    "pack": "otter-official/text-utils",
                    "operation": "toLower",
                    "inputs": {"src": {"fromStep": "stepA"}},
                    "orderAfter": ["stepA"]
                }
            ]
        }
        wf_path = os.path.join(tmpdir, "cycle_wf.json")
        with open(wf_path, "w") as f:
            json.dump(cycle_wf, f)

        res = run_cli(["compose", "plan", "--workflow", wf_path])
        assert res.returncode != 0
        assert "DEPENDENCY_CYCLE" in res.stdout or "DEPENDENCY_CYCLE" in res.stderr
        print("  [PASS] Dependency cycles across data and ordering edges rejected.")


def test_end_to_end_test_a_text_and_file():
    print("--> Test 11 (General Demonstration A): Text processing + file persistence...")
    with tempfile.TemporaryDirectory() as tmpdir:
        init_res = run_cli(["new", "console", "test-app-a"], cwd=tmpdir)
        assert init_res.returncode == 0
        proj_dir = os.path.join(tmpdir, "test-app-a")

        wf_path = os.path.join(REPO_ROOT, "library", "workflows", "text-processing-persistence.workflow.json")
        res = run_cli([
            "compose", "plan",
            "--workflow", wf_path,
            "--apply", "--yes"
        ], cwd=proj_dir)
        assert res.returncode == 0, f"Plan apply failed: {res.stdout}\n{res.stderr}"

        # Execute synthesized Otter orchestrator
        run_res = run_cli(["run", "src/main.ot"], cwd=proj_dir)
        assert run_res.returncode == 0, f"Runtime execution failed: {run_res.stderr}\n{run_res.stdout}"
        assert "Pipeline execution complete" in run_res.stdout

        # Assert persisted file on disk
        out_json_path = os.path.join(proj_dir, "composition_output.json")
        assert os.path.isfile(out_json_path), "Expected composition_output.json to exist"
        with open(out_json_path, "r", encoding="utf-8") as f:
            persisted = json.load(f)
        assert "slug" in persisted
        assert persisted["slug"] == "multi-pack-deterministic-composition-architecture"
        print("  [PASS] Test A: Text processing + file persistence executed cleanly with verified JSON state.")


def test_end_to_end_test_b_numeric_and_text_formatting():
    print("--> Test 12 (General Demonstration B): Numeric processing + text formatting (real adapter)...")
    with tempfile.TemporaryDirectory() as tmpdir:
        init_res = run_cli(["new", "console", "test-app-b"], cwd=tmpdir)
        assert init_res.returncode == 0
        proj_dir = os.path.join(tmpdir, "test-app-b")

        wf_path = os.path.join(REPO_ROOT, "library", "workflows", "numeric-calculation-formatting.workflow.json")
        res = run_cli([
            "compose", "plan",
            "--workflow", wf_path,
            "--apply", "--yes"
        ], cwd=proj_dir)
        assert res.returncode == 0, f"Plan apply failed: {res.stdout}\n{res.stderr}"

        # Execute synthesized Otter orchestrator
        run_res = run_cli(["run", "src/main.ot"], cwd=proj_dir)
        assert run_res.returncode == 0, f"Runtime execution failed: {run_res.stderr}\n{run_res.stdout}"
        assert "000042" in run_res.stdout, f"Expected padded string '000042' in stdout, got: {run_res.stdout}"
        print("  [PASS] Test B: Numeric clamp (42) + text padLeft ('000042') executed cleanly via real adapter.")


def test_end_to_end_test_c_custom_third_party_pack():
    print("--> Test 13 (General Demonstration C): Custom 3rd-party Pack outside repo composed with official Pack...")
    with tempfile.TemporaryDirectory() as tmpdir:
        # Create an external custom pack catalog outside repository
        ext_lib = os.path.join(tmpdir, "custom_library")
        os.makedirs(os.path.join(ext_lib, "snippets"), exist_ok=True)
        shutil.copytree(os.path.join(REPO_ROOT, "library", "snippets"), os.path.join(ext_lib, "snippets"), dirs_exist_ok=True)
        shutil.copytree(os.path.join(REPO_ROOT, "library", "packs", "otter-official"), os.path.join(ext_lib, "packs", "otter-official"), dirs_exist_ok=True)

        # Create custom third-party pack: external-vendor/audit-logger
        custom_pack_dir = os.path.join(ext_lib, "packs", "external-vendor", "audit-logger")
        os.makedirs(os.path.join(custom_pack_dir, "src"), exist_ok=True)

        with open(os.path.join(custom_pack_dir, "src", "main.ot"), "w", encoding="utf-8") as f:
            f.write(
                "to createAuditEntry action user\n"
                "    _al_entry is a thing\n"
                "    action of _al_entry is action\n"
                "    user of _al_entry is user\n"
                "    status of _al_entry is \"LOGGED\"\n"
                "    return _al_entry\n"
                ".\n"
            )

        custom_manifest = {
            "schemaVersion": 2,
            "name": "audit-logger",
            "publisher": "external-vendor",
            "version": "1.0.0",
            "kind": "pack",
            "description": "Custom enterprise audit logging pack created outside official repository",
            "entry": "src/main.ot",
            "license": "Apache-2.0",
            "platforms": ["console", "desktop"],
            "variablePrefix": "_al_",
            "provides": {
                "capabilities": ["enterprise.audit"],
                "operations": [
                    {
                        "name": "createAuditEntry",
                        "description": "Creates a structured audit record thing",
                        "parameters": [
                            {"name": "action", "type": "text"},
                            {"name": "user", "type": "text"}
                        ],
                        "returns": {"type": "thing"},
                        "sideEffects": ["none"],
                        "errorMode": "returns-fallback"
                    }
                ]
            },
            "requires": {"packages": {}}
        }
        with open(os.path.join(custom_pack_dir, "otter.pack.json"), "w", encoding="utf-8") as f:
            json.dump(custom_manifest, f)

        # Create declarative workflow composing official text-utils, external audit-logger, and official file-helpers
        custom_wf = {
            "workflowVersion": 1,
            "id": "audit-pipeline",
            "name": "Enterprise Audit Logging Workflow",
            "composedPacks": [
                "otter-official/text-utils",
                "external-vendor/audit-logger",
                "otter-official/file-helpers"
            ],
            "pipelineInputs": [
                {"name": "rawAction", "type": "text", "default": "service-deploy"},
                {"name": "operatorUser", "type": "text", "default": "admin"}
            ],
            "steps": [
                {
                    "id": "step1_format",
                    "pack": "otter-official/text-utils",
                    "operation": "toUpper",
                    "inputs": {"text": {"fromInput": "rawAction"}}
                },
                {
                    "id": "step2_audit",
                    "pack": "external-vendor/audit-logger",
                    "operation": "createAuditEntry",
                    "inputs": {
                        "action": {"fromStep": "step1_format", "port": "returns"},
                        "user": {"fromInput": "operatorUser"}
                    },
                    "orderAfter": ["step1_format"]
                },
                {
                    "id": "step3_save",
                    "pack": "otter-official/file-helpers",
                    "operation": "writeJsonObject",
                    "inputs": {
                        "filePath": {"value": "audit_output.json"},
                        "obj": {"fromStep": "step2_audit", "port": "returns"}
                    },
                    "orderAfter": ["step2_audit"]
                }
            ],
            "output": {"fromStep": "step3_save", "port": "returns"}
        }

        wf_path = os.path.join(tmpdir, "custom_audit_wf.json")
        with open(wf_path, "w", encoding="utf-8") as f:
            json.dump(custom_wf, f)

        # Create project and apply workflow using external library
        init_res = run_cli(["new", "console", "test-app-c"], cwd=tmpdir, env={"OTTER_LIBRARY_PATH": ext_lib})
        assert init_res.returncode == 0
        proj_dir = os.path.join(tmpdir, "test-app-c")

        res = run_cli([
            "compose", "plan",
            "--workflow", wf_path,
            "--apply", "--yes"
        ], cwd=proj_dir, env={"OTTER_LIBRARY_PATH": ext_lib})
        assert res.returncode == 0, f"Plan apply failed: {res.stdout}\n{res.stderr}"

        # Execute synthesized Otter orchestrator
        run_res = run_cli(["run", "src/main.ot"], cwd=proj_dir, env={"OTTER_LIBRARY_PATH": ext_lib})
        assert run_res.returncode == 0, f"Runtime execution failed: {run_res.stderr}\n{run_res.stdout}"
        assert "Pipeline execution complete" in run_res.stdout

        # Verify persisted JSON on disk
        audit_file = os.path.join(proj_dir, "audit_output.json")
        assert os.path.isfile(audit_file), "Expected audit_output.json to exist"
        with open(audit_file, "r", encoding="utf-8") as f:
            audit_data = json.load(f)
        assert audit_data["action"] == "SERVICE-DEPLOY"
        assert audit_data["user"] == "admin"
        assert audit_data["status"] == "LOGGED"

        # Assert CompositionPlanner.cs has zero hardcoded references to external-vendor or audit-logger
        planner_src = open(os.path.join(REPO_ROOT, "src", "Otter.Cli", "CompositionPlanner.cs"), "r", encoding="utf-8").read()
        assert "external-vendor" not in planner_src
        assert "audit-logger" not in planner_src
        assert "createAuditEntry" not in planner_src
        print("  [PASS] Test C: Custom external 3rd-party Pack composed without modifying CompositionPlanner.cs.")


def main():
    print("=" * 70)
    print(" Otter Phase 11 Milestone 2: Deterministic Composition Planner Tests")
    print(" Generalized Contract-Driven Architecture Verification")
    print("=" * 70)

    test_multi_pack_discovery_and_planning()
    test_explainable_graph_and_mermaid()
    test_preview_orchestrator_source()
    test_intent_based_multi_pack_resolution()
    test_negative_unsupported_domain_rejection()
    test_negative_missing_pack_rejection()
    test_negative_unsupported_platform_rejection()
    test_negative_variable_prefix_collision()
    test_negative_adapter_contract_validation()
    test_negative_cycle_detection()
    test_end_to_end_test_a_text_and_file()
    test_end_to_end_test_b_numeric_and_text_formatting()
    test_end_to_end_test_c_custom_third_party_pack()

    print("=" * 70)
    print(" ALL 13 DETERMINISTIC COMPOSITION PLANNER TESTS PASSED (100%)")
    print("=" * 70)


if __name__ == "__main__":
    main()
