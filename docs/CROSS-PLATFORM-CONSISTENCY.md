# Proposal: one Otter on every platform

**Status:** proposal - nothing here is decided. Otter 1.0.1's behaviour,
otterPS's `SPEC-DECISIONS.md` and otter-net's compatibility behaviour stay
exactly as they are until Jeff decides each question below in
`SPEC-DECISIONS.md`. Then both implementations follow.

**Principle:** Otter should behave like *Otter* everywhere - not like Windows
everywhere. Where Otter 1.0.1 behaves one way on Windows and another on
macOS and Linux, neither side is right by default: each case gets a decision
written in Otter's own terms.

**Scope:** a later release (Otter 1.1 is proposed). This is separate from,
and does not change, the released 1.0.1 compatibility baseline.

## Where the differences come from

Otter 1.0.1 has three behaviours today, not two:

| | Runs on | Behaviour |
|---|---|---|
| otterPS on Windows | Windows PowerShell 5.1 (.NET Framework 4.x) | the certified reference |
| otterPS on macOS and Linux | PowerShell 7 (.NET 8+) | differs from Windows wherever PowerShell 7 or .NET changed |
| otter-net, every platform | .NET 10 | copies otterPS on Windows, deliberately, on every platform |

The differences were measured on 2026-10-17 by running 169 programs through
otterPS on PowerShell 7 and otter-net on macOS
([baseline](baselines/macos-pwsh7-2026-10-17.md)): 137 identical, 20
skipped, 12 different. Each difference below comes from that run or from
following one up.

## Classifications

Each area is classified as one or more of:

- **Bug** - a decided rule (a D-number) is broken on some platform, or
  PowerShell/.NET internals reach the user. Fixing it changes output, but
  only towards what Otter already promises.
- **Spec gap** - no decision covers it, so each platform shows whatever its
  runtime does. Needs a decision; any choice changes output on some
  platform.
- **Platform-specific by nature** - the platforms really differ (file
  systems, paths), and Otter should respect that, consistently.

Each proposal also gives its **breaking-change risk**: which existing
programs would see different output, and on which platforms.

## Summary

| # | Area | Classification | Proposal | Changes output on | Risk |
|---|---|---|---|---|---|
| 1 | JSON text layout | Spec gap | One Otter layout everywhere | Windows and macOS/Linux | Medium |
| 2 | Negative zero | Bug (macOS/Linux) | Always `0` | macOS/Linux | Low |
| 3 | Saying a timer | Bug (every platform) | A timer shows as `a timer` | Windows and macOS/Linux | Low |
| 4 | .NET wording in errors | Bug (every platform, D14) | Otter's own messages | Windows and macOS/Linux | Low-medium |
| 5 | Environment variables | Bug (macOS/Linux) and spec gap (Windows) | Empty is not missing; `gone` removes | macOS/Linux; Windows for empty values | Medium |
| 6 | Folder listing order | Spec gap | Sorted by name, defined order | macOS/Linux | Low |
| 7 | File name rules | Platform-specific by nature | Each platform's own rules, Otter's wording | otter-net on macOS/Linux | Low |
| 8 | Dates in JSON depend on the time zone | Spec gap | Dates written the same on every machine | Every platform | Medium |

## 1. JSON text layout

`convert X to json` (D29) produces different text:

| | Windows (5.1) and otter-net | macOS/Linux (PowerShell 7) |
|---|---|---|
| Indent | 4 spaces; arrays aligned under their key | 2 spaces |
| After `:` | two spaces | one space |
| Whole numbers | `41` | `41.0` |
| `1 divided by 3` | `0.33333333333333331` (17 digits) | `0.3333333333333333` (shortest round-trip) |
| Empty list | `[` blank line `]`, aligned | `[]` |
| Line endings | `\r\n` | `\n` |

**Classification:** spec gap. D29 decides how JSON *becomes* Otter values,
not what text `convert ... to json` writes. Both layouts are valid JSON, and
both are artefacts of PowerShell's `ConvertTo-Json`. Neither was designed
for Otter.

**Proposal:** an Otter-defined layout, the same everywhere: 2-space indent,
one space after `:`, `[]` and `{}` when empty, numbers written as D8 writes
them where that round-trips (`41`, `2.5`) and otherwise the shortest text
that reads back as the same number (`0.3333333333333333`), `\n` between
lines. Property order stays as written.

**Breaking-change risk: medium.** Programs that read the JSON back see no
difference. Programs that compare JSON *text*, or write it to files that
other tools diff, see new text on every platform.

**Decision needed:** adopt the layout above, or keep the Windows layout
(and change macOS/Linux).

## 2. Negative zero

`say 0 minus 0` prints `0` on Windows and `-0` on macOS/Linux, and so does
any result that rounds to zero from below (`x times 0` where x is negative).

