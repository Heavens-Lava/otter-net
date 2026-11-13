#!/usr/bin/env python3
import os
import subprocess
import json

app_dir = os.path.abspath("examples/contact-manager-app")
otter_dll = os.path.abspath("src/Otter.Cli/bin/Debug/net10.0/otter.dll")
db_file = os.path.join(app_dir, "contacts_multiprocess.json")

if os.path.exists(db_file):
    os.remove(db_file)

# Script 1: Run process 1 to write contacts
proc1_code = f"""
use "../.otter/packages/otter-official/file-helpers/1.0.0/src/main.ot"
use "../.otter/packages/otter-official/text-utils/1.0.0/src/main.ot"
use "../.otter/packages/otter-official/list-utils/1.0.0/src/main.ot"

a Contact has
    id
    name
    email
    phone
    tag
.

a ContactStore has
    contacts
    count
    lastId
.

to createContactStore
    s is a ContactStore
    e are empty
    contacts of s is e
    count of s is 0
    lastId of s is 0
    return s
.

to createContact cid cname cemail cphone ctag
    c is a Contact
    id of c is cid
    name of c is cname
    email of c is cemail
    phone of c is cphone
    tag of c is ctag
    return c
.

to addContact store name email phone tag
    curLast is lastId of store
    newId is curLast plus 1
    lastId of store is newId
    c is createContact newId name email phone tag
    cList is contacts of store
    add c to cList
    count of store is count of store plus 1
    return c
.

to buildRecord it
    record has
        id is id of it
        name is name of it
        email is email of it
        phone is phone of it
        tag is tag of it
    .
    return record
.

store is createContactStore
addContact store "Diana Prince" "diana@themyscira.gov" "555-7777" "Security"
addContact store "Clark Kent" "clark@dailyplanet.com" "555-8888" "Journalist"

rawList are empty
for each c in contacts of store
    r is buildRecord c
    add r to rawList
.
payload has
    count is count of store
    lastId is lastId of store
    contacts is rawList
.

ok is writeJsonObject "{db_file}" payload
if ok is not true
    fail with "Process 1 write failed"
.
say "Process 1: Saved contacts successfully"
"""

proc1_file = os.path.join(app_dir, "src", "proc1_save.ot")
with open(proc1_file, "w") as f:
    f.write(proc1_code)

res1 = subprocess.run(["dotnet", otter_dll, proc1_file], capture_output=True, text=True, cwd=app_dir)
print("Process 1 Output:\n" + res1.stdout)
if res1.returncode != 0:
    print("Process 1 failed:", res1.stderr)
    exit(1)

if not os.path.exists(db_file):
    print("Database file was not written!")
    exit(1)

# Script 2: Separate process starts and verifies persistence
proc2_code = f"""
use "../.otter/packages/otter-official/file-helpers/1.0.0/src/main.ot"
use "../.otter/packages/otter-official/text-utils/1.0.0/src/main.ot"
use "../.otter/packages/otter-official/list-utils/1.0.0/src/main.ot"

emptyList are empty
fallback has
    count is 0
    lastId is 0
    contacts is emptyList
.

loaded is readJsonObjectSafe "{db_file}" fallback
if count of loaded is not 2
    fail with "Process 2: Expected 2 loaded contacts"
.

rawContacts is contacts of loaded
c1 is first of rawContacts
if name of c1 is not "Diana Prince"
    fail with "Process 2: Expected Diana Prince as first contact"
.
if tag of c1 is not "Security"
    fail with "Process 2: Expected Security tag"
.

c2 is last of rawContacts
if name of c2 is not "Clark Kent"
    fail with "Process 2: Expected Clark Kent as second contact"
.

say "Process 2: Verified persistence across separate processes successfully!"
"""

proc2_file = os.path.join(app_dir, "src", "proc2_load.ot")
with open(proc2_file, "w") as f:
    f.write(proc2_code)

res2 = subprocess.run(["dotnet", otter_dll, proc2_file], capture_output=True, text=True, cwd=app_dir)
print("Process 2 Output:\n" + res2.stdout)
if res2.returncode != 0:
    print("Process 2 failed:", res2.stderr)
    exit(1)

# Clean up temporary test files
os.remove(proc1_file)
os.remove(proc2_file)
if os.path.exists(db_file):
    os.remove(db_file)

print("=== Contact Manager Multi-Process Persistence Verified ===")
