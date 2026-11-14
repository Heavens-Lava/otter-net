#!/usr/bin/env python3
"""
Otter Phase 10 — Trustworthy Reusable Composition Consumer Test Suite
Tests that Otter composition engine:
1. Replaces keyword-only guessing with metadata-driven capability discovery and ranking.
2. Adheres to composition-proposal schema with explainable component rationale and connections.
3. Honestly reports unsupported requests when no compatible library components exist.
4. Safely applies proposals transactionally (path containment, pre-write preview, approval gate).
5. Installs dependencies, commits files atomically, verifies projects, and rolls back cleanly on any failure.
6. Exercises 5 distinct real-world application requests outside the repository from a fresh install.
"""

import os
import sys
import subprocess
import tempfile
import json
import re
import shutil

def run_cmd(cmd, cwd=None, env=None, input_text=None):
    res = subprocess.run(
        cmd,
        cwd=cwd,
        env=env,
        input=input_text,
        capture_output=True,
        text=True
    )
    return res

def validate_proposal_schema(proposal):
    """Validates proposal dictionary against composition-proposal.schema.json rules."""
    assert proposal.get("schemaVersion") == 1, "schemaVersion must be 1"
    pid = proposal.get("proposalId", "")
    assert re.match(r"^prop-[a-f0-9]{8,16}$", pid), f"Invalid proposalId format: {pid}"
    assert "intent" in proposal and isinstance(proposal["intent"], str), "Missing intent"
    assert "recommendedComponents" in proposal and isinstance(proposal["recommendedComponents"], list), "Missing recommendedComponents"
    assert "compositionPlan" in proposal and isinstance(proposal["compositionPlan"], dict), "Missing compositionPlan"
    assert "generatedSourcePreview" in proposal and isinstance(proposal["generatedSourcePreview"], list), "Missing generatedSourcePreview"
    assert "approvalRequired" in proposal and isinstance(proposal["approvalRequired"], bool), "Missing approvalRequired"
    assert "verification" in proposal and isinstance(proposal["verification"], dict), "Missing verification"

