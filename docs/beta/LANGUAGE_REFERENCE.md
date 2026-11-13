# Otter 1.0.1 Language Reference

This reference describes the official grammar, lexical structure, data types, and execution model of Otter 1.0.1.

---

## 1. General Principles

- **Human-Readable:** Otter uses plain English keywords (`is`, `plus`, `minus`, `times`, `for each`, `say`) in place of symbolic operators.
- **Indentation:** Code blocks must be indented using 4 spaces or tabs.
- **Block Terminators:** Every block (`if`, `for`, `has`, `to`, `try`) ends with a single period (`.`) on its own line.
- **Zero Ambiguity:** There is no automatic semicolon insertion or hidden operator precedence quirks.

---

## 2. Variables and Assignment

Variables are dynamically typed and assigned with `is`:
```otter
x is 42
name is "Otter"
active is true
emptyList are empty
```

Missing or unassigned values evaluate to `gone`:
```otter
if value is gone
    say "Value is missing"
.
```

---

## 3. Data Types

| Type | Description | Example |
|------|-------------|---------|
| `Number` | 64-bit IEEE double precision float / integer | `100`, `3.14159`, `-42` |
| `Text` | UTF-8 encoded string literal | `"Hello, Otter!"` |
| `Boolean` | True or false boolean flags | `true`, `false` |
| `List` | Ordered dynamic collection of elements | `myList are empty` |
| `Thing / Object` | Key-value associative mapping | `tx has id is 1, amount is 50 .` |
| `gone` | Represents the absence of a value | `gone` |

---

## 4. Arithmetic and Comparisons

### Arithmetic
```otter
total is a plus b
diff is a minus b
product is a times b
quotient is a divided by b
remainder is a modulo b
```

### Comparisons
```otter
if a is b
if a is not b
if a is greater than b
if a is less than b
if a is at least b
if a is at most b
if text contains "substring"
```

---

## 5. Control Flow

### Conditionals (`if` / `otherwise`)
```otter
if score is at least 90
    say "Grade: A"
otherwise
    if score is at least 80
        say "Grade: B"
    otherwise
        say "Grade: C"
    .
.
```

### Counting Loops (`count from X to Y`)
```otter
count from 1 to 5 as i
    say "Step:" i
.
```

### Collection Loops (`for each item in list`)
```otter
fruits are
    "Apple"
    "Banana"
    "Cherry"
.

for each fruit in fruits
    say "Fruit:" fruit
.
```

### Error Handling (`try` / `otherwise`)
```otter
try
    dangerousOperation
otherwise
    say "An error occurred, but execution continued safely."
.
```

---

## 6. Functions

Functions are defined with `to` and parameters separated by `and`:
```otter
to calculateArea width and height
    area is width times height
    return area
.

# Invocation
myArea is calculateArea 10 and 20
say "Area:" myArea
```

---

## 7. Objects and Types

Custom object structures are defined using `has`:
```otter
a Player has
    name
    health
    level
.

to createPlayer playerName
    p is a Player
    name of p is playerName
    health of p is 100
    level of p is 1
    return p
.

hero is createPlayer "Sir Arthur"
say "Hero Name:" name of hero
say "Hero HP:" health of hero
```

Dynamic objects (Things) can be created inline:
```otter
item has
    title is "Shield"
    defense is 15
.

say title of item "provides" defense of item "defense."
```

---

## 8. File I/O & JSON

### File Operations
```otter
# Read text
read "data.txt" into content

# Write text
write "Log entry\n" to "log.txt"

# Check existence
if file "config.json" exists
    say "Configuration exists."
.

# Delete file
delete file "temp.txt"
```

### JSON Serialization
```otter
# Serialize object to JSON string
convert myObject to json into jsonText

# Deserialize JSON string into object
loaded is a thing
convert jsonText from json into loaded
```
