# Personal Task Manager Application (Recipe)

**ID:** `otter-official/task-tracker`  
**Version:** `1.0.0`  
**Target:** `console`  
**Required Otter Version:** `>=1.0.1`  
**Capabilities:** `files`

## Overview
A complete console application demonstrating structured state management, file-backed task storage, and padded tabular formatting using Otter 1.0.1 standard syntax.

## Composed Components
- **Packs:**
  - `otter-official/file-helpers` (v1.0.0): JSON object serialization and fallback loading.
  - `otter-official/text-utils` (v1.0.0): Column width padding and formatting.
- **Blueprints:**
  - `otter-official/file-settings` (v1.0.0): Application configuration loading.

## Generated Files
- `src/tasks.ot`: Main application entry point.

## Running Tests
```bash
otter test library/recipes/otter-official/task-tracker/tests/test.ot
```
