# Getting Started with Otter

This 10-minute guide takes you from zero to running your first Otter program, installing a reusable pack, and running automated tests.

---

## 1. Creating Your First Otter Program

Create a file named `hello.ot`:
```otter
# hello.ot - My First Otter Program

say "Hello, Otter World!"

name is "Explorer"
say "Welcome," name "!"

count from 1 to 5 as i
    say "Counting:" i
.
```

Run it directly using the installed `otter` CLI:
```bash
otter run hello.ot
```
Output:
```text
Hello, Otter World!
Welcome, Explorer !
Counting: 1
Counting: 2
Counting: 3
Counting: 4
Counting: 5
```

---

## 2. Initializing a New Project

Otter projects use a standard `project.json` file to manage configuration, entry points, and dependencies.

Initialize a new project:
```bash
mkdir my-app && cd my-app
otter new project
```
Or create `project.json` manually:
```json
{
  "name": "my-app",
  "version": "1.0.0",
  "entry": "src/main.ot",
  "dependencies": {}
}
```

Create `src/main.ot`:
```otter
# src/main.ot

to greet person
    return "Greetings, " plus person plus "!"
.

msg is greet "Ada"
say msg
```

Validate and run the project:
```bash
otter check
otter run
```

---

## 3. Reusing Code with Otter Packs

Otter includes a built-in, offline-first package manager for source packs.

Search available official packs:
```bash
otter pack list
```

Install the `math-utils` pack:
```bash
otter pack install otter-official/math-utils
```
This downloads the verified pack into `.otter/packages/` and pins its cryptographic hash in `otter.lock`.

Use the pack in your code (`src/main.ot`):
```otter
use "../.otter/packages/otter-official/math-utils/1.0.1/src/main.ot"

# Use clamp from math-utils
clampedValue is clamp 120 0 100
say "Clamped value:" clampedValue
```

Verify your project dependencies:
```bash
otter pack verify
```

---

## 4. Writing and Running Tests

Otter provides a built-in test runner that discovers `.ot` files in your `tests/` directory.

Create `tests/app_test.ot`:
```otter
# tests/app_test.ot
use "../src/main.ot"

result is greet "Lovelace"
if result is not "Greetings, Lovelace!"
    fail with "Greeting output did not match expected value"
.

say "App tests passed!"
```

Run tests across your project:
```bash
otter test
```
Output:
```text
Running 1 Otter test...

PASS tests/app_test.ot

1 passed
```

---

## Next Steps

- Explore the [Language Reference](LANGUAGE_REFERENCE.md) to learn syntax rules and idioms.
- Browse the [Standard Library Reference](STDLIB_REFERENCE.md) for built-in snippets and packs.
- Set up Visual Studio Code with the [VS Code Guide](VSCODE_GUIDE.md).
