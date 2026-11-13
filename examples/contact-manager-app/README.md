# Persistent Contact Manager Application

A production-grade Otter application demonstrating real-world pack consumption, contact lifecycle management, text search, and state recovery across program sessions.

## Packs Consumed
- `otter-official/file-helpers` v1.0.0 (`writeJsonObject`, `readJsonObjectSafe`, `deleteFileSafe`)
- `otter-official/text-utils` v1.0.0 (`toLower`, `containsText`)
- `otter-official/list-utils` v1.0.0 (`length of list`, list manipulation)

## Capabilities
- Add new contacts with auto-incrementing IDs
- Search contacts by name, email, or department tag (case-insensitive substring search)
- Edit existing contacts (email, phone, tag updates)
- Delete contacts by ID with store compaction
- Persist contact store to JSON on disk
- Restore and recover contact state across application restarts

## Running the Application
```bash
otter run examples/contact-manager-app
```

## Running Persistence Tests
```bash
python3 examples/contact-manager-app/tests/test_persistence.py
```
Demonstrates that Process 1 writes contacts to disk, terminates, and Process 2 starts in a clean OS process and successfully recovers all contacts and fields.
