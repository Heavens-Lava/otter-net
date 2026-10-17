# Open questions for the Otter language specification

Things found while porting otterPS to C# where otterPS's code and its own
documentation disagree, or where behaviour is unspecified. otter-net copies
otterPS's **code** exactly - what users get today - until Jeff decides the
question in otterPS's `SPEC-DECISIONS.md`; then both implementations follow.

## Q1. `months between` across a shorter month end (found 2026-10-15)

`Measure-OtterDateDifference` in `src/Otter.Interpreter.psm1` (D32.7) says in
its comment "Calendar months, not averaged days: Jan 31 to Feb 28 is one
month", but the code counts a month only when the end day is at least the
start day:

    months between date from "2024-01-31" and date from "2024-02-28"   # 0

The same rule makes `years between` 2000-06-15 and 2026-06-14 equal 25.

**Status:** behaviour unchanged in both implementations (Jeff, 2026-10-16).
Needs a decision: either the comment is wrong (keep the code, fix the
comment), or the code is (count Jan 31 -> Feb 28/29 as one month, which
changes results for existing programs).
Covered by `tests/compat/dates.ot`.

## Q2. Registry paths under HKCR:, HKU: and HKCC: (found 2026-10-17)

D78 names five registry namespaces (`HKCU:`, `HKLM:`, `HKCR:`, `HKU:`,
`HKCC:`) and otterPS's `Assert-OtterRegistryKeyPath` accepts all five, but
the statements go through PowerShell's registry provider, which has only the
`HKCU:` and `HKLM:` drives. So under the other three, `get registry value`
answers gone, `registry key ... exists` is false, and `set registry value`
fails with "Cannot find drive. A drive with the name 'HKCR' does not exist."

**Status:** otter-net does exactly that. Needs a decision: either the three
prefixes are dropped from D78, or they are made to work (which changes what
existing programs see). Covered on Windows by `tests/compat/sys_windows.ot`.

## Q3. Windows-only system information off Windows (found 2026-10-17)

D129 says features built on Windows stop on macOS and Linux with "... is only
available on Windows, and this program is running on macOS/Linux." The
`get system information` kinds do not: on PowerShell 7 off Windows, "user"
fails as an internal error (`Exception calling "GetCurrent" with "0"
argument(s): "Windows Principal functionality is not supported on this
platform."`), "groups", "software", "tasks", "printers" and "services" are
empty lists, "cpu" has no name and "memory" has no figures, and "os" names
the .NET platform ("Unix 26.1.0"). Likewise `run command ... over ssh`
always looks for `ssh.exe`, so off Windows it reports `I could not find a
program called "ssh.exe".` even where an ssh client is installed.

**Status:** otter-net copies the code. Needs a decision on whether these
should give D129's message (or, for ssh, use the platform's `ssh`). Covered
by `tests/compat/sys_system_info.ot`, `sys_desktop.ot` and
`sys_ssh_arguments.ot`.

## Q4. `kill process p and its children` off Windows (found 2026-10-17)

otterPS finds the children through CIM (`Win32_Process`), which PowerShell 7
on macOS and Linux does not have; the lookup fails quietly and only the
process itself is killed. On Windows the whole tree goes. otter-net kills the
whole tree on every platform (the Windows PowerShell 5.1 behaviour).

**Status:** a platform difference for the cross-platform consistency
question; no test observes it.