**Classification:** bug on macOS/Linux. .NET Core 3.0 changed how negative
zero formats. D8 ("numbers print without trailing zeros") describes number
display without any sign for zero, and Otter has no notion of negative zero.

**Proposal:** a zero always displays as `0`, in `say`, joined text, JSON and
error messages. Arithmetic is unchanged.

**Breaking-change risk: low.** Only macOS/Linux output changes, and only to
match Windows.

## 3. Saying a timer

`start timer workTimer` then `say workTimer` (D101):

| | Shows |
|---|---|
| Windows and otter-net | `System.Diagnostics.Stopwatch` |
| macOS/Linux | `00:00:00.1053329` |

The same text appears inside errors (`... but got System.Diagnostics.Stopwatch.`).

Found while following this up: on macOS/Linux, `if workTimer is workTimer`
is **false**. The timer is compared by its moving elapsed time, so it is not
equal to itself. On Windows and in otter-net it is true.

**Classification:** bugs. A .NET type name reaching the user breaks D14's
intent. A value that is not equal to itself is a bug on any platform.

**Proposal:** a timer displays as `a timer`, as other things without a text
form already do (`say files` prints `a file, a file, a file`). Its time is
read only through `elapsed time of` and `elapsed milliseconds of`, which
already agree everywhere. A timer is equal to itself everywhere.

**Breaking-change risk: low.** Saying a timer directly was never useful
output on either platform.

## 4. .NET wording in error messages

Some errors still carry .NET's text, which differs between .NET versions and
platforms:

| Situation | Windows (5.1) and otter-net | macOS/Linux |
|---|---|---|
| `set environment variable "" to "x"` | `Exception calling "SetEnvironmentVariable" with "2" argument(s): "String cannot be of zero length.` + `Parameter name: variable"` | `... "The value cannot be an empty string. (Parameter 'variable')"` |
| A name containing `=` | `... "Environment variable name cannot contain equal character."` | the same, ending `(Parameter 'variable')"` |
| Moving a folder that is not there | `Could not find a part of the path.` | `Could not find a part of the path '<full path>'.` |
| Copying a file onto itself | `Cannot overwrite the item C:\...\same.txt with itself.` | the same with `/` (paths) |
| A cursor move when output is not a terminal (`console.ot`) | `I could not move the cursor there. Exception calling ...` | a different check answers first: `I need a row and column of 1 or greater.` |

**Classification:** bugs. D14 says PowerShell (and so .NET) exceptions never
reach a normal user. These do, verbatim, and some reach them as an *internal*
error.

**Proposal:** each gets an Otter message with a `Try:` line, the same words
everywhere. For example: `An environment variable needs a name.` and
`"A=B" cannot be an environment variable name: names cannot contain "=".`
A path inside a message is shown the way the platform writes paths (see 7).
A complete list is made by searching both implementations for messages
passed through from .NET, and each one becomes a fixture.

**Breaking-change risk: low to medium.** Only error text changes. Programs
that catch these errors with `otherwise into reason` and compare `reason` to
the old text would change. That is rare, and the old text was never a
documented form.

## 5. Environment variables

Three cases, with the platforms differing in two of them:

| Program | Windows (5.1) | macOS/Linux (otterPS) | otter-net macOS | otter-net Windows |
|---|---|---|---|---|
| `set environment variable "X" to ""` then `get` | `gone` (setting empty removes it in .NET) | empty text | empty text | `gone` (not yet measured; expected from .NET) |
| `set environment variable "X" to gone` then `get` | `gone` | **empty text** | `gone` | `gone` |
| Variable set to empty by the parent process | `gone` | empty text | empty text | (to be measured) |

**Classification:**
- `to gone` leaving an empty variable on macOS/Linux is a **bug**. otterPS
  passes `$null`, which PowerShell turns into `""` on the way to .NET.
- What an empty value means is a **spec gap**. On Windows it is currently
  impossible for an Otter program to hold an empty variable, which follows
  from .NET's behaviour, not from a decision. Windows itself supports empty
  values.

**Proposal:**
- An empty variable is **not** a missing one. `get` returns empty text for a
  variable that exists and is empty, and `gone` only for one that does not
  exist, on every platform.
- `set environment variable "X" to ""` makes an existing, empty variable on
  every platform. On Windows this needs the operating system's own call
  (`SetEnvironmentVariableW`), not .NET's, which removes the variable.
- `set environment variable "X" to gone` removes the variable, on every
  platform.

**Breaking-change risk: medium.** Windows programs that set `""` to clear a
variable would now leave it existing and empty (`if X is gone` changes
answer). macOS/Linux programs that set `gone` would now see it removed.

**Decision needed:** confirm "empty is not missing". Alternatively, decide
that Otter does not distinguish them, and make `""` remove the variable
everywhere.

## 6. Folder listing order

`get files in "Photos"` lists `A.png`, `b.jpg`, `_notes.txt` on Windows and
in otter-net, and `_notes.txt` first on macOS/Linux. Subfolder walks differ
in the same way.

