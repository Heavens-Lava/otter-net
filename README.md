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

## Platforms

Every commit is built as a native executable and tested on each of these:

| Platform | Runtime | Built and tested on |
|---|---|---|
| Windows 10/11, Intel and AMD | `win-x64` | Windows (also compared with otterPS) |
| Windows 11 on Arm | `win-arm64` | Windows 11 Arm |
| macOS, Apple silicon | `osx-arm64` | macOS |
| macOS, Intel | `osx-x64` | macOS Intel |
| Linux, Intel and AMD | `linux-x64` | Debian 12 |
| Linux, Arm (Raspberry Pi 4/5, Arm servers) | `linux-arm64` | Debian 12 |
| Chromebooks | `linux-x64` or `linux-arm64` | Debian 12 |

**Chromebooks** run `otter` in ChromeOS's Linux development environment
(Settings > Advanced > Developers > Linux development environment), which is
Debian 12: the Linux executable for the Chromebook's processor runs there
unchanged. Linux executables are built on Debian 12 so they need nothing
newer than its C library, and so also run on current distributions. Managed
(school or work) Chromebooks can have the Linux environment turned off by
the administrator.

Programs that otter-net does not run natively yet are handed to the
PowerShell Otter, which needs PowerShell 7 (`pwsh`) on Linux, macOS and
Chromebooks ([install it](https://aka.ms/install-powershell)); on Windows it
uses the built-in Windows PowerShell.

## Building

    dotnet build otter-net.sln -c Release
    dotnet src/Otter.Cli/bin/Release/net10.0/otter.dll run hello.ot

`tools/publish.ps1` makes the native executable. `tools/compare.ps1
-OtterPs <otterPS checkout> -OtterNet <otter.dll or otter.exe>` compares the
two implementations.

## License

Otter Free Use License 1.1 - free to use; source-available, not open source.
See [LICENSE](LICENSE).
