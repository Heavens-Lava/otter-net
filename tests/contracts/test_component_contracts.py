#!/usr/bin/env python3
"""
Test Suite: Component Contract Schema & Validation (Phase 11 Milestone 1)
Tests otter pack verify --contracts and otter pack contract commands.
Covers positive and negative test cases:
  1. Positive validation of migrated reference pack (otter-official/text-utils)
  2. Public contract inspection (otter pack contract)
  3. Negative: missing exported function in .ot source
  4. Negative: parameter count / signature mismatch
  5. Negative: invalid parameter & return types
  6. Negative: invalid version & runtime semver constraints
  7. Negative: undeclared package dependency in use statement
  8. Negative: variable prefix collision risk (Otter 1.0.1 dynamic scoping mitigation)
  9. Negative: malformed JSON manifest syntax
  10. Negative: invalid variablePrefix regex format
  11. Backward compatibility: existing v1 manifests pass standard verify
  12. Project-level contract verification via otter.lock
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OTTER_CLI_PROJECT = os.path.join(REPO_ROOT, "src", "Otter.Cli")
BUILT_CLI = os.path.join(REPO_ROOT, "src", "Otter.Cli", "bin", "Debug", "net10.0", "otter.dll")

def run_otter(args, cwd=None, env=None):
    cmd_env = os.environ.copy()
    if "OTTER_LIBRARY_PATH" not in cmd_env:
        cmd_env["OTTER_LIBRARY_PATH"] = os.path.join(REPO_ROOT, "library")
    if env:
        cmd_env.update(env)
    
    if os.path.exists(BUILT_CLI):
        cmd = ["dotnet", BUILT_CLI] + args
    else:
        cmd = ["dotnet", "run", "--project", OTTER_CLI_PROJECT, "--"] + args
    
    res = subprocess.run(
        cmd,
        cwd=cwd or REPO_ROOT,
        env=cmd_env,
        capture_output=True,
        text=True
    )
    return res

def test_migrated_pack_positive():
    print("--> Test 1: Positive verification of migrated text-utils pack...")
    pack_dir = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
    
    # Text output
    res = run_otter(["pack", "verify", pack_dir, "--contracts"])
    assert res.returncode == 0, f"Expected 0, got {res.returncode}. Output:\n{res.stdout}\n{res.stderr}"
    assert "Contract: [OK] (15 operations verified)" in res.stdout
    assert "repeatText" in res.stdout
    assert "slugify" in res.stdout
    assert "normalizeSpaces" in res.stdout
    
    # JSON output
    json_res = run_otter(["pack", "verify", pack_dir, "--contracts", "--json"])
    assert json_res.returncode == 0
    data = json.loads(json_res.stdout)
    assert data["status"] == "OK"
    assert data["contract"]["status"] == "VALID"
    assert data["contract"]["schemaVersion"] == 2
    assert data["contract"]["variablePrefix"] == "_tu_"
    assert data["contract"]["operationsCount"] == 15
    assert len(data["contract"]["diagnostics"]) == 0
    print("  [PASS] text-utils verified successfully with 15 operations.")

def test_contract_inspection():
    print("--> Test 2: Public contract inspection via 'otter pack contract'...")
    pack_dir = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
    
    # Text format
    res = run_otter(["pack", "contract", pack_dir])
    assert res.returncode == 0, f"Expected 0, got {res.returncode}. Output:\n{res.stdout}\n{res.stderr}"
    assert "Component Contract: otter-official/text-utils v1.0.1 (Schema v2)" in res.stdout
    assert "Variable Prefix: _tu_" in res.stdout
    assert "repeatText(src: text, timesCount: number) -> text [returns-fallback]" in res.stdout
    
    # JSON format
    json_res = run_otter(["pack", "contract", pack_dir, "--json"])
    assert json_res.returncode == 0
    data = json.loads(json_res.stdout)
    assert data["status"] == "VALID"
    assert data["pack"] == "otter-official/text-utils"
    print("  [PASS] Contract inspection outputs formatted signatures accurately.")

def test_negative_missing_function():
    print("--> Test 3: Negative - Missing exported function in source...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_fn_") as tmpdir:
        # Copy text-utils pack
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        # Add a declared operation that is missing from src/main.ot
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
        
        manifest["provides"]["operations"].append({
            "name": "nonExistentHelper",
            "description": "A phantom helper function not in source",
            "parameters": [{"name": "input", "type": "text"}],
            "returns": {"type": "text"},
            "sideEffects": ["none"],
            "errorMode": "returns-fallback"
        })
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2, f"Expected exit code 2, got {res.returncode}. Output:\n{res.stdout}"
        assert "MISSING_FUNCTION" in res.stdout
        assert "nonExistentHelper" in res.stdout
        assert "Contract: [FAIL]" in res.stdout
        print("  [PASS] Caught missing exported function with actionable diagnostic.")

def test_negative_parameter_count_mismatch():
    print("--> Test 4: Negative - Parameter count / arity mismatch...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_param_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        # Modify repeatText to declare 3 parameters instead of 2
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
        
        for op in manifest["provides"]["operations"]:
            if op["name"] == "repeatText":
                op["parameters"].append({"name": "extraFlag", "type": "boolean"})
                
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2, f"Expected exit code 2, got {res.returncode}"
        assert "PARAMETER_COUNT_MISMATCH" in res.stdout
        assert "repeatText" in res.stdout
        print("  [PASS] Caught parameter count mismatch cleanly.")

def test_negative_invalid_types():
    print("--> Test 5: Negative - Invalid parameter and return types...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_types_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
            
        manifest["provides"]["operations"][0]["parameters"][0]["type"] = "invalid_custom_type"
        manifest["provides"]["operations"][0]["returns"]["type"] = "unsupported_pointer"
        
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "INVALID_PARAM_TYPE" in res.stdout
        assert "INVALID_RETURN_TYPE" in res.stdout
        print("  [PASS] Caught invalid parameter and return types.")

def test_negative_invalid_version():
    print("--> Test 6: Negative - Invalid version and semver constraints...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_ver_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
            
        manifest["version"] = "1.0_invalid"
        manifest["requiresOtter"] = "not-a-valid-semver"
        
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "INVALID_VERSION" in res.stdout
        assert "INVALID_RUNTIME_CONSTRAINT" in res.stdout
        print("  [PASS] Caught invalid semantic version and runtime constraints.")

def test_negative_undeclared_dependency():
    print("--> Test 7: Negative - Undeclared package dependency in 'use'...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_dep_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        entry_path = os.path.join(pack_dir, "src", "main.ot")
        
        # Inject an undeclared dependency into src/main.ot
        with open(entry_path, "r") as f:
            content = f.read()
        with open(entry_path, "w") as f:
            f.write('use "otter-official/math-utils"\n' + content)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "UNDECLARED_DEPENDENCY" in res.stdout
        assert "otter-official/math-utils" in res.stdout
        print("  [PASS] Caught undeclared package dependency imported via 'use'.")

def test_negative_variable_collision_risk():
    print("--> Test 8: Negative - Variable prefix collision risk in function body...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_prefix_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        entry_path = os.path.join(pack_dir, "src", "main.ot")
        
        # Inject an unprefixed local variable assignment into repeatText
        with open(entry_path, "r") as f:
            content = f.read()
        
        # repeatText starts at line 7; inject 'temp is 100' inside repeatText
        modified = content.replace('_tu_result is ""', 'temp is 100\n    _tu_result is ""')
        with open(entry_path, "w") as f:
            f.write(modified)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "VARIABLE_COLLISION_RISK" in res.stdout
        assert "temp" in res.stdout
        assert "_tu_" in res.stdout
        print("  [PASS] Caught unprefixed local variable collision risk.")

def test_negative_malformed_json():
    print("--> Test 9: Negative - Malformed JSON in manifest...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_json_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        # Write corrupted JSON
        with open(manifest_path, "w") as f:
            f.write("{\n  \"schemaVersion\": 2,\n  \"name\": \"bad-json\",\n  BROKEN SYNTAX HERE\n")
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "MALFORMED_JSON" in res.stdout
        print("  [PASS] Caught malformed JSON syntax with line and character diagnostics.")

def test_negative_invalid_variable_prefix_format():
    print("--> Test 10: Negative - Invalid variablePrefix regex...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_pfx_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
            
        manifest["variablePrefix"] = "invalid_no_underscores"
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "INVALID_VARIABLE_PREFIX" in res.stdout
        print("  [PASS] Caught invalid variablePrefix format.")

def test_backward_compatibility():
    print("--> Test 11: Backward compatibility with unmodified Otter 1.0.1 packs...")
    list_dir = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "list-utils")
    
    # Standard verify continues working unchanged (exit 0)
    std_res = run_otter(["pack", "verify", list_dir])
    assert std_res.returncode == 0, f"Expected 0 for standard verify on list-utils, got {std_res.returncode}"
    assert "src/main.ot [OK]" in std_res.stdout
    
    # Contract verify on v1 pack reports schema v1 uncontracted error with migration guide
    contract_res = run_otter(["pack", "verify", list_dir, "--contracts"])
    assert contract_res.returncode == 2
    assert "SCHEMA_V1_UNCONTRACTED" in contract_res.stdout
    assert "Migrate manifest to schemaVersion: 2" in contract_res.stdout
    print("  [PASS] Existing Otter 1.0.1 packs continue working; migration guidance provided.")

def test_project_level_verification():
    print("--> Test 12: Project-level contract verification via otter.lock...")
    with tempfile.TemporaryDirectory(prefix="otter_test_proj_contracts_") as proj_dir:
        # Initialize project
        init_res = run_otter(["new", "console", "test-contracts-app"], cwd=proj_dir)
        app_dir = os.path.join(proj_dir, "test-contracts-app")
        
        # Install migrated pack text-utils
        inst_res = run_otter(["pack", "install", "otter-official/text-utils"], cwd=app_dir)
        assert inst_res.returncode == 0
        
        # Run standard verify
        std_res = run_otter(["pack", "verify"], cwd=app_dir)
        assert std_res.returncode == 0
        assert "[OK] otter-official/text-utils" in std_res.stdout
        
        # Run contract verify at project level
        contract_res = run_otter(["pack", "verify", "--contracts"], cwd=app_dir)
        assert contract_res.returncode == 0
        assert "[OK] otter-official/text-utils" in contract_res.stdout
        assert "Contract: [OK] (15 operations verified)" in contract_res.stdout
        
        # JSON project verification
        json_res = run_otter(["pack", "verify", "--contracts", "--json"], cwd=app_dir)
        assert json_res.returncode == 0
        items = json.loads(json_res.stdout)
        assert len(items) == 1
        assert items[0]["contract"]["status"] == "VALID"
        assert items[0]["contract"]["operationsCount"] == 15
        assert items[0]["contract"]["verificationScope"] == "structural_ast"
        print("  [PASS] Project-level contract verification verified.")

def test_negative_parameter_name_mismatch():
    print("--> Test 13: Negative - Parameter name mismatch with AST function...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_pname_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        # Modify repeatText parameter name in manifest from 'src' to 'wrongSourceParam'
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
        
        for op in manifest["provides"]["operations"]:
            if op["name"] == "repeatText":
                op["parameters"][0]["name"] = "wrongSourceParam"
                
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2, f"Expected exit code 2, got {res.returncode}"
        assert "PARAMETER_NAME_MISMATCH" in res.stdout
        assert "wrongSourceParam" in res.stdout
        assert "src" in res.stdout
        print("  [PASS] Caught parameter name mismatch with actionable diagnostic.")

def test_negative_misleading_contradictory_side_effects():
    print("--> Test 14: Negative - Contradictory side-effects in contract...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_se_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
            
        # Declare contradictory side effects: 'none' plus 'filesystem:write'
        manifest["provides"]["operations"][0]["sideEffects"] = ["none", "filesystem:write"]
        
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "MISLEADING_CONTRACT" in res.stdout
        assert "contradictory side-effect declarations" in res.stdout
        print("  [PASS] Caught contradictory side-effect declarations.")

def test_negative_misleading_error_mode_type_conflict():
    print("--> Test 15: Negative - Misleading errorMode and return type conflict...")
    with tempfile.TemporaryDirectory(prefix="otter_test_neg_errtype_") as tmpdir:
        src_pack = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
        shutil.copytree(src_pack, os.path.join(tmpdir, "pack"))
        pack_dir = os.path.join(tmpdir, "pack")
        manifest_path = os.path.join(pack_dir, "otter.pack.json")
        
        with open(manifest_path, "r") as f:
            manifest = json.load(f)
            
        # Declare repeatText returns 'number' but errorMode is 'returns-false'
        manifest["provides"]["operations"][0]["returns"]["type"] = "number"
        manifest["provides"]["operations"][0]["errorMode"] = "returns-false"
        
        with open(manifest_path, "w") as f:
            json.dump(manifest, f, indent=2)
            
        res = run_otter(["pack", "verify", pack_dir, "--contracts"])
        assert res.returncode == 2
        assert "MISLEADING_CONTRACT" in res.stdout
        assert "declares errorMode 'returns-false' but return type 'number'" in res.stdout
        print("  [PASS] Caught misleading errorMode / return type conflict.")

def test_structural_vs_behavioral_distinction():
    print("--> Test 16: Verification scope distinguishes structural AST from behavioral claims...")
    pack_dir = os.path.join(REPO_ROOT, "library", "packs", "otter-official", "text-utils")
    
    # Text output check
    res = run_otter(["pack", "contract", pack_dir])
    assert res.returncode == 0
    assert "Validation Scope:   Structural AST" in res.stdout
    assert "Behavioral Note:    Return types, side effects & error modes are declared" in res.stdout
    
    # JSON output check
    json_res = run_otter(["pack", "verify", pack_dir, "--contracts", "--json"])
    assert json_res.returncode == 0
    data = json.loads(json_res.stdout)
    contract = data["contract"]
    assert contract["verificationScope"] == "structural_ast"
    assert contract["behavioralVerification"] == "unproven_contract_specification"
    assert "Declared return types, side effects, and error modes are machine-readable contract specifications" in contract["behavioralNote"]
    assert "parameter_names" in contract["structuralChecks"]
    print("  [PASS] Explicitly distinguished structural AST verification from behavioral specifications.")

def main():
    print("======================================================================")
    print(" Otter Phase 11 Milestone 1: Component Contracts Verification Suite")
    print("======================================================================")
    
    test_migrated_pack_positive()
    test_contract_inspection()
    test_negative_missing_function()
    test_negative_parameter_count_mismatch()
    test_negative_invalid_types()
    test_negative_invalid_version()
    test_negative_undeclared_dependency()
    test_negative_variable_collision_risk()
    test_negative_malformed_json()
    test_negative_invalid_variable_prefix_format()
    test_backward_compatibility()
    test_project_level_verification()
    test_negative_parameter_name_mismatch()
    test_negative_misleading_contradictory_side_effects()
    test_negative_misleading_error_mode_type_conflict()
    test_structural_vs_behavioral_distinction()
    
    print("======================================================================")
    print(" ALL 16 CONTRACT VERIFICATION TESTS PASSED (100%)")
    print("======================================================================")

if __name__ == "__main__":
    main()