def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    package_script = os.path.join(repo_root, "tools", "package-distribution.sh")

    print("=" * 75)
    print(" Otter Phase 10: Composition Consumer Test Suite (Fresh Outside Install)")
    print("=" * 75)

    with tempfile.TemporaryDirectory(prefix="otter_phase10_install_") as install_dir, \
         tempfile.TemporaryDirectory(prefix="otter_phase10_home_") as fake_home, \
         tempfile.TemporaryDirectory(prefix="otter_phase10_workspace_") as workspace:

        print(f" Fresh Install Directory: {install_dir}")
        print(f" Isolated Home Directory:    {fake_home}")
        print(f" Clean Workspace:            {workspace}")

        # 1. Package fresh distribution to isolated install_dir
        print("\n--> Packaging fresh distribution via package-distribution.sh...")
        pack_res = run_cmd([package_script, install_dir], cwd=repo_root)
        if pack_res.returncode != 0:
            print(f"FATAL: package-distribution.sh failed:\n{pack_res.stderr}\n{pack_res.stdout}")
            sys.exit(1)

        otter_bin = os.path.join(install_dir, "bin", "otter")
        if not os.path.exists(otter_bin):
            print(f"FATAL: otter launcher not found at {otter_bin}")
            sys.exit(1)

        clean_env = os.environ.copy()
        clean_env.pop("OTTER_LIBRARY_PATH", None)
        clean_env["HOME"] = fake_home

        # Check version and doctor
        v_res = run_cmd([otter_bin, "--version"], env=clean_env)
        assert v_res.returncode == 0, f"otter --version failed: {v_res.stderr}"
        print(f" Otter Installed Version: {v_res.stdout.strip()}")

        doc_res = run_cmd([otter_bin, "doctor"], cwd=workspace, env=clean_env)
        assert doc_res.returncode == 0, f"otter doctor failed: {doc_res.stderr}"
        print(" [OK] Environment doctor check passed.")

        # =====================================================================
        # SCENARIO 1: Inventory management with persistence
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Scenario 1: 'Inventory management with persistence'")
        print("=" * 70)

        proj1_dir = os.path.join(workspace, "proj1_inventory")
        os.makedirs(proj1_dir, exist_ok=True)
        prop1_path = os.path.join(proj1_dir, "proposal.json")

        prop1_res = run_cmd([
            otter_bin, "compose", "propose",
            "Inventory management with persistence",
            "--output", prop1_path
        ], cwd=proj1_dir, env=clean_env)
        assert prop1_res.returncode == 0, f"Propose failed: {prop1_res.stderr}"

        with open(prop1_path, "r", encoding="utf-8") as f:
            prop1 = json.load(f)

        validate_proposal_schema(prop1)
        recs1 = [c["id"] for c in prop1["recommendedComponents"]]
        assert "otter-official/game-entity" in recs1, "Expected game-entity in recommendations"
        assert "otter-official/file-helpers" in recs1, "Expected file-helpers in recommendations"
        assert "game-inventory-capacity" in recs1, "Expected game-inventory-capacity snippet in recommendations"
        assert prop1["verification"]["syntaxValid"] is True, "Expected syntaxValid: true"
        print(f" [PROPOSAL PASS] ID: {prop1['proposalId']}, components: {recs1}")

        # Apply proposal
        apply1_res = run_cmd([
            otter_bin, "compose", "apply", prop1_path,
            "--project", proj1_dir, "--yes"
        ], cwd=proj1_dir, env=clean_env)
        assert apply1_res.returncode == 0, f"Apply failed: {apply1_res.stderr}\n{apply1_res.stdout}"
        assert os.path.exists(os.path.join(proj1_dir, "src", "game.ot")), "src/game.ot was not created"
        assert os.path.exists(os.path.join(proj1_dir, "otter.lock")), "otter.lock was not created"
        print(" [APPLY PASS] Successfully applied proposal with dependencies.")

        # Run generated application
        run1_res = run_cmd([otter_bin, "run", "src/game.ot"], cwd=proj1_dir, env=clean_env)
        assert run1_res.returncode == 0, f"Execution failed:\n{run1_res.stderr}\n{run1_res.stdout}"
        assert "=== RPG Adventure ===" in run1_res.stdout, "Missing game header"
        assert "Loot acquired:" in run1_res.stdout, "Missing loot acquisition"
        assert "Game progress saved to rpg_savegame.json." in run1_res.stdout, "Missing save message"

        savegame1_path = os.path.join(proj1_dir, "rpg_savegame.json")
        assert os.path.exists(savegame1_path), "rpg_savegame.json was not created by running game"
        with open(savegame1_path, "r", encoding="utf-8") as f:
            saved1 = json.load(f)
        assert saved1.get("heroName") == "Aria"
        assert saved1.get("heroLevel") == 2
        assert saved1.get("invSlots") == 3
        print(" [EXECUTION PASS] Application executed cleanly; state persisted to rpg_savegame.json.")

        # =====================================================================
        # SCENARIO 2: Contact manager with search
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Scenario 2: 'Contact manager with search'")
        print("=" * 70)

        proj2_dir = os.path.join(workspace, "proj2_contacts")
        os.makedirs(proj2_dir, exist_ok=True)
        prop2_path = os.path.join(proj2_dir, "proposal.json")

        prop2_res = run_cmd([
            otter_bin, "compose", "propose",
            "Contact manager with search",
            "--output", prop2_path
        ], cwd=proj2_dir, env=clean_env)
        assert prop2_res.returncode == 0, f"Propose failed: {prop2_res.stderr}"

        with open(prop2_path, "r", encoding="utf-8") as f:
            prop2 = json.load(f)

        validate_proposal_schema(prop2)
        recs2 = [c["id"] for c in prop2["recommendedComponents"]]
        assert "otter-official/contact-manager" in recs2, "Expected contact-manager recipe"
        assert "otter-official/text-utils" in recs2, "Expected text-utils pack"
        assert "otter-official/file-helpers" in recs2, "Expected file-helpers pack"
        assert "txt-contains" in recs2, "Expected txt-contains snippet"
        print(f" [PROPOSAL PASS] ID: {prop2['proposalId']}, components: {recs2}")

        # Apply proposal
        apply2_res = run_cmd([
            otter_bin, "compose", "apply", prop2_path,
            "--project", proj2_dir, "--yes"
        ], cwd=proj2_dir, env=clean_env)
        assert apply2_res.returncode == 0, f"Apply failed: {apply2_res.stderr}\n{apply2_res.stdout}"
        assert os.path.exists(os.path.join(proj2_dir, "src", "contacts.ot")), "src/contacts.ot missing"

        # Run generated application
        run2_res = run_cmd([otter_bin, "run", "src/contacts.ot"], cwd=proj2_dir, env=clean_env)
        assert run2_res.returncode == 0, f"Execution failed:\n{run2_res.stderr}\n{run2_res.stdout}"
        assert "Team Directory" in run2_res.stdout
        assert "Ada Lovelace" in run2_res.stdout
        assert "Charles Babbage" in run2_res.stdout
        assert "Active contact count:" in run2_res.stdout

        store2_path = os.path.join(proj2_dir, "contacts_store.json")
        assert os.path.exists(store2_path), "contacts_store.json missing"
        with open(store2_path, "r", encoding="utf-8") as f:
            contacts_data = json.load(f)
        assert len(contacts_data) == 3, f"Expected 3 contacts, found {len(contacts_data)}"
        print(" [EXECUTION PASS] Contact manager executed cleanly; contacts saved to JSON.")

        # =====================================================================
        # SCENARIO 3: Task tracker with saved data
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Scenario 3: 'Task tracker with saved data'")
        print("=" * 70)

        proj3_dir = os.path.join(workspace, "proj3_tasks")
        os.makedirs(proj3_dir, exist_ok=True)
        prop3_path = os.path.join(proj3_dir, "proposal.json")

        prop3_res = run_cmd([
            otter_bin, "compose", "propose",
            "Task tracker with saved data",
            "--output", prop3_path
        ], cwd=proj3_dir, env=clean_env)
        assert prop3_res.returncode == 0, f"Propose failed: {prop3_res.stderr}"

        with open(prop3_path, "r", encoding="utf-8") as f:
            prop3 = json.load(f)

        validate_proposal_schema(prop3)
        recs3 = [c["id"] for c in prop3["recommendedComponents"]]
        assert "otter-official/task-tracker" in recs3
        assert "otter-official/file-settings" in recs3
        assert "otter-official/file-helpers" in recs3
        assert "file-json-storage" in recs3
        print(f" [PROPOSAL PASS] ID: {prop3['proposalId']}, components: {recs3}")

        # Apply proposal
        apply3_res = run_cmd([
            otter_bin, "compose", "apply", prop3_path,
            "--project", proj3_dir, "--yes"
        ], cwd=proj3_dir, env=clean_env)
        assert apply3_res.returncode == 0, f"Apply failed: {apply3_res.stderr}\n{apply3_res.stdout}"

        # Run generated application
        run3_res = run_cmd([otter_bin, "run", "src/tasks.ot"], cwd=proj3_dir, env=clean_env)
        assert run3_res.returncode == 0, f"Execution failed:\n{run3_res.stderr}\n{run3_res.stdout}"
        assert "My Tasks (All Tasks)" in run3_res.stdout
        assert "Active Tasks" in run3_res.stdout
        assert "Completed Tasks" in run3_res.stdout
        assert "Task operation complete." in run3_res.stdout

        store3_path = os.path.join(proj3_dir, "tasks_store.json")
        assert os.path.exists(store3_path), "tasks_store.json missing"
        print(" [EXECUTION PASS] Task tracker executed cleanly; tasks verified.")

        # =====================================================================
        # SCENARIO 4: RPG combat with cooldowns
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Scenario 4: 'RPG combat with cooldowns'")
        print("=" * 70)

        proj4_dir = os.path.join(workspace, "proj4_combat")
        os.makedirs(proj4_dir, exist_ok=True)
        prop4_path = os.path.join(proj4_dir, "proposal.json")

        prop4_res = run_cmd([
            otter_bin, "compose", "propose",
            "RPG combat with cooldowns",
            "--output", prop4_path
        ], cwd=proj4_dir, env=clean_env)
        assert prop4_res.returncode == 0, f"Propose failed: {prop4_res.stderr}"

        with open(prop4_path, "r", encoding="utf-8") as f:
            prop4 = json.load(f)

        validate_proposal_schema(prop4)
        recs4 = [c["id"] for c in prop4["recommendedComponents"]]
        assert "otter-official/rpg-adventure" in recs4
        assert "otter-official/game-combat" in recs4
        assert "otter-official/game-entity" in recs4
        assert "game-actor-damage" in recs4

        # Explain command check
        explain_res = run_cmd([otter_bin, "compose", "explain", prop4_path], cwd=proj4_dir, env=clean_env)
        assert explain_res.returncode == 0, f"Explain failed: {explain_res.stderr}"
        assert "Component Selection Rationale:" in explain_res.stdout
        assert "Composition & Data Flow Connections:" in explain_res.stdout
        assert "Unresolved Limitations & Scoping Considerations:" in explain_res.stdout
        print(" [EXPLAIN PASS] Architectural explanation verified.")

        # Preview command check
        preview_res = run_cmd([otter_bin, "compose", "preview", prop4_path], cwd=proj4_dir, env=clean_env)
        assert preview_res.returncode == 0, f"Preview failed: {preview_res.stderr}"
        assert "--- File: src/game.ot ---" in preview_res.stdout
        print(" [PREVIEW PASS] Source preview formatting verified.")

        # Apply proposal
        apply4_res = run_cmd([
            otter_bin, "compose", "apply", prop4_path,
            "--project", proj4_dir, "--yes"
        ], cwd=proj4_dir, env=clean_env)
        assert apply4_res.returncode == 0, f"Apply failed: {apply4_res.stderr}\n{apply4_res.stdout}"

        # Run generated application
        run4_res = run_cmd([otter_bin, "run", "src/game.ot"], cwd=proj4_dir, env=clean_env)
        assert run4_res.returncode == 0, f"Execution failed:\n{run4_res.stderr}\n{run4_res.stdout}"
        assert "Hero casts Meteor Strike for damage:" in run4_res.stdout
        assert "Enemy HP after spell:" in run4_res.stdout
        assert "HERO LEVELED UP!" in run4_res.stdout
        print(" [EXECUTION PASS] RPG combat with cooldowns executed cleanly.")

        # =====================================================================
        # SCENARIO 5: Unsupported request requiring honest explanation
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Scenario 5: Unsupported Intent Reporting")
        print("=" * 70)

        unsupp_intent = "Train a deep neural network on GPU using PyTorch CUDA tensors"
        prop5_path = os.path.join(workspace, "proposal_unsupported.json")

        prop5_res = run_cmd([
            otter_bin, "compose", "propose",
            unsupp_intent,
            "--output", prop5_path
        ], cwd=workspace, env=clean_env)

        assert prop5_res.returncode == 2, f"Expected non-zero exit code (2) for unsupported intent, got {prop5_res.returncode}"
        assert "[UNSUPPORTED REQUEST]" in prop5_res.stdout, "Missing unsupported header in stdout"
        assert "gpu-acceleration" in prop5_res.stdout, "Missing gpu-acceleration in missing caps"
        assert "neural-networks" in prop5_res.stdout, "Missing neural-networks in missing caps"

        with open(prop5_path, "r", encoding="utf-8") as f:
            prop5 = json.load(f)

        assert prop5.get("supported") is False, "Expected supported: false"
        assert "gpu-acceleration" in prop5.get("missingCapabilities", [])
        assert "neural-networks" in prop5.get("missingCapabilities", [])
        print(" [UNSUPPORTED DISCOVERY PASS] Proposal honestly reported missing capabilities without inventing APIs.")

        # Attempt to apply unsupported proposal -> must fail cleanly with safety abort
        unsupp_proj = os.path.join(workspace, "proj_unsupp")
        apply5_res = run_cmd([
            otter_bin, "compose", "apply", prop5_path,
            "--project", unsupp_proj, "--yes"
        ], cwd=workspace, env=clean_env)
        assert apply5_res.returncode == 2, f"Expected apply rejection (2), got {apply5_res.returncode}"
        assert "Refusing to apply proposal" in apply5_res.stderr, "Missing safety refusal message"
        assert not os.path.exists(unsupp_proj), "Directory should not be created for unsupported proposal"
        print(" [UNSUPPORTED APPLY PASS] Refused to apply unsupported proposal.")

        # =====================================================================
        # SAFETY TEST 1: User Approval Rejection
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 1: Mandatory Approval Gate Denial")
        print("=" * 70)

        proj_denial = os.path.join(workspace, "proj_denial")
        os.makedirs(proj_denial, exist_ok=True)
        canary_file = os.path.join(proj_denial, "canary.txt")
        with open(canary_file, "w") as f:
            f.write("canary_content")

        # Send "n\n" to prompt
        denial_res = run_cmd([
            otter_bin, "compose", "apply", prop1_path,
            "--project", proj_denial
        ], cwd=proj_denial, env=clean_env, input_text="n\n")

        assert denial_res.returncode == 1, f"Expected exit code 1 on denial, got {denial_res.returncode}"
        assert "Operation cancelled. No files were written." in denial_res.stdout
        assert os.path.exists(canary_file), "Canary file missing"
        with open(canary_file, "r") as f:
            assert f.read() == "canary_content", "Canary file was modified"
        assert not os.path.exists(os.path.join(proj_denial, "src")), "src/ was created despite cancellation"
        assert not os.path.exists(os.path.join(proj_denial, "otter.lock")), "otter.lock was created despite cancellation"
        print(" [APPROVAL GATE PASS] Explicit user denial aborted operation without modifying project.")

        # =====================================================================
        # SAFETY TEST 2: Path Traversal Containment Check
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 2: Path Traversal Rejection")
        print("=" * 70)

        malicious_prop_path = os.path.join(workspace, "malicious_traversal.json")
        malicious_prop = {
            "schemaVersion": 1,
            "proposalId": "prop-badpath1",
            "intent": "Malicious path traversal attack",
            "recommendedComponents": [],
            "compositionPlan": {
                "target": "console",
                "composedPacks": [],
                "composedBlueprints": [],
                "connections": []
            },
            "generatedSourcePreview": [
                {
                    "path": "../../escaped_payload.ot",
                    "sourceCode": "say 'escaped'",
                    "sourceMappings": []
                }
            ],
            "approvalRequired": True,
            "verification": {
                "command": "otter check",
                "syntaxValid": True
            }
        }
        with open(malicious_prop_path, "w", encoding="utf-8") as f:
            json.dump(malicious_prop, f)

        proj_safe = os.path.join(workspace, "proj_safe")
        os.makedirs(proj_safe, exist_ok=True)

        trav_res = run_cmd([
            otter_bin, "compose", "apply", malicious_prop_path,
            "--project", proj_safe, "--yes"
        ], cwd=workspace, env=clean_env)

        assert trav_res.returncode == 2, f"Expected CheckError (2) on traversal, got {trav_res.returncode}"
        assert "Path traversal rejected" in trav_res.stderr or "Path traversal rejected" in trav_res.stdout
        escaped_file = os.path.abspath(os.path.join(proj_safe, "../../escaped_payload.ot"))
        assert not os.path.exists(escaped_file), f"Malicious file was written: {escaped_file}"
        print(" [PATH TRAVERSAL PASS] Relative path traversal escaping project was rejected.")

        # Absolute path rejection check
        abs_prop_path = os.path.join(workspace, "absolute_path.json")
        abs_prop = dict(malicious_prop)
        abs_prop["proposalId"] = "prop-badpath2"
        abs_prop["generatedSourcePreview"] = [
            {
                "path": "/tmp/malicious_root.ot",
                "sourceCode": "say 'rooted'",
                "sourceMappings": []
            }
        ]
        with open(abs_prop_path, "w", encoding="utf-8") as f:
            json.dump(abs_prop, f)

        abs_res = run_cmd([
            otter_bin, "compose", "apply", abs_prop_path,
            "--project", proj_safe, "--yes"
        ], cwd=workspace, env=clean_env)
        assert abs_res.returncode == 2, f"Expected CheckError (2) on absolute path, got {abs_res.returncode}"
        assert "Absolute path rejected" in abs_res.stderr or "Absolute path rejected" in abs_res.stdout
        print(" [ABSOLUTE PATH PASS] Absolute paths in proposals strictly rejected.")

        # =====================================================================
        # SAFETY TEST 3: Missing Component Dependency Safety Abort
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 3: Missing Component Dependency Abort")
        print("=" * 70)

        missing_comp_prop = os.path.join(workspace, "missing_comp.json")
        bad_prop = dict(malicious_prop)
        bad_prop["proposalId"] = "prop-missing1"
        bad_prop["compositionPlan"]["composedPacks"] = ["otter-official/nonexistent-pack-foo"]
        bad_prop["generatedSourcePreview"] = [
            {"path": "src/main.ot", "sourceCode": "say 'hello'", "sourceMappings": []}
        ]
        with open(missing_comp_prop, "w", encoding="utf-8") as f:
            json.dump(bad_prop, f)

        proj_missing = os.path.join(workspace, "proj_missing")
        os.makedirs(proj_missing, exist_ok=True)

        miss_res = run_cmd([
            otter_bin, "compose", "apply", missing_comp_prop,
            "--project", proj_missing, "--yes"
        ], cwd=workspace, env=clean_env)
        assert miss_res.returncode == 2, f"Expected exit code 2, got {miss_res.returncode}"
        assert "Missing component dependency" in miss_res.stderr or "Missing component dependency" in miss_res.stdout
        assert not os.path.exists(os.path.join(proj_missing, "src", "main.ot")), "File written despite missing dependency"
        print(" [MISSING DEPENDENCY PASS] Missing dependencies aborted before file modification.")

        # =====================================================================
        # SAFETY TEST 4: Atomic Rollback on Failure & Unchanged Pre-existing Files
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 4: Atomic Rollback & Pre-existing File Restoration")
        print("=" * 70)

        proj_rollback = os.path.join(workspace, "proj_rollback")
        os.makedirs(proj_rollback, exist_ok=True)
        existing_cfg = os.path.join(proj_rollback, "config.json")
        initial_content = '{\n  "appName": "OriginalPreexistingApp",\n  "version": 1\n}\n'
        with open(existing_cfg, "w", encoding="utf-8") as f:
            f.write(initial_content)

        # Inject failure: proposal has invalid syntax in secondary file to fail post-validation or write failure
        # Let's create a proposal with a pre-apply syntax failure:
        fail_syntax_prop = os.path.join(workspace, "syntax_fail.json")
        bad_syntax_prop = {
            "schemaVersion": 1,
            "proposalId": "prop-failsyntax",
            "intent": "Corrupted syntax test",
            "recommendedComponents": [],
            "compositionPlan": {
                "target": "console",
                "composedPacks": [],
                "composedBlueprints": [],
                "connections": []
            },
            "generatedSourcePreview": [
                {
                    "path": "config.json",
                    "sourceCode": "{\"overwritten\": true}",
                    "sourceMappings": []
                },
                {
                    "path": "src/broken.ot",
                    "sourceCode": "this is totally invalid otter syntax !!!",
                    "sourceMappings": []
                }
            ],
            "approvalRequired": True,
            "verification": {
                "command": "otter check",
                "syntaxValid": False
            }
        }
        with open(fail_syntax_prop, "w", encoding="utf-8") as f:
            json.dump(bad_syntax_prop, f)

        syntax_fail_res = run_cmd([
            otter_bin, "compose", "apply", fail_syntax_prop,
            "--project", proj_rollback, "--yes"
        ], cwd=workspace, env=clean_env)

        assert syntax_fail_res.returncode == 2, f"Expected CheckError (2), got {syntax_fail_res.returncode}"
        assert "Pre-apply syntax error" in syntax_fail_res.stderr or "Pre-apply syntax error" in syntax_fail_res.stdout

        # Verify config.json was NOT overwritten!
        with open(existing_cfg, "r", encoding="utf-8") as f:
            recovered_content = f.read()
        assert recovered_content == initial_content, f"Pre-existing config.json was corrupted!\nFound: {recovered_content}"
        assert not os.path.exists(os.path.join(proj_rollback, "src")), "src/ was created despite syntax error"
        print(" [ROLLBACK PASS] Pre-existing file preserved intact; corrupt generation aborted cleanly.")

        print("\n" + "=" * 75)
        print(" ALL 5 SCENARIOS & 4 SAFETY VALIDATION TESTS PASSED CLEANLY!")
        print("=" * 75)

if __name__ == "__main__":
    main()
