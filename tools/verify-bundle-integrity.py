#!/usr/bin/env python3
"""
tools/verify-bundle-integrity.py
Automated Bundle & Distribution Integrity Verification for Otter 1.0.1

Compares bundled library copies against canonical repository library assets:
- Validates all 38 snippets against catalog.json
- Validates all 5 official Pack versions (math-utils 1.0.1, game-entity 1.1.0,
  text-utils 1.0.1, list-utils 1.0.1, file-helpers 1.0.1)
- Validates all 4 Blueprints and 4 Recipes
- Validates all 5 schema files
- Computes and verifies SHA-256 hashes of every bundled file against canonical source
- Detects stale pack versions, missing files, or modified/corrupted assets
"""

import os
import sys
import json
import hashlib
from typing import Dict, List, Tuple

EXPECTED_PACK_VERSIONS = {
    "math-utils": "1.0.1",
    "game-entity": "1.1.0",
    "text-utils": "1.0.1",
    "list-utils": "1.0.1",
    "file-helpers": "1.0.1",
}

EXPECTED_BLUEPRINTS = {
    "console-calculator",
    "file-settings",
    "game-combat",
    "text-cleanup",
}

EXPECTED_RECIPES = {
    "contact-manager",
    "rpg-adventure",
    "task-tracker",
    "web-portal",
}

EXPECTED_SCHEMAS = {
    "pack.schema.json",
    "snippet.schema.json",
    "blueprint.schema.json",
    "recipe.schema.json",
    "composition-proposal.schema.json",
}

EXPECTED_SNIPPET_COUNT = 38


def compute_sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def verify_target_library(canonical_dir: str, target_dir: str) -> Tuple[bool, List[str]]:
    errors = []
    
    if not os.path.isdir(target_dir):
        return False, [f"Target library directory does not exist: {target_dir}"]

    print(f"--> Verifying library bundle: {target_dir}")

    # 1. Check schemas
    schema_dir = os.path.join(target_dir, "schema")
    if not os.path.isdir(schema_dir):
        errors.append(f"Missing schema directory: {schema_dir}")
    else:
        found_schemas = set(os.listdir(schema_dir))
        for schema in EXPECTED_SCHEMAS:
            if schema not in found_schemas:
                errors.append(f"Missing required schema: {schema}")

    # 2. Check Snippets
    catalog_path = os.path.join(target_dir, "snippets", "catalog.json")
    if not os.path.isfile(catalog_path):
        errors.append(f"Missing snippet catalog: {catalog_path}")
    else:
        try:
            with open(catalog_path, "r", encoding="utf-8") as f:
                catalog_data = json.load(f)
            snippets = catalog_data.get("snippets", [])
            if len(snippets) != EXPECTED_SNIPPET_COUNT:
                errors.append(
                    f"Snippet count mismatch in catalog: expected {EXPECTED_SNIPPET_COUNT}, got {len(snippets)}"
                )
            for snip in snippets:
                if not snip.get("id") or not snip.get("example"):
                    errors.append(f"Malformed snippet entry in catalog: {snip.get('id', 'unknown')}")
        except Exception as ex:
            errors.append(f"Failed to read snippet catalog: {ex}")

    # 3. Check Blueprints
    bp_root = os.path.join(target_dir, "blueprints", "otter-official")
    if not os.path.isdir(bp_root):
        errors.append(f"Missing blueprints directory: {bp_root}")
    else:
        found_bps = set(os.listdir(bp_root))
        for bp in EXPECTED_BLUEPRINTS:
            if bp not in found_bps:
                errors.append(f"Missing expected blueprint: {bp}")
            else:
                bp_manifest = os.path.join(bp_root, bp, "otter.blueprint.json")
                if not os.path.isfile(bp_manifest):
                    errors.append(f"Blueprint manifest missing: {bp_manifest}")

    # 4. Check Recipes
    recipe_root = os.path.join(target_dir, "recipes", "otter-official")
    if not os.path.isdir(recipe_root):
        errors.append(f"Missing recipes directory: {recipe_root}")
    else:
        found_recipes = set(os.listdir(recipe_root))
        for r in EXPECTED_RECIPES:
            if r not in found_recipes:
                errors.append(f"Missing expected recipe: {r}")
            else:
                r_manifest = os.path.join(recipe_root, r, "otter.recipe.json")
                if not os.path.isfile(r_manifest):
                    errors.append(f"Recipe manifest missing: {r_manifest}")

    # 5. Check Official Packs & Versions
    pack_root = os.path.join(target_dir, "packs", "otter-official")
    if not os.path.isdir(pack_root):
        errors.append(f"Missing packs directory: {pack_root}")
    else:
        found_packs = set(os.listdir(pack_root))
        for pack_name, expected_ver in EXPECTED_PACK_VERSIONS.items():
            if pack_name not in found_packs:
                errors.append(f"Missing official pack: {pack_name}")
                continue

            manifest_path = os.path.join(pack_root, pack_name, "otter.pack.json")
            if not os.path.isfile(manifest_path):
                errors.append(f"Missing pack manifest: {manifest_path}")
                continue

            try:
                with open(manifest_path, "r", encoding="utf-8") as f:
                    m = json.load(f)
                actual_ver = m.get("version")
                if actual_ver != expected_ver:
                    errors.append(
                        f"Stale pack version for '{pack_name}': expected {expected_ver}, got {actual_ver}"
                    )
            except Exception as ex:
                errors.append(f"Failed to read pack manifest {manifest_path}: {ex}")

    # 6. Full Recursive SHA-256 Comparison Against Canonical
    canonical_files = {}
    for root, _, files in os.walk(canonical_dir):
        for fname in files:
            full_path = os.path.join(root, fname)
            rel_path = os.path.relpath(full_path, canonical_dir)
            # Skip hidden files or test artifacts if any
            if any(part.startswith(".") for part in rel_path.split(os.sep)):
                continue
            canonical_files[rel_path] = compute_sha256(full_path)

    for rel_path, expected_hash in canonical_files.items():
        target_file = os.path.join(target_dir, rel_path)
        if not os.path.isfile(target_file):
            errors.append(f"Canonical file missing from bundle: {rel_path}")
            continue
        actual_hash = compute_sha256(target_file)
        if actual_hash != expected_hash:
            errors.append(
                f"Source hash mismatch for {rel_path}:\n"
                f"   Canonical SHA-256: {expected_hash}\n"
                f"   Bundled   SHA-256: {actual_hash}"
            )

    return len(errors) == 0, errors