**Classification:** spec gap. Otter has no decision on order. Windows shows
NTFS's order, and otterPS on macOS/Linux shows what .NET and the file system
return. (otter-net already sorts on macOS/Linux to match Windows.)

**Proposal:** listings are sorted by name, defined in Otter's own terms:
compare names by their upper-case forms character by character
(culture-independent), and break ties by the exact text. Recursive listings
list a folder's own files, then each subfolder in the same order. This is
the order Windows gives today, so Windows output does not change.

**Breaking-change risk: low.** Only macOS/Linux output changes, and a
program depending on its old order was depending on an unstable property of
the file system.

## 7. File name rules

`write "x" to "a<b.txt"`, and names containing `?`, `|`, `*`, `:`, `"`,
`\`:

| | Result |
|---|---|
| Windows (5.1) | refused: `"a<b.txt" is not a name Otter can use for a file.` |
| otterPS on macOS/Linux | allowed - these are ordinary names there |
| otter-net on every platform | refused, as on Windows |

**Classification:** platform-specific by nature. These names are invalid on
Windows file systems and valid on macOS and Linux ones. otter-net refusing
them on macOS/Linux applies a Windows rule where it does not belong.

**Proposal:**
- Otter checks a file name against the **platform's own rules**. On Windows
  those are `< > : " | ? *`, control characters, the reserved device names
  (`CON`, `NUL`, ...), and names ending in a dot or space. On macOS and Linux
  the only rule is no NUL character, with `/` separating folders.
- The refusal message is Otter's, names the platform, and is the same words
  everywhere: `"a<b.txt" cannot be a file name on Windows.`
- Optional, for programs meant to run anywhere: `otter check` *warns*, but
  never refuses, about a literal file name that would be refused on another
  platform.
- Paths in messages keep the platform's separator (`\` on Windows, `/`
  elsewhere). This is the one known difference in CI's cross-platform
  check, and it becomes intended.

**Breaking-change risk: low.** It changes otter-net on macOS/Linux (it would
accept these names, like otterPS there), and changes the refusal wording on
Windows.

## 8. Dates in JSON depend on the machine's time zone

Found by otter-net's CI on 2026-10-17, not by the macOS run: the same
program, on the same Windows, wrote different JSON on two machines.

    day is date from "2024-01-15"
    convert day to json into dayText

| Machine time zone | `dayText` contains |
|---|---|
| UTC | `"\/Date(1705276800000)\/"` |
| US Pacific | `"\/Date(1705305600000)\/"` - 8 hours later |

A date (D32) has no time zone of its own. PowerShell's JSON conversion treats
it as local time and writes the instant in UTC, so the number depends on
where the program runs. The `\/Date(...)\/` form itself is a .NET
convention, not one other JSON readers understand.

**Classification:** spec gap. D29 decides how JSON becomes Otter values, but
not how an Otter date becomes JSON. It affects Windows and macOS/Linux alike,
and it is not a platform difference but a *machine* difference.

**Proposal:** a date becomes JSON text in ISO 8601, independent of the time
zone: `"2024-01-15"` for a date without a time of day, and
`"2024-01-15T09:30:00"` for one with (no zone, matching how D32 treats
dates). This could be decided together with 1.

**Breaking-change risk: medium.** Every program that writes dates to JSON
writes different text. Programs reading that JSON back with Otter today get
text either way, since D29 has no date type in JSON.

## Towards a cross-platform conformance specification

Today "correct" means "what otterPS on Windows prints". The proposal is to
define correctness by **what Otter guarantees on every platform**:

1. **Platform conformance fixtures.** A folder of programs with their
   expected output written from the specification, not captured from an
   implementation. Each must pass, byte for byte (after normalising line
   endings), on Windows, macOS, Linux and ChromeOS. Each area above gets a
   fixture once decided.
2. **Declared platform differences.** Where Otter intentionally differs (7),
   the fixture declares the expected output per platform. Any undeclared
   difference fails CI.
3. **Both implementations are tested against the fixtures.** otterPS on 5.1
   and on PowerShell 7, and otter-net on all six runtimes. otter-net's CI
   already runs on every platform; the PowerShell 7 run of otterPS would be
   added to it.
4. **The 1.0.1 baseline stays as it is.** The existing compatibility
   comparison against otterPS 1.0.1 keeps running unchanged, and records
   each decided change as an expected difference with its D-number. Nothing
   is silently broken: every behaviour change is decided, numbered and
   visible.

## Order of work, once decided

1. Jeff decides each question, recorded as a D-number in `SPEC-DECISIONS.md`.
2. Platform conformance fixtures are written for the decided rules.
3. otterPS and otter-net both change, each passing the fixtures on every
   platform.
4. The 1.0.1 baseline comparison lists the decided differences by D-number.
5. Release notes name every behaviour change.
