#!/usr/bin/env python3
"""
Otter Phase 10 — Trustworthy Reusable Composition Consumer Test Suite
Tests that Otter composition engine:
1. Replaces keyword-only guessing with metadata-driven capability discovery and explicit capability coverage.
2. Adheres to composition-proposal schema with explainable component rationale and connections.
3. Honestly reports unsupported requests when no compatible library components exist.
4. Safely applies proposals transactionally (path containment, symlink escape protection, pre-write preview, approval gate).
5. Installs dependencies, commits files atomically, verifies projects, and rolls back cleanly on any failure.
6. Handles failure injection post-commit and post-pack-install, verifying byte-for-byte state restoration.
7. Exercises 5 distinct real-world application requests outside the repository from a fresh install.
"""

import os
import sys
import subprocess
import tempfile
import json
import re
import hashlib
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

def compute_sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()

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
    assert "capabilityCoverage" in proposal and isinstance(proposal["capabilityCoverage"], list), "Missing capabilityCoverage"

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

        # Verify capability coverage matrix
        cov1 = {c["capability"]: c["status"] for c in prop1["capabilityCoverage"]}
        assert cov1.get("inventory-management") == "implemented", f"Expected inventory-management implemented, got: {cov1}"
        assert cov1.get("persistence") == "implemented", f"Expected persistence implemented, got: {cov1}"
        print(f" [PROPOSAL PASS] ID: {prop1['proposalId']}, components: {recs1}")
        print(f" [COVERAGE PASS] Capabilities: {cov1}")

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

        cov2 = {c["capability"]: c["status"] for c in prop2["capabilityCoverage"]}
        assert cov2.get("contact-management") == "implemented"
        assert cov2.get("search") == "implemented"
        print(f" [PROPOSAL PASS] ID: {prop2['proposalId']}, components: {recs2}")
        print(f" [COVERAGE PASS] Capabilities: {cov2}")

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

        cov3 = {c["capability"]: c["status"] for c in prop3["capabilityCoverage"]}
        assert cov3.get("task-tracking") == "implemented"
        assert cov3.get("persistence") == "implemented"
        print(f" [PROPOSAL PASS] ID: {prop3['proposalId']}, components: {recs3}")
        print(f" [COVERAGE PASS] Capabilities: {cov3}")

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

        cov4 = {c["capability"]: c["status"] for c in prop4["capabilityCoverage"]}
        assert cov4.get("combat-and-cooldowns") == "implemented"
        print(f" [PROPOSAL PASS] ID: {prop4['proposalId']}, components: {recs4}")
        print(f" [COVERAGE PASS] Capabilities: {cov4}")

        # Explain command check
        explain_res = run_cmd([otter_bin, "compose", "explain", prop4_path], cwd=proj4_dir, env=clean_env)
        assert explain_res.returncode == 0, f"Explain failed: {explain_res.stderr}"
        assert "Capability Coverage Matrix:" in explain_res.stdout
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
        # SCENARIO 5: Unsupported Intent Reporting
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
        cov5 = {c["capability"]: c["status"] for c in prop5.get("capabilityCoverage", [])}
        assert cov5.get("gpu-acceleration") == "unsupported"
        assert cov5.get("neural-networks") == "unsupported"
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
            "proposalId": "prop-baad0001",
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
                    "sourceCode": "say \"escaped\"",
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
        assert "Path traversal" in trav_res.stderr or "Path traversal" in trav_res.stdout
        escaped_file = os.path.abspath(os.path.join(proj_safe, "../../escaped_payload.ot"))
        assert not os.path.exists(escaped_file), f"Malicious file was written: {escaped_file}"
        print(" [PATH TRAVERSAL PASS] Relative path traversal escaping project was rejected.")

        # Absolute path rejection check
        abs_prop_path = os.path.join(workspace, "absolute_path.json")
        abs_prop = dict(malicious_prop)
        abs_prop["proposalId"] = "prop-baad0002"
        abs_prop["generatedSourcePreview"] = [
            {
                "path": "/tmp/malicious_root.ot",
                "sourceCode": "say \"rooted\"",
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
        # SAFETY TEST 3: Symbolic Link Escape Protection
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 3: Symbolic Link Escape Protection")
        print("=" * 70)

        proj_symlink = os.path.join(workspace, "proj_symlink")
        os.makedirs(proj_symlink, exist_ok=True)

        # Create external target directory
        external_dir = os.path.join(workspace, "external_target")
        os.makedirs(external_dir, exist_ok=True)

        # Create a symlink inside project pointing outside: proj_symlink/sym_folder -> external_dir
        symlink_dir = os.path.join(proj_symlink, "sym_folder")
        try:
            os.symlink(external_dir, symlink_dir)
        except OSError:
            # On some platforms without symlink privilege, skip symlink creation if unsupported
            pass

        if os.path.islink(symlink_dir):
            symlink_prop = dict(malicious_prop)
            symlink_prop["proposalId"] = "prop-53311001"
            symlink_prop["generatedSourcePreview"] = [
                {
                    "path": "sym_folder/escaped.ot",
                    "sourceCode": "say \"escaped via symlink\"",
                    "sourceMappings": []
                }
            ]
            symlink_prop_path = os.path.join(workspace, "symlink_prop.json")
            with open(symlink_prop_path, "w", encoding="utf-8") as f:
                json.dump(symlink_prop, f)

            sym_res = run_cmd([
                otter_bin, "compose", "apply", symlink_prop_path,
                "--project", proj_symlink, "--yes"
            ], cwd=workspace, env=clean_env)

            assert sym_res.returncode == 2, f"Expected CheckError (2) on symlink escape, got {sym_res.returncode}"
            assert "Symlink escape rejected" in sym_res.stderr or "Symlink escape rejected" in sym_res.stdout
            assert not os.path.exists(os.path.join(external_dir, "escaped.ot")), "File was written through symlink into external dir!"
            print(" [SYMLINK ESCAPE PASS] Directory symlink pointing outside project was rejected.")

            # Test symlink file overwrite rejection
            sym_file_path = os.path.join(proj_symlink, "sym_file.ot")
            external_file = os.path.join(external_dir, "ext.txt")
            with open(external_file, "w") as f:
                f.write("sensitive_external_content")
            os.symlink(external_file, sym_file_path)

            sym_file_prop = dict(malicious_prop)
            sym_file_prop["proposalId"] = "prop-53311002"
            sym_file_prop["generatedSourcePreview"] = [
                {
                    "path": "sym_file.ot",
                    "sourceCode": "say \"overwrite attempt\"",
                    "sourceMappings": []
                }
            ]
            sym_file_prop_path = os.path.join(workspace, "sym_file_prop.json")
            with open(sym_file_prop_path, "w", encoding="utf-8") as f:
                json.dump(sym_file_prop, f)

            sym_f_res = run_cmd([
                otter_bin, "compose", "apply", sym_file_prop_path,
                "--project", proj_symlink, "--yes"
            ], cwd=workspace, env=clean_env)
            assert sym_f_res.returncode == 2, f"Expected CheckError (2), got {sym_f_res.returncode}"
            assert "Symlink overwrite rejected" in sym_f_res.stderr or "Symlink overwrite rejected" in sym_f_res.stdout
            with open(external_file, "r") as f:
                assert f.read() == "sensitive_external_content", "External file was overwritten through symlink!"
            print(" [SYMLINK OVERWRITE PASS] Direct overwrite of symbolic link file rejected.")

        # =====================================================================
        # SAFETY TEST 4: Duplicate Destination Output Paths Rejection
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 4: Duplicate Destination Paths Rejection")
        print("=" * 70)

        dup_prop_path = os.path.join(workspace, "duplicate_paths.json")
        dup_prop = {
            "schemaVersion": 1,
            "proposalId": "prop-da001122",
            "intent": "Duplicate paths proposal",
            "recommendedComponents": [],
            "compositionPlan": {
                "target": "console",
                "composedPacks": [],
                "composedBlueprints": [],
                "connections": []
            },
            "generatedSourcePreview": [
                {
                    "path": "src/main.ot",
                    "sourceCode": "say \"one\"",
                    "sourceMappings": []
                },
                {
                    "path": "src/./main.ot",
                    "sourceCode": "say \"duplicate\"",
                    "sourceMappings": []
                }
            ],
            "approvalRequired": True,
            "verification": {
                "command": "otter check",
                "syntaxValid": True
            }
        }
        with open(dup_prop_path, "w", encoding="utf-8") as f:
            json.dump(dup_prop, f)

        proj_dup = os.path.join(workspace, "proj_dup")
        os.makedirs(proj_dup, exist_ok=True)

        dup_res = run_cmd([
            otter_bin, "compose", "apply", dup_prop_path,
            "--project", proj_dup, "--yes"
        ], cwd=workspace, env=clean_env)

        assert dup_res.returncode == 2, f"Expected CheckError (2) on duplicate paths, got {dup_res.returncode}"
        assert "Duplicate destination file path" in dup_res.stderr or "Duplicate destination file path" in dup_res.stdout
        assert not os.path.exists(os.path.join(proj_dup, "src", "main.ot")), "File written despite duplicate path error"
        print(" [DUPLICATE PATHS PASS] Duplicate destination paths detected and rejected before staging.")

        # =====================================================================
        # SAFETY TEST 5: Malformed Proposals & Invalid ID Regex
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 5: Malformed Proposals & Invalid ID Regex")
        print("=" * 70)

        # 1. Invalid proposalId (short hex, not matching regex)
        short_id_prop = dict(dup_prop)
        short_id_prop["proposalId"] = "prop-12"
        short_prop_path = os.path.join(workspace, "short_id.json")
        with open(short_prop_path, "w", encoding="utf-8") as f:
            json.dump(short_id_prop, f)

        short_res = run_cmd([
            otter_bin, "compose", "apply", short_prop_path,
            "--project", proj_dup, "--yes"
        ], cwd=workspace, env=clean_env)
        assert short_res.returncode == 2, f"Expected rejection on short ID, got {short_res.returncode}"
        assert "Malformed proposal" in short_res.stderr or "Malformed proposal" in short_res.stdout
        print(" [MALFORMED ID PASS] Proposal with invalid proposalId pattern rejected.")

        # 2. Corrupt JSON syntax
        bad_json_path = os.path.join(workspace, "corrupt.json")
        with open(bad_json_path, "w") as f:
            f.write("{ this is not valid json")

        corrupt_res = run_cmd([
            otter_bin, "compose", "apply", bad_json_path,
            "--project", proj_dup, "--yes"
        ], cwd=workspace, env=clean_env)
        assert corrupt_res.returncode == 1, f"Expected UsageError on corrupt JSON, got {corrupt_res.returncode}"
        print(" [CORRUPT JSON PASS] Malformed JSON syntax caught cleanly.")

        # =====================================================================
        # SAFETY TEST 6: Incompatible Version Dependency Check
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 6: Incompatible Version Dependency Check")
        print("=" * 70)

        # Check a non-existent or incompatible component
        incompat_res = run_cmd([
            otter_bin, "compose", "check", "otter-official/nonexistent-pack", "--json"
        ], cwd=workspace, env=clean_env)
        assert incompat_res.returncode == 2, f"Expected exit code 2, got {incompat_res.returncode}"
        incompat_json = json.loads(incompat_res.stdout)
        assert incompat_json.get("compatible") is False
        print(" [INCOMPATIBLE CHECK PASS] Incompatible/missing component reported properly.")

        # =====================================================================
        # SAFETY TEST 7: Deterministic Failure Injection Rollback (Post-Commit)
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 7: Deterministic Rollback After File Commit & Pack Install")
        print("=" * 70)

        proj_inject1 = os.path.join(workspace, "proj_inject1")
        os.makedirs(proj_inject1, exist_ok=True)

        # 1. Pre-existing original file
        existing_cfg1 = os.path.join(proj_inject1, "config.json")
        orig_cfg_bytes = b'{\n  "appName": "OriginalApp",\n  "status": "initial"\n}\n'
        with open(existing_cfg1, "wb") as f:
            f.write(orig_cfg_bytes)
        orig_cfg_hash = hashlib.sha256(orig_cfg_bytes).hexdigest()

        # 2. Pre-existing project manifest (otter.json)
        existing_manifest1 = os.path.join(proj_inject1, "otter.json")
        orig_manifest_bytes = b'{\n  "name": "my-project",\n  "version": "1.0.0"\n}\n'
        with open(existing_manifest1, "wb") as f:
            f.write(orig_manifest_bytes)
        orig_manifest_hash = hashlib.sha256(orig_manifest_bytes).hexdigest()

        # 3. Pre-existing project source file
        existing_src1 = os.path.join(proj_inject1, "src", "existing_module.ot")
        os.makedirs(os.path.dirname(existing_src1), exist_ok=True)
        orig_src_bytes = b'say "pre-existing module untouched"\n'
        with open(existing_src1, "wb") as f:
            f.write(orig_src_bytes)
        orig_src_hash = hashlib.sha256(orig_src_bytes).hexdigest()

        # 4. Pre-install an existing dependency pack (math-utils)
        pre_install1 = run_cmd([
            otter_bin, "pack", "install", "otter-official/math-utils",
            "--project", proj_inject1
        ], cwd=workspace, env=clean_env)
        assert pre_install1.returncode == 0, f"Pre-install failed: {pre_install1.stderr}"

        # 5. Capture pre-existing lockfile hash
        existing_lock1 = os.path.join(proj_inject1, "otter.lock")
        assert os.path.exists(existing_lock1), "Expected otter.lock from pre-install"
        orig_lock_hash = compute_sha256(existing_lock1)

        # 6. Capture pre-existing dependency files hashes
        orig_dep_main1 = os.path.join(proj_inject1, ".otter", "packages", "otter-official", "math-utils", "1.0.1", "src", "main.ot")
        orig_dep_pack1 = os.path.join(proj_inject1, ".otter", "packages", "otter-official", "math-utils", "1.0.1", "otter.pack.json")
        assert os.path.exists(orig_dep_main1), "Pre-installed pack main.ot missing"
        assert os.path.exists(orig_dep_pack1), "Pre-installed pack otter.pack.json missing"
        orig_dep_main_hash1 = compute_sha256(orig_dep_main1)
        orig_dep_pack_hash1 = compute_sha256(orig_dep_pack1)

        # Multi-file proposal with dependencies:
        # Installs new pack (game-entity), modifies lockfile, commits file 1, and then fails on file 2!
        inject_prop = {
            "schemaVersion": 1,
            "proposalId": "prop-111ec710",
            "intent": "Failure injection test",
            "recommendedComponents": [],
            "compositionPlan": {
                "target": "console",
                "composedPacks": ["otter-official/game-entity"],
                "composedBlueprints": [],
                "connections": []
            },
            "generatedSourcePreview": [
                {
                    "path": "src/file1.ot",
                    "sourceCode": "say \"file 1 committed\"",
                    "sourceMappings": []
                },
                {
                    "path": "config.json",
                    "sourceCode": "{\"overwritten\": true}",
                    "sourceMappings": []
                }
            ],
            "approvalRequired": True,
            "verification": {
                "command": "otter check",
                "syntaxValid": True
            }
        }
        inject_prop_path = os.path.join(workspace, "inject_prop.json")
        with open(inject_prop_path, "w", encoding="utf-8") as f:
            json.dump(inject_prop, f)

        # Run with OTTER_TEST_INJECT_FAILURE=post-first-commit
        inject_env = clean_env.copy()
        inject_env["OTTER_TEST_INJECT_FAILURE"] = "post-first-commit"

        inj_res = run_cmd([
            otter_bin, "compose", "apply", inject_prop_path,
            "--project", proj_inject1, "--yes"
        ], cwd=workspace, env=inject_env)

        assert inj_res.returncode == 2, f"Expected failure (exit code 2), got {inj_res.returncode}"
        assert "[ROLLBACK]" in inj_res.stdout or "[ROLLBACK]" in inj_res.stderr, "Rollback message missing"

        # A. Verify byte-for-byte restoration of original files
        assert os.path.exists(existing_cfg1), "config.json was deleted during rollback!"
        assert compute_sha256(existing_cfg1) == orig_cfg_hash, "config.json was not restored byte-for-byte!"

        assert os.path.exists(existing_src1), "existing_module.ot was deleted during rollback!"
        assert compute_sha256(existing_src1) == orig_src_hash, "existing_module.ot was not restored byte-for-byte!"

        # B. Verify byte-for-byte restoration of project manifest (otter.json)
        assert os.path.exists(existing_manifest1), "otter.json was deleted during rollback!"
        assert compute_sha256(existing_manifest1) == orig_manifest_hash, "otter.json was not restored byte-for-byte!"

        # C. Verify byte-for-byte restoration of lockfile (otter.lock)
        assert os.path.exists(existing_lock1), "otter.lock was deleted during rollback!"
        assert compute_sha256(existing_lock1) == orig_lock_hash, "otter.lock was not restored byte-for-byte to pre-installation state!"

        # D. Verify byte-for-byte preservation of pre-existing dependency files
        assert os.path.exists(orig_dep_main1), "Pre-existing dependency file main.ot was removed or corrupted!"
        assert compute_sha256(orig_dep_main1) == orig_dep_main_hash1, "Pre-existing dependency file changed byte content!"
        assert os.path.exists(orig_dep_pack1), "Pre-existing dependency manifest was removed!"
        assert compute_sha256(orig_dep_pack1) == orig_dep_pack_hash1, "Pre-existing dependency manifest changed byte content!"

        # E. Verify newly created file1.ot was completely deleted!
        assert not os.path.exists(os.path.join(proj_inject1, "src", "file1.ot")), "Committed file1.ot was NOT deleted by rollback!"

        # F. Verify newly installed package (game-entity) was purged!
        assert not os.path.exists(os.path.join(proj_inject1, ".otter", "packages", "otter-official", "game-entity")), "Newly installed package was NOT purged by rollback!"

        # G. Verify no staging or backup directories remain
        otter_hidden = os.path.join(proj_inject1, ".otter")
        if os.path.exists(otter_hidden):
            for entry in os.listdir(otter_hidden):
                assert not entry.startswith(".staging_"), f"Dangling staging directory found: {entry}"
                assert not entry.startswith(".backups_"), f"Dangling backup directory found: {entry}"

        print(" [ACTUAL ROLLBACK PASS] Injected failure after file commit triggered atomic rollback.")
        print(f" [BYTE-FOR-BYTE RESTORED] config.json        SHA-256: {orig_cfg_hash}")
        print(f" [BYTE-FOR-BYTE RESTORED] otter.json         SHA-256: {orig_manifest_hash}")
        print(f" [BYTE-FOR-BYTE RESTORED] otter.lock         SHA-256: {orig_lock_hash}")
        print(f" [BYTE-FOR-BYTE RESTORED] existing_module.ot SHA-256: {orig_src_hash}")
        print(f" [BYTE-FOR-BYTE PRESERVED] math-utils dep   SHA-256: {orig_dep_main_hash1}")
        print(" [PURGED COMMITTED FILE] Newly created src/file1.ot was cleanly deleted.")
        print(" [PURGED INSTALLED PACK] Newly installed game-entity package was cleanly purged.")

        # =====================================================================
        # SAFETY TEST 8: Failure Injection Rollback After Pack Installation
        # =====================================================================
        print("\n" + "=" * 70)
        print(" Safety Test 8: Deterministic Rollback After Pack Installation")
        print("=" * 70)

        proj_inject2 = os.path.join(workspace, "proj_inject2")
        os.makedirs(proj_inject2, exist_ok=True)

        # 1. Pre-existing settings file
        existing_cfg2 = os.path.join(proj_inject2, "settings.json")
        orig_settings_bytes = b'{\n  "version": "1.0-preexisting"\n}\n'
        with open(existing_cfg2, "wb") as f:
            f.write(orig_settings_bytes)
        orig_settings_hash = hashlib.sha256(orig_settings_bytes).hexdigest()

        # 2. Pre-existing project manifest
        existing_manifest2 = os.path.join(proj_inject2, "otter.json")
        orig_manifest_bytes2 = b'{\n  "name": "settings-service",\n  "version": "2.1.0"\n}\n'
        with open(existing_manifest2, "wb") as f:
            f.write(orig_manifest_bytes2)
        orig_manifest_hash2 = hashlib.sha256(orig_manifest_bytes2).hexdigest()

        # 3. Pre-install existing dependency pack (text-utils)
        pre_install2 = run_cmd([
            otter_bin, "pack", "install", "otter-official/text-utils",
            "--project", proj_inject2
        ], cwd=workspace, env=clean_env)
        assert pre_install2.returncode == 0, f"Pre-install failed: {pre_install2.stderr}"

        # 4. Capture pre-existing lockfile hash
        existing_lock2 = os.path.join(proj_inject2, "otter.lock")
        assert os.path.exists(existing_lock2), "Expected otter.lock in proj_inject2"
        orig_lock_hash2 = compute_sha256(existing_lock2)

        # 5. Capture pre-existing dependency files
        orig_dep_main2 = os.path.join(proj_inject2, ".otter", "packages", "otter-official", "text-utils", "1.0.1", "src", "main.ot")
        assert os.path.exists(orig_dep_main2), "text-utils main.ot missing"
        orig_dep_main_hash2 = compute_sha256(orig_dep_main2)

        # Proposal with dependencies: packs install first, then failure injected!
        pack_inj_env = clean_env.copy()
        pack_inj_env["OTTER_TEST_INJECT_FAILURE"] = "post-pack-install"

        pack_inj_res = run_cmd([
            otter_bin, "compose", "apply", prop1_path,
            "--project", proj_inject2, "--yes"
        ], cwd=workspace, env=pack_inj_env)

        assert pack_inj_res.returncode == 2, f"Expected failure (2), got {pack_inj_res.returncode}"
        assert "[ROLLBACK]" in pack_inj_res.stdout or "[ROLLBACK]" in pack_inj_res.stderr

        # A. Verify settings.json restored byte-for-byte
        assert os.path.exists(existing_cfg2)
        assert compute_sha256(existing_cfg2) == orig_settings_hash

        # B. Verify otter.json restored byte-for-byte
        assert os.path.exists(existing_manifest2)
        assert compute_sha256(existing_manifest2) == orig_manifest_hash2

        # C. Verify otter.lock restored byte-for-byte to pre-installation state
        assert os.path.exists(existing_lock2)
        assert compute_sha256(existing_lock2) == orig_lock_hash2

        # D. Verify pre-existing dependency files preserved byte-for-byte
        assert os.path.exists(orig_dep_main2)
        assert compute_sha256(orig_dep_main2) == orig_dep_main_hash2

        # E. Verify no newly created project source files remain
        assert not os.path.exists(os.path.join(proj_inject2, "src", "game.ot"))

        # F. Verify newly installed packages were purged
        assert not os.path.exists(os.path.join(proj_inject2, ".otter", "packages", "otter-official", "game-entity"))

        # G. Verify no staging/backup directories remain
        otter_hidden2 = os.path.join(proj_inject2, ".otter")
        if os.path.exists(otter_hidden2):
            for entry in os.listdir(otter_hidden2):
                assert not entry.startswith(".staging_"), f"Dangling staging directory found: {entry}"
                assert not entry.startswith(".backups_"), f"Dangling backup directory found: {entry}"

        print(" [PACK ROLLBACK PASS] Injected failure after pack install rolled back package directories cleanly.")
        print(f" [BYTE-FOR-BYTE RESTORED] settings.json   SHA-256: {orig_settings_hash}")
        print(f" [BYTE-FOR-BYTE RESTORED] otter.json      SHA-256: {orig_manifest_hash2}")
        print(f" [BYTE-FOR-BYTE RESTORED] otter.lock      SHA-256: {orig_lock_hash2}")
        print(f" [BYTE-FOR-BYTE PRESERVED] text-utils dep SHA-256: {orig_dep_main_hash2}")
        print(" [PURGED NEW PACK] game-entity package directory was cleanly purged.")

        print("\n" + "=" * 75)
        print(" ALL 5 SCENARIOS & 8 SAFETY VALIDATION TESTS PASSED CLEANLY (100%)!")
        print("=" * 75)

if __name__ == "__main__":
    main()