def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    canonical_dir = os.path.join(repo_root, "library")

    if not os.path.isdir(canonical_dir):
        print(f"FATAL: Canonical library directory not found at {canonical_dir}", file=sys.stderr)
        sys.exit(1)

    targets = sys.argv[1:]
    if not targets:
        # Default targets to check if none specified: Debug & Release bin libraries
        debug_lib = os.path.join(repo_root, "src", "Otter.Cli", "bin", "Debug", "net10.0", "library")
        release_lib = os.path.join(repo_root, "src", "Otter.Cli", "bin", "Release", "net10.0", "library")
        if os.path.isdir(debug_lib):
            targets.append(debug_lib)
        if os.path.isdir(release_lib):
            targets.append(release_lib)

    if not targets:
        print("No target library directories specified and no build outputs found to verify.")
        print("Usage: python3 tools/verify-bundle-integrity.py <target_library_dir> [more_dirs...]")
        sys.exit(1)

    print("=" * 70)
    print(" Otter Distribution & Library Bundle Integrity Verification")
    print(f" Canonical Library: {canonical_dir}")
    print("=" * 70)

    all_passed = True
    for target in targets:
        ok, errs = verify_target_library(canonical_dir, target)
        if ok:
            print(f"  [PASS] {target}: All 38 snippets, 5 packs, 4 blueprints, 4 recipes, schemas, and SHA-256 hashes match perfectly.")
        else:
            all_passed = False
            print(f"  [FAIL] {target}: {len(errs)} integrity error(s) detected:")
            for e in errs:
                print(f"    - {e}")

    print("=" * 70)
    if all_passed:
        print(" BUNDLE INTEGRITY CHECK: ALL TARGETS CLEAN (100% PASS)")
        print("=" * 70)
        sys.exit(0)
    else:
        print(" BUNDLE INTEGRITY CHECK FAILED: Stale or corrupted assets detected.")
        print("=" * 70)
        sys.exit(1)


if __name__ == "__main__":
    main()
