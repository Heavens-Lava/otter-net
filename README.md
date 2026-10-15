# otter-net

The Otter programming language on a native .NET engine.

[Otter](https://otterprogramming.com) is a programming language that reads
like plain English. Its released implementation,
[otterPS](https://github.com/Heavens-Lava/otterPS), is written in PowerShell.
otter-net is a second implementation of **the same language** in C#
(.NET 10, Native AOT): one self-contained `otter` per platform that starts in
tens of milliseconds instead of seconds and needs nothing installed.

> **Status: prototype.** Not part of any Otter release yet. Use the
> [released Otter](https://otterprogramming.com/download/) to write programs.

## What "the same language" means

- No new syntax and no new features. otterPS's `SPEC-DECISIONS.md` is the
  language specification for both implementations.
- Every program must print the same output, the same error messages and
  exit with the same code as otterPS. `tools/compare.ps1` runs programs
  through both and compares them byte for byte.
- Until otter-net supports everything a program uses, `otter` hands the whole
  program to the PowerShell Otter before anything runs, so nothing is lost.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design and the plan.

## Where it stands

| | |
|---|---|
| Lexer | Complete port of otterPS's |
| Parser and interpreter | Core language: say, ask, variables, arithmetic, conditions, loops, lists, functions, runtime errors |
| Compatibility | 0 differences in 86 programs (otterPS's conformance fixtures, examples and tests/compat); 33 run natively |
| Cold start, `say "Hello"` | about 30 ms (otterPS: about 1.5 s) |

## Building

    dotnet build otter-net.sln -c Release
    dotnet src/Otter.Cli/bin/Release/net10.0/otter.dll run hello.ot

`tools/publish.ps1` makes the native executable. `tools/compare.ps1
-OtterPs <otterPS checkout> -OtterNet <otter.dll or otter.exe>` compares the
two implementations.

## License

Otter Free Use License 1.1 - free to use; source-available, not open source.
See [LICENSE](LICENSE).
