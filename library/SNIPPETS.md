# Otter Ultimate Reusable Snippet Catalog

The official collection of curated, idiomatic, ready-to-insert snippets for the Otter programming language, built in accordance with [OTTER_REUSABLE_LIBRARY_RFC.md](file:///Users/jeffreymacy/Projects/otter-net/OTTER_REUSABLE_LIBRARY_RFC.md).

Every snippet strictly adheres to the canonical Otter 1.0.1 grammar specification (`SPEC-DECISIONS.md`), using standard `.` block termination, `field of target` property access, and zero non-standard keywords or syntax extensions.

---

## Quick Navigation

- [Control Flow](#1-control-flow)
- [Text & Strings](#2-text-strings)
- [Math & Numbers](#3-math-numbers)
- [Collections & Lists](#4-collections-lists)
- [Files & I/O](#5-files-i/o)
- [Data & Records](#6-data-records)
- [Game & Simulation](#7-game-simulation)
- [Web & Network](#8-web-network)
- [Dates & Times](#9-dates-times)
- [Collections](#10-collections)
- [Files & Storage](#11-files-storage)
- [Database](#12-database)
- [User Interface](#13-user-interface)
- [Game Systems](#14-game-systems)
- [Validation & Errors](#15-validation-errors)

---

## 1. Control Flow

### `cf-for-each` — For Each Loop
Iterate over elements in a list using the idiomatic Otter loop syntax.
- **Tags:** `loop`, `for`, `for each`, `each`, `iterate`, `list`, `array`, `collection`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
for each ${item} in ${items}
    say ${item}
.
```

**Runnable Example:**
```otter
items are
    "Ruby"
    "Otter"
    "C#"
.

itemCount is 0
for each item in items
    say "Language:" item
    increase itemCount by 1
.

if itemCount is not 3
    fail with "for-each count mismatch"
.

```

---

### `cf-count-range` — Count Range Counter Loop
Count sequentially through an inclusive numeric range from start to finish.
- **Tags:** `count`, `range`, `from to`, `counter`, `step`, `number loop`, `repeat`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
count from ${start} to ${finish} as ${number}
    say ${number}
.
```

**Runnable Example:**
```otter
sum is 0
count from 1 to 5 as n
    say "Counting:" n
    sum is sum plus n
.

if sum is not 15
    fail with "count-range sum mismatch: expected 15"
.

```

---

### `cf-while-loop` — While Loop
Execute statements repeatedly while a condition evaluates to true.
- **Tags:** `while`, `loop`, `condition`, `repeat until`, `iteration`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
while ${condition}
    # statements
.
```

**Runnable Example:**
```otter
counter is 3
while counter is greater than 0
    say "Countdown:" counter
    decrease counter
.

if counter is not 0
    fail with "while-loop termination mismatch: expected 0"
.

```

---

### `cf-if-otherwise` — Multi-Branch Conditional (If / Otherwise)
Evaluate multiple conditions sequentially with fallback branch handling.
- **Tags:** `if`, `otherwise`, `else`, `else if`, `condition`, `branch`, `switch`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
if ${condition}
    say "branch 1"
otherwise if ${otherCondition}
    say "branch 2"
otherwise
    say "fallback"
.
```

**Runnable Example:**
```otter
score is 85
grade is "unknown"

if score is at least 90
    grade is "A"
    say "Grade: A"
otherwise if score is at least 80
    grade is "B"
    say "Grade: B"
otherwise
    grade is "C"
    say "Grade: C"
.

if grade is not "B"
    fail with "if-otherwise evaluation mismatch: expected B"
.

```

---

### `cf-try-otherwise` — Safe Error Handling (Try / Otherwise)
Safely capture and handle runtime errors without terminating execution.
- **Tags:** `try`, `catch`, `otherwise`, `error`, `reason`, `exception`, `safe`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
try
    ${riskyOperation}
otherwise into ${reason}
    say "Error encountered:" ${reason}
.
```

**Runnable Example:**
```otter
caught is false
reasonMsg is ""

try
    badMath is 10 divided by 0
    say badMath
otherwise into reason
    caught is true
    reasonMsg is reason
    say "Handled error safely:" reason
.

if caught is not true
    fail with "try-otherwise failed: exception was not caught"
.

```

---

## 2. Text & Strings

### `text-slugify` — Convert Text to URL Slug
Transform titles and text into clean, lowercase, hyphenated URL slugs.
- **Tags:** `slug`, `slugify`, `url`, `kebab-case`, `text`, `lowercase`, `replace`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to slugify text
    clean is lowercase of text
    replace " " with "-" in clean
    replace "_" with "-" in clean
    return clean
.
```

**Runnable Example:**
```otter
to slugify text
    clean is lowercase of text
    replace " " with "-" in clean
    replace "_" with "-" in clean
    return clean
.

slug is slugify "Otter Language Release 2026"
say "Slug:" slug

if slug is not "otter-language-release-2026"
    fail with "slugify mismatch: expected otter-language-release-2026"
.

```

---

### `text-pad-left` — Pad Number / Text Left
Prepend repeating pad characters until text reaches desired target length.
- **Tags:** `pad`, `padLeft`, `align`, `format`, `number format`, `zero pad`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to padLeft text targetLen padChar
    curLen is length of text
    if curLen is at least targetLen
        return text
    .
    needed is targetLen minus curLen
    padding is ""
    count from 1 to needed as idx
        padding is padding plus padChar
    .
    return padding plus text
.
```

**Runnable Example:**
```otter
to padLeft text targetLen padChar
    curLen is length of text
    if curLen is at least targetLen
        return text
    .
    needed is targetLen minus curLen
    padding is ""
    count from 1 to needed as idx
        padding is padding plus padChar
    .
    return padding plus text
.

res is padLeft "item" 7 "x"
say "Padded:" res

if res is not "xxxitem"
    fail with "padLeft mismatch: expected xxxitem"
.
if length of res is not 7
    fail with "padLeft length mismatch: expected 7"
.

```

---

### `text-split-join` — Split Text and Rejoin with Delimiter
Deconstruct text by delimiter and reassemble parts with a new separator.
- **Tags:** `split`, `join`, `csv`, `delimiter`, `separator`, `words`, `string`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
split ${sourceText} by "${delimiter}" into ${parts}
join ${parts} with "${newSeparator}" into ${result}
```

**Runnable Example:**
```otter
sentence is "alpha,beta,gamma,delta"
split sentence by "," into items
join items with " -> " into pipeline
say pipeline

if length of items is not 4
    fail with "split items count mismatch: expected 4"
.
if pipeline is not "alpha -> beta -> gamma -> delta"
    fail with "split-join pipeline mismatch"
.

```

---

### `text-replace-all` — Replace Text Substrings
Replace matching substrings with replacement text inside variable.
- **Tags:** `replace`, `substitute`, `string`, `modify`, `find and replace`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
replace "${findText}" with "${replacementText}" in ${targetVariable}
```

**Runnable Example:**
```otter
greeting is "Hello world! Welcome to world."
replace "world" with "Otter" in greeting
say greeting

if greeting is not "Hello Otter! Welcome to Otter."
    fail with "text replacement mismatch"
.

```

---

### `conv-bytes-hex` — Encode and Decode Hex Strings
Transform between hexadecimal text strings and raw binary byte sequences.
- **Tags:** `hex`, `bytes`, `encode`, `decode`, `binary`, `conversion`, `string`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
rawBytes is bytes from hex ${hexInput}
decodedText is text from bytes rawBytes
encodedHex is hex from bytes rawBytes
```

**Runnable Example:**
```otter
hexInput is "4F74746572"
rawBytes is bytes from hex hexInput
decodedText is text from bytes rawBytes
say "Decoded text:" decodedText

if decodedText is not "Otter"
    fail with "bytes from hex decode mismatch: expected Otter"
.

encodedHex is hex from bytes rawBytes
say "Encoded back:" encodedHex

if uppercase of encodedHex is not "4F74746572"
    fail with "hex from bytes encode mismatch"
.

```

---

## 3. Math & Numbers

### `math-clamp` — Clamp Value Within Range
Constrain a numeric value between minimum and maximum boundaries.
- **Tags:** `clamp`, `math`, `limit`, `bound`, `range`, `min max`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to clamp val minLimit maxLimit
    if val is less than minLimit
        return minLimit
    .
    if val is greater than maxLimit
        return maxLimit
    .
    return val
.
```

**Runnable Example:**
```otter
to clamp val minLimit maxLimit
    if val is less than minLimit
        return minLimit
    .
    if val is greater than maxLimit
        return maxLimit
    .
    return val
.

c1 is clamp 150 0 100
negVal is 0 minus 10
c2 is clamp negVal 0 100
c3 is clamp 50 0 100

say "Clamped:" c1 c2 c3

if c1 is not 100
    fail with "clamp upper bound mismatch: expected 100"
.
if c2 is not 0
    fail with "clamp lower bound mismatch: expected 0"
.
if c3 is not 50
    fail with "clamp middle value mismatch: expected 50"
.

```

---

### `math-lerp` — Linear Interpolation (Lerp)
Calculate linear interpolation between start and end values by factor t.
- **Tags:** `lerp`, `interpolation`, `animation`, `transition`, `blend`, `smooth`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to lerp startVal endVal t
    diff is endVal minus startVal
    offset is diff times t
    return startVal plus offset
.
```

**Runnable Example:**
```otter
to lerp startVal endVal t
    diff is endVal minus startVal
    offset is diff times t
    return startVal plus offset
.

mid is lerp 10 20 0.5
say "Midpoint:" mid

if mid is not 15
    fail with "lerp midpoint mismatch: expected 15"
.

```

---

### `math-map-range` — Map Range (Scale Values)
Re-map a number from an incoming range into an outgoing target range.
- **Tags:** `map`, `range`, `scale`, `normalize`, `remap`, `math`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to mapRange val inMin inMax outMin outMax
    inSpan is inMax minus inMin
    outSpan is outMax minus outMin
    valDelta is val minus inMin
    fraction is valDelta divided by inSpan
    scaled is fraction times outSpan
    return outMin plus scaled
.
```

**Runnable Example:**
```otter
to mapRange val inMin inMax outMin outMax
    inSpan is inMax minus inMin
    outSpan is outMax minus outMin
    valDelta is val minus inMin
    fraction is valDelta divided by inSpan
    scaled is fraction times outSpan
    return outMin plus scaled
.

res is mapRange 75 0 100 0 1
say "Percentage to fraction:" res

if res is not 0.75
    fail with "mapRange calculation mismatch: expected 0.75"
.

```

---

### `math-calc-average` — Calculate List Average
Compute the arithmetic mean of all numeric elements in a list.
- **Tags:** `average`, `mean`, `statistics`, `sum`, `math`, `numbers`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to calcAverage items
    total is 0
    count is 0
    for each item in items
        total is total plus item
        increase count by 1
    .
    if count is 0
        return 0
    .
    return total divided by count
.
```

**Runnable Example:**
```otter
to calcAverage items
    total is 0
    itemCount is 0
    for each item in items
        total is total plus item
        increase itemCount by 1
    .
    if itemCount is 0
        return 0
    .
    return total divided by itemCount
.

grades are
    85
    90
    95
.
avg is calcAverage grades
say "Class average:" avg

if avg is not 90
    fail with "calcAverage mismatch: expected 90"
.

```

---

### `conv-number-format` — Format Number as Currency
Format a numeric amount into a currency string with 2-decimal precision.
- **Tags:** `format`, `currency`, `number`, `decimal`, `dollar`, `money`, `conversion`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to formatCurrency amount
    parts are
        amount
    .
    join parts with "" into amountStr
    if amountStr contains "."
        split amountStr by "." into decParts
        whole is first of decParts
        cents is last of decParts
        if length of cents is 1
            cents is cents plus "0"
        .
        outParts are
            "$"
            whole
            "."
            cents
        .
        join outParts with "" into result
        return result
    otherwise
        outParts are
            "$"
            amountStr
            ".00"
        .
        join outParts with "" into result
        return result
    .
.
```

**Runnable Example:**
```otter
to formatCurrency amount
    parts are
        amount
    .
    join parts with "" into amountStr
    if amountStr contains "."
        split amountStr by "." into decParts
        whole is first of decParts
        cents is last of decParts
        if length of cents is 1
            cents is cents plus "0"
        .
        outParts are
            "$"
            whole
            "."
            cents
        .
        join outParts with "" into result
        return result
    otherwise
        outParts are
            "$"
            amountStr
            ".00"
        .
        join outParts with "" into result
        return result
    .
.

formatted1 is formatCurrency 42.5
formatted2 is formatCurrency 100

say "Price 1:" formatted1
say "Price 2:" formatted2

if formatted1 is not "$42.50"
    fail with "formatCurrency decimal mismatch: expected $42.50"
.
if formatted2 is not "$100.00"
    fail with "formatCurrency integer mismatch: expected $100.00"
.

```

---

## 4. Collections & Lists

### `list-contains-check` — Check if List Contains Element
Determine if an item exists within a list using the natural contains condition.
- **Tags:** `contains`, `exists`, `search`, `list`, `array`, `membership`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
if ${items} contains ${targetItem}
    say "Item found in list"
.
```

**Runnable Example:**
```otter
allowedRoles are
    "admin"
    "moderator"
.

userRole is "admin"
guestRole is "guest"

hasAdmin is false
if allowedRoles contains userRole
    hasAdmin is true
.

hasGuest is false
if allowedRoles contains guestRole
    hasGuest is true
.

say "Has admin:" hasAdmin
say "Has guest:" hasGuest

if hasAdmin is not true
    fail with "list-contains expected admin to be present"
.
if hasGuest is not false
    fail with "list-contains expected guest to be absent"
.

```

---

### `list-deduplicate` — Deduplicate List Items
Construct a new list retaining only unique occurrences of elements.
- **Tags:** `dedupe`, `distinct`, `unique`, `list`, `array`, `collection`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to dedupeList items
    result are empty
    for each item in items
        if not result contains item
            add item to result
        .
    .
    return result
.
```

**Runnable Example:**
```otter
to dedupeList items
    result are empty
    for each item in items
        if not result contains item
            add item to result
        .
    .
    return result
.

raw are
    "cat"
    "dog"
    "cat"
    "bird"
.
uniquePets is dedupeList raw
say "Unique count:" length of uniquePets

if length of uniquePets is not 3
    fail with "dedupeList length mismatch: expected 3"
.
if not uniquePets contains "dog"
    fail with "dedupeList missing expected item 'dog'"
.

```

---

### `list-filter-items` — Filter List by Condition
Filter elements from a list into a new target list using a condition.
- **Tags:** `filter`, `where`, `select`, `list`, `array`, `query`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
filtered are empty
for each ${item} in ${items}
    if ${condition}
        add ${item} to filtered
    .
.
```

**Runnable Example:**
```otter
numbers are
    12
    5
    28
    3
    45
.
highNumbers are empty
for each n in numbers
    if n is at least 10
        add n to highNumbers
    .
.
say "Found numbers >= 10:" length of highNumbers

if length of highNumbers is not 3
    fail with "filter count mismatch: expected 3 items"
.
if not highNumbers contains 28
    fail with "filtered list missing element 28"
.

```

---

## 5. Files & I/O

### `file-read-safe` — Safe File Read with Fallback
Verify file existence before reading to return fallback content when absent.
- **Tags:** `file`, `read`, `exists`, `fallback`, `safe file`, `io`
- **Capabilities:** files
- **Platforms:** console, desktop

```otter
if file "${filePath}" exists
    read "${filePath}" into ${content}
otherwise
    ${content} is "${fallbackDefault}"
.
```

**Runnable Example:**
```otter
testPath is "snippet_test_read.txt"
write "version-2026.1" to testPath

if file testPath exists
    read testPath into appVersion
otherwise
    appVersion is "fallback-version"
.

say "App Version:" appVersion

if appVersion is not "version-2026.1"
    fail with "file-read-safe existing file read mismatch"
.

delete file testPath

if file "non_existent_file_999.txt" exists
    read "non_existent_file_999.txt" into missingVal
otherwise
    missingVal is "safe-fallback"
.

if missingVal is not "safe-fallback"
    fail with "file-read-safe fallback branch mismatch"
.

```

---

### `file-write-text` — Write Text File
Write or overwrite content to a file path cleanly.
- **Tags:** `file`, `write`, `save`, `output`, `io`, `text`
- **Capabilities:** files
- **Platforms:** console, desktop

```otter
write ${content} to "${filePath}"
```

**Runnable Example:**
```otter
testLogPath is "snippet_session.log"
message is "Session started at 2026-11-10"
write message to testLogPath
say "Session written to disk."

read testLogPath into readBack
if readBack is not message
    fail with "file-write-text content readback mismatch"
.

delete file testLogPath

```

---

### `file-json-storage` — Read & Write JSON Objects to File
Serialize structured Otter objects to JSON on disk and load them back.
- **Tags:** `json`, `save json`, `load json`, `config`, `persistence`, `file`
- **Capabilities:** files
- **Platforms:** console, desktop

```otter
to saveJsonObject filePath obj
    convert obj to json into rawJson
    write rawJson to filePath
    return true
.

to loadJsonObject filePath
    if file filePath exists
        read filePath into rawJson
        loaded is a thing
        convert rawJson from json into loaded
        return loaded
    .
    return gone
.
```

**Runnable Example:**
```otter
to saveJsonObject filePath obj
    convert obj to json into rawJson
    write rawJson to filePath
    return true
.

to loadJsonObject filePath
    if file filePath exists
        read filePath into rawJson
        loaded is a thing
        convert rawJson from json into loaded
        return loaded
    .
    return gone
.

cfg has
    appName is "Otter App"
    port is 3000
.

ok is saveJsonObject "app_test.json" cfg
if ok is not true
    fail with "saveJsonObject failed"
.

readCfg is loadJsonObject "app_test.json"
say "Loaded app:" appName of readCfg

if appName of readCfg is not "Otter App"
    fail with "json storage appName mismatch"
.
if port of readCfg is not 3000
    fail with "json storage port mismatch"
.

delete file "app_test.json"

```

---

## 6. Data & Records

### `data-custom-type` — Define & Instantiate Custom Type
Declare a strongly named domain record type with fields and instantiate it.
- **Tags:** `type`, `struct`, `class`, `record`, `has`, `is a`, `model`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
a ${TypeName} has
    ${field1}
    ${field2}
.

${variable} is a ${TypeName}
${field1} of ${variable} is ${value1}
${field2} of ${variable} is ${value2}
```

**Runnable Example:**
```otter
a Profile has
    username
    email
.

user is a Profile
username of user is "jeffrey"
email of user is "jeffrey@example.com"
say username of user "<" email of user ">"

if username of user is not "jeffrey"
    fail with "profile username mismatch"
.
if email of user is not "jeffrey@example.com"
    fail with "profile email mismatch"
.

```

---

### `data-object-thing` — Dynamic Object Literal (has block)
Construct an ad-hoc key-value data structure with named properties.
- **Tags:** `object`, `thing`, `map`, `dictionary`, `has`, `properties`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
${variable} has
    ${property} is ${value}
.
```

**Runnable Example:**
```otter
serverConfig has
    host is "127.0.0.1"
    port is 8080
    active is true
.
say "Listening on" host of serverConfig ":" port of serverConfig

if host of serverConfig is not "127.0.0.1"
    fail with "serverConfig host mismatch"
.
if port of serverConfig is not 8080
    fail with "serverConfig port mismatch"
.
if active of serverConfig is not true
    fail with "serverConfig active flag mismatch"
.

```

---

### `db-sqlite-query` — SQLite Table Query (Parameterized)
Execute parameterized SQLite queries and iterate over record rows safely.
- **Tags:** `database`, `sqlite`, `sql`, `query`, `parameter`, `rows`, `records`, `data`
- **Capabilities:** database
- **Platforms:** console, desktop

```otter
database has
    provider is "sqlite"
    connection is "${dbPath}"
.
connect database into ${db}
execute ${db} with
    "${sqlCreateTable}"

execute ${db} with
    "${sqlInsert}"
    parameter "${paramName}" is ${paramValue}
into ${insertResult}

query ${db} with
    "${sqlSelect}"
into ${rows}
for each ${row} in ${rows}
    say ${row}
.
```

**Runnable Example:**
```otter
database has
    provider is "sqlite"
    connection is ":memory:"
.
connect database into db
execute db with
    "create table items (id integer primary key autoincrement, name text not null)"
execute db with
    "insert into items (name) values (@name)"
    parameter "name" is "Otter Net"
into result
query db with
    "select id, name from items"
into rows

say "Found rows:" length of rows
if length of rows is not 1
    fail with "sqlite query row count mismatch: expected 1"
.

r is first of rows
if name of r is not "Otter Net"
    fail with "sqlite query result mismatch: expected 'Otter Net'"
.

```

---

## 7. Game & Simulation

### `game-actor-damage` — Game Actor Combat & Damage
Declare RPG entity state and compute clamped damage with death detection.
- **Tags:** `game`, `rpg`, `actor`, `enemy`, `damage`, `health`, `combat`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
a Entity has
    name
    health
    isAlive
.

to takeDamage entity amount
    cur is health of entity
    newHp is cur minus amount
    if newHp is at most 0
        health of entity is 0
        isAlive of entity is false
        return 0
    .
    health of entity is newHp
    return newHp
.
```

**Runnable Example:**
```otter
a Entity has
    name
    health
    isAlive
.

to takeDamage entity amount
    cur is health of entity
    newHp is cur minus amount
    if newHp is at most 0
        health of entity is 0
        isAlive of entity is false
        return 0
    .
    health of entity is newHp
    return newHp
.

boss is a Entity
name of boss is "Ancient Dragon"
health of boss is 100
isAlive of boss is true

remHp is takeDamage boss 40
say "Remaining HP:" health of boss

if remHp is not 60
    fail with "damage calculation mismatch: expected 60"
.
if health of boss is not 60
    fail with "boss health state mismatch: expected 60"
.

fatalHp is takeDamage boss 80
if fatalHp is not 0
    fail with "overkill health mismatch: expected 0"
.
if isAlive of boss is not false
    fail with "boss alive state mismatch after lethal damage"
.

```

---

## 8. Web & Network

### `web-http-json-response` — Web Server JSON Route
Respond to incoming HTTP GET request with a JSON structured payload.
- **Tags:** `web`, `server`, `http`, `api`, `route`, `json`, `rest`
- **Capabilities:** network
- **Platforms:** console, desktop

```otter
site is a web server
    port is ${portNumber}
    host is "127.0.0.1"
.

when site receives GET at "${path}"
    payload has
        status is "ok"
    .
    respond with payload as json
.
```

**Runnable Example:**
```otter
site is a web server
    port is 18090
    host is "127.0.0.1"
.

when site receives GET at "/health"
    payload has
        status is "healthy"
        uptime is "active"
    .
    respond with payload as json
.

```

---

### `http-get-request` — HTTP GET Request with Client Lifecycle
Issue an asynchronous HTTP GET request and await completion response.
- **Tags:** `http`, `get`, `request`, `api`, `network`, `client`, `fetch`
- **Capabilities:** network
- **Platforms:** console, desktop

```otter
start get from ${url} and call it ${req}
tries is 0
while ${req} is pending and tries is less than 50
    wait 20 milliseconds
    tries is tries plus 1
.
if ${req} is completed
    say "Response:" response of ${req}
.
```

**Runnable Example:**
```otter
listen for tcp on port 0 and call it srv
port is local port of srv
baseParts are
    "http://127.0.0.1:"
    port
.
join baseParts with "" into baseUrl
on connection to srv
    c is incoming connection
    on data from c
        send bytes from text "HTTP/1.1 200 OK\nContent-Type: text/plain\nContent-Length: 12\nConnection: close\n\nHello Otter!" through c
        close tcp c
    .
.

url is baseUrl plus "/api/ping"
start get from url and call it req
tries is 0
while req is pending and tries is less than 50
    wait 30 milliseconds
    tries is tries plus 1
.

if req is not completed
    fail with "http request did not complete"
.

say "HTTP Response:" response of req

if not response of req contains "Hello Otter!"
    fail with "http response body mismatch: expected Hello Otter!"
.

stop tcp srv

```

---

## 9. Dates & Times

### `date-diff-days` — Calculate Days Between Two Dates
Compute the calendar difference in days between two date instances.
- **Tags:** `date`, `time`, `days between`, `duration`, `calendar`, `difference`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
startDay is date from "${startDate}"
endDay is date from "${endDate}"
diffDays is days between startDay and endDay
```

**Runnable Example:**
```otter
startDay is date from "2026-01-01"
endDay is date from "2026-01-15"
diffDays is days between startDay and endDay
say "Days between dates:" diffDays

if diffDays is not 14
    fail with "days between calculation mismatch: expected 14"
.

```

---

### `date-add-days` — Add Days to Calculate Due Date
Add or advance calendar days on a date instance to calculate deadlines.
- **Tags:** `date`, `add days`, `deadline`, `due date`, `calendar`, `future date`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
targetDate is date from "${initialDate}"
add ${numDays} days to targetDate
```

**Runnable Example:**
```otter
invoiceDate is date from "2026-03-01"
add 30 days to invoiceDate
say "Invoice due date:" invoiceDate

expectedDate is date from "2026-03-31"
if invoiceDate is not expectedDate
    fail with "date addition mismatch: expected 2026-03-31"
.

```

---

## 10. Collections

### `list-sort-reverse` — In-Place Sort and Reverse List
Sort list elements in ascending order and reverse for descending order.
- **Tags:** `sort`, `reverse`, `order`, `descending`, `ascending`, `list`, `collection`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
sort ${targetList}
reverse ${targetList}
```

**Runnable Example:**
```otter
scores are
    88
    42
    95
    17
.
sort scores
say "Sorted ascending:" scores

if first of scores is not 17
    fail with "sort ascending first element mismatch: expected 17"
.
if last of scores is not 95
    fail with "sort ascending last element mismatch: expected 95"
.

reverse scores
say "Sorted descending:" scores

if first of scores is not 95
    fail with "reverse first element mismatch: expected 95"
.
if last of scores is not 17
    fail with "reverse last element mismatch: expected 17"
.

```

---

## 11. Files & Storage

### `file-csv-parse` — Parse CSV Text Into Structured Rows
Convert delimiter-separated text data into an array of typed property records.
- **Tags:** `csv`, `table`, `parse`, `data`, `convert`, `records`, `import`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
convert ${csvText} from csv into ${rows}
for each ${row} in ${rows}
    say ${row}
.
```

**Runnable Example:**
```otter
csvContent is "name,department,level\nAlice,Engineering,4\nBob,Design,3"
convert csvContent from csv into staff
say "Staff count:" length of staff

if length of staff is not 2
    fail with "csv parse staff count mismatch: expected 2"
.

firstMember is first of staff
if name of firstMember is not "Alice"
    fail with "csv parse first member name mismatch: expected Alice"
.
if department of firstMember is not "Engineering"
    fail with "csv parse first member department mismatch: expected Engineering"
.

```

---

### `file-append-log` — Safe Append Line to Log File
Atomically append a text log entry to a file with fallback creation.
- **Tags:** `file`, `append`, `log`, `write`, `storage`, `audit`
- **Capabilities:** files
- **Platforms:** console, desktop

```otter
to appendLogEntry path msg
    line is msg plus "\n"
    try
        read path into existing
        updated is existing plus line
        write updated to path
    otherwise into reason
        write line to path
    .
.
```

**Runnable Example:**
```otter
logFilePath is "snippet_audit.log"
to appendLogEntry path msg
    line is msg plus "\n"
    try
        read path into existing
        updated is existing plus line
        write updated to path
    otherwise into reason
        write line to path
    .
.

appendLogEntry logFilePath "Server starting"
appendLogEntry logFilePath "Ready to receive"
read logFilePath into auditContents
say "Audit log:" auditContents

if not auditContents contains "Server starting"
    fail with "append log missing first entry"
.
if not auditContents contains "Ready to receive"
    fail with "append log missing second entry"
.

delete file logFilePath

```

---

## 12. Database

### `db-sqlite-transaction` — SQLite Atomic Transaction (Commit / Rollback)
Execute multiple database mutations inside an atomic transaction block.
- **Tags:** `database`, `sqlite`, `transaction`, `commit`, `rollback`, `sql`, `acid`
- **Capabilities:** database
- **Platforms:** console, desktop

```otter
begin transaction on ${db} into ${tx}
execute ${tx} with
    "${sqlMutation}"
commit ${tx}
```

**Runnable Example:**
```otter
database has
    provider is "sqlite"
    connection is ":memory:"
.
connect database into memDb
execute memDb with
    "create table balances (account text primary key, balance real)"
execute memDb with
    "insert into balances (account, balance) values ('ACC-1', 500)"

begin transaction on memDb into tx
execute tx with
    "update balances set balance = balance - 50 where account = 'ACC-1'"
commit tx

query memDb with
    "select balance from balances where account = 'ACC-1'"
into rows
row is first of rows
say "Committed balance:" balance of row

if balance of row is not 450
    fail with "transaction balance mismatch: expected 450"
.

```

---

## 13. User Interface

### `ui-page-layout` — Declarative UI Page Layout
Define a hierarchical declarative UI page structure with headings, cards, and buttons.
- **Tags:** `ui`, `page`, `card`, `button`, `heading`, `desktop`, `web`, `layout`
- **Capabilities:** None (Pure language)
- **Platforms:** desktop, web

```otter
page "${pageTitle}"
    heading "${headingText}"
    card "${cardTitle}"
        text "${bodyText}"
        button "${buttonLabel}"
            click
                say "Clicked ${buttonLabel}"
    primary button "Submit"
```

**Runnable Example:**
```otter
layoutDefined is false

page "User Management"
    heading "All Users"
    card "Directory"
        text "Showing 25 active members"
        button "Export CSV"
            click
                say "Exporting CSV..."
    primary button "Add User"

layoutDefined is true
say "Page layout defined"

if layoutDefined is not true
    fail with "page layout failed to define"
.

```

---

## 14. Game Systems

### `game-cooldown-system` — Ability Cooldown System
Track ability cooldown durations and check ready status against elapsed timestamps.
- **Tags:** `game`, `cooldown`, `ability`, `skill`, `combat`, `timer`, `rpg`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
a AbilityCooldown has
    abilityName
    cooldownSeconds
    lastUsedAt
.

to createAbility name cd
    ability is a AbilityCooldown
    abilityName of ability is name
    cooldownSeconds of ability is cd
    lastUsedAt of ability is 0 minus 9999
    return ability
.

to isAbilityReady ability nowSeconds
    last is lastUsedAt of ability
    duration is cooldownSeconds of ability
    elapsed is nowSeconds minus last
    if elapsed is at least duration
        return true
    otherwise
        return false
    .
.

to useAbility ability nowSeconds
    ready is isAbilityReady ability nowSeconds
    if ready is true
        lastUsedAt of ability is nowSeconds
        return true
    otherwise
        return false
    .
.
```

**Runnable Example:**
```otter
a AbilityCooldown has
    abilityName
    cooldownSeconds
    lastUsedAt
.

to createAbility name cd
    ability is a AbilityCooldown
    abilityName of ability is name
    cooldownSeconds of ability is cd
    lastUsedAt of ability is 0 minus 9999
    return ability
.

to isAbilityReady ability nowSeconds
    last is lastUsedAt of ability
    duration is cooldownSeconds of ability
    elapsed is nowSeconds minus last
    if elapsed is at least duration
        return true
    otherwise
        return false
    .
.

to useAbility ability nowSeconds
    ready is isAbilityReady ability nowSeconds
    if ready is true
        lastUsedAt of ability is nowSeconds
        return true
    otherwise
        return false
    .
.

spinAttack is createAbility "Whirlwind" 4
readyInitial is isAbilityReady spinAttack 0
if readyInitial is not true
    fail with "ability expected ready initially"
.

usedOk is useAbility spinAttack 0
if usedOk is not true
    fail with "ability use expected to succeed at t=0"
.

readyAt2 is isAbilityReady spinAttack 2
if readyAt2 is not false
    fail with "ability expected on cooldown at t=2"
.

readyAt5 is isAbilityReady spinAttack 5
if readyAt5 is not true
    fail with "ability expected ready at t=5 after 4s cooldown"
.

say "Cooldown checks passed successfully."

```

---

### `game-inventory-capacity` — Inventory Bag with Capacity Limits
Manage player inventory slots with strict maximum capacity checks.
- **Tags:** `game`, `inventory`, `bag`, `items`, `capacity`, `slots`, `rpg`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
a InventoryBag has
    maxSlots
    currentCount
    items
.

to createInventoryBag slots
    bag is a InventoryBag
    maxSlots of bag is slots
    currentCount of bag is 0
    bagItems are empty
    items of bag is bagItems
    return bag
.

to addItemToBag bag item
    cur is currentCount of bag
    slots is maxSlots of bag
    if cur is at least slots
        return false
    .
    currentCount of bag is cur plus 1
    itemList is items of bag
    add item to itemList
    return true
.
```

**Runnable Example:**
```otter
a InventoryBag has
    maxSlots
    currentCount
    items
.

to createInventoryBag slots
    bag is a InventoryBag
    maxSlots of bag is slots
    currentCount of bag is 0
    bagItems are empty
    items of bag is bagItems
    return bag
.

to addItemToBag bag item
    cur is currentCount of bag
    slots is maxSlots of bag
    if cur is at least slots
        return false
    .
    currentCount of bag is cur plus 1
    itemList is items of bag
    add item to itemList
    return true
.

playerBag is createInventoryBag 2
added1 is addItemToBag playerBag "Wooden Bow"
added2 is addItemToBag playerBag "Quiver"
added3 is addItemToBag playerBag "Iron Shield"

if added1 is not true
    fail with "addItemToBag failed to add item 1"
.
if added2 is not true
    fail with "addItemToBag failed to add item 2"
.
if added3 is not false
    fail with "addItemToBag permitted adding item exceeding capacity"
.
if length of items of playerBag is not 2
    fail with "inventory bag item count mismatch: expected 2"
.

say "Inventory capacity checks passed."

```

---

## 15. Validation & Errors

### `val-email-check` — Structural Email Format Validation
Validate email address structure by checking username, '@' symbol, and domain suffix.
- **Tags:** `validation`, `email`, `check`, `string`, `verify`, `form`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to validateEmail email
    if email contains "@"
        split email by "@" into parts
        if length of parts is 2
            userPart is first of parts
            domainPart is last of parts
            if length of userPart is greater than 0
                if domainPart contains "."
                    return true
                .
            .
        .
    .
    return false
.
```

**Runnable Example:**
```otter
to validateEmail email
    if email contains "@"
        split email by "@" into parts
        if length of parts is 2
            userPart is first of parts
            domainPart is last of parts
            if length of userPart is greater than 0
                if domainPart contains "."
                    return true
                .
            .
        .
    .
    return false
.

okEmail is validateEmail "dev@otter.org"
badEmail is validateEmail "invalid-email"

say "Check dev@otter.org:" okEmail
say "Check invalid-email:" badEmail

if okEmail is not true
    fail with "validateEmail rejected valid email dev@otter.org"
.
if badEmail is not false
    fail with "validateEmail accepted invalid email invalid-email"
.

```

---

### `val-numeric-range` — Numeric Range Boundary Assertion
Assert that a numeric parameter falls within inclusive lower and upper bounds.
- **Tags:** `validation`, `range`, `boundary`, `assert`, `min`, `max`, `error`, `check`
- **Capabilities:** None (Pure language)
- **Platforms:** console, desktop, web

```otter
to validateRange value minVal maxVal fieldName
    if value is less than minVal
        say "Validation error:" fieldName "is below minimum" minVal
        return false
    .
    if value is greater than maxVal
        say "Validation error:" fieldName "exceeds maximum" maxVal
        return false
    .
    return true
.
```

**Runnable Example:**
```otter
to validateRange value minVal maxVal fieldName
    if value is less than minVal
        say "Validation error:" fieldName "is below minimum" minVal
        return false
    .
    if value is greater than maxVal
        say "Validation error:" fieldName "exceeds maximum" maxVal
        return false
    .
    return true
.

validAge is validateRange 28 18 65 "age"
overAge is validateRange 90 18 65 "age"

if validAge is not true
    fail with "validateRange rejected valid age 28"
.
if overAge is not false
    fail with "validateRange accepted out-of-bounds age 90"
.

say "Numeric range checks passed."

```

---
