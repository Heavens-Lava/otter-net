# Port inventory: System (operating-system integration)

One row per form otterPS 1.0.1 accepts in this area, found by reading
`src/Otter.Parser.psm1` (Read-OtterStatement, Read-OtterValue,
Read-OtterConditionPrimary), with the interpreter case
(`src/Otter.Interpreter.psm1`, Invoke-OtterStatement / Get-OtterValue) and the
library function (`src/Otter.Library.psm1`) that runs it.

Status: **native** (runs in C# everywhere), **partial** (some variants or
platforms still go to the PowerShell Otter), **missing** (always goes to the
PowerShell Otter, decided before anything runs).

Parser: `src/Otter.Language/Parser.System.cs` unless noted. Nodes:
`src/Otter.Language/Syntax.System.cs`. Runtime: the case in
`src/Otter.Runtime/Interpreter.System.cs`, with the work in the file named.

| Form | Node kind | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|---|
| `run command C [into r]` | RunProgram (IsCommand) | `Run`; Invoke-OtterCommand | ReadSystemRun -> RunCommandStmt; Commands.cs | native | no | commands.ot | - |
| `run P [into p]` | RunProgram | `Run`; Start-OtterProgram, New-OtterProcessObject | RunProgramStmt; Processes.cs `Start` | native | no | sys_processes.ot | - |
| `run command C over ssh to H [into r]` | RunSshCommand | `Run`; Invoke-OtterSshCommand | RunSshCommandStmt; `RunOverSsh` | native | no | sys_ssh_arguments.ot | - |
| `run command C on remote H using credential N [into r]` | RunRemoteCommand | `Run`; Invoke-OtterRemoteCommand (Invoke-Command over WinRM) | ReadSystemRun -> RunRemoteCommandStmt; Credentials.cs `OtterRemoteCredentials` | partial | on Windows | p2_remote_command.ot, sys_syntax_errors.ot | off Windows otterPS stops at the credential (name check, store folder, nothing decrypts) and so does otter-net; on Windows it needs PowerShell remoting: a decision is needed (see "Remote commands on Windows" below) |
| `start command C and call it J` | StartCommand | `Start`; Start-OtterCommandJob | Parser.cs (Unsupported) | missing | yes | - | needs the event loop (`on output from`, `on exit of`, `cancel`, `is running`) of the Network area |
| `received output` / `received error output` | JobContext | Read-OtterValue; `JobContext` | Parser.cs (Unsupported) | missing | yes | - | as above |
| `get system information K into x`: os, cpu, memory, disk, network, user, groups, software, services | GetSystemInfo | `Get`; Get-OtterSystemInfoValue | GetSystemInfoStmt; SystemInfo.cs, Services.cs | native | no | sys_system_info.ot | - |
| `get system information K into x`: tasks, printers, or a kind that is not written as text | GetSystemInfo | same (Get-ScheduledTask, Win32_Printer) | same; ScheduledTasks.cs (Task Scheduler 2.0 COM through its vtable), Printers.cs (EnumPrinters, GetDefaultPrinter) | native | no | t3_system_tasks_printers.ot, sys_desktop.ot | the order of tasks and the status of paused or failed printers are to be confirmed on Windows (SPEC-QUESTIONS T3-1, T3-2); off Windows empty lists, as otterPS on PowerShell 7 |
| `get processes into x` | GetProcesses | `Get`; Get-OtterProcessList | Processes.cs `List` | native | no | sys_processes.ot | - |
| `kill process p [and its children]` | KillProcess | `Kill`; Stop-OtterProcess | Processes.cs `Kill` | native | no | sys_processes.ot | tree kill everywhere (SPEC-QUESTIONS Q9) |
| `set priority of process p to L` | SetProcessPriority | `Set`; Set-OtterProcessPriority | Processes.cs `SetPriority` | native | no | sys_processes.ot | - |
| `wait for process p up to N seconds [into x]` | WaitForProcess | `Wait`; Wait-OtterProcess | Processes.cs `Wait` | native | no | sys_processes.ot | - |
| `copy X to clipboard` | CopyToClipboard | `Copy`; Set-Clipboard | Clipboard.cs | native | no | sys_clipboard.ot | - |
| `get clipboard into x` | GetClipboard | `Get`; Get-Clipboard -Raw | Clipboard.cs | native | no | sys_clipboard.ot | - |
| `notify T with M` | Notify | `Notify`; Show-OtterNotification (NotifyIcon) | Desktop.cs `Notify` (Shell_NotifyIcon) | native | no | sys_print_notify.ot | - |
| `choose file into x` / `choose folder into x` / `choose file to save into x` | ChooseFile / ChooseFolder / ChooseSaveFile | `Choose`; Show-OtterFileDialog | ChooseFileStmt; Desktop.cs `ChooseFile` (GetOpenFileName, GetSaveFileName, SHBrowseForFolder) | native | no | sys_desktop.ot, sys_print_notify.ot (off Windows only: a dialog waits for a person) | - |
| `create symbolic link L pointing to T` | CreateSymbolicLink | `Create`; New-OtterSymbolicLink | SystemFiles.cs | native | no | sys_links.ot | - |
| `get symbolic link target of L into x` | GetSymbolicLinkTarget | `Get`; Get-OtterSymbolicLinkTarget | SystemFiles.cs | native | no | sys_links.ot | - |
| `file L is a symbolic link` (condition) | FileIsSymbolicLink | Read-OtterConditionPrimary; Test-OtterSymbolicLink | TryReadSystemCondition; SystemFiles.cs | native | no | sys_links.ot | - |
| `file P exists` / `file P is locked` (condition) | FileExists / FileLocked | Read-OtterConditionPrimary | TryReadSystemCondition (the condition is read here so its other variants are too); Files.cs | native | no | files tests, sys_syntax_errors.ot | - |
| `get owner of P into x` | GetFileOwner | `Get`; Get-OtterFileOwner (Get-Acl) | SystemFiles.cs `GetOwner` | native | no | sys_windows.ot | - |
| `file P is read only` (condition) | FileIsReadOnly | Read-OtterConditionPrimary; Test-OtterFileReadOnly | SystemFiles.cs | native | no | sys_links.ot | - |
| `set file P to read only` / `to writable` | SetFileReadOnly | `Set`; Set-OtterFileReadOnly | SystemFiles.cs | native | no | sys_links.ot | - |
| `get registry value N from K into x` | GetRegistryValue | `Get`; Get-OtterRegistryValue | Registry.cs | native | no | sys_windows.ot | - (SPEC-QUESTIONS Q7) |
| `set registry value N to D in K` | SetRegistryValue | `Set`; Set-OtterRegistryValue | Registry.cs | native | no | sys_windows.ot | - |
| `delete registry value N from K` | DeleteRegistryValue | `Delete`; Remove-OtterRegistryValue | Registry.cs | native | no | sys_windows.ot | - |
| `registry key K exists` (condition) | RegistryKeyExists | Read-OtterConditionPrimary; Test-OtterRegistryKeyExists | Registry.cs | native | no | sys_windows.ot | - |
| `get event log entries from L up to N into x` | GetEventLogEntries | `Get`; Get-OtterEventLogEntries (Get-WinEvent) | GetEventLogEntriesStmt; EventLog.cs `OtterEventLog` (wevtapi.dll) | native | no | sys_desktop.ot (Windows) | not yet compared on Windows: run sys_desktop.ot there (level names, messages of events without a description) |
| `set credential N to S` / `get credential N into x` / `delete credential N` | SetCredential / GetCredential / DeleteCredential | `Set`/`Get`/`Delete`; DPAPI files in %LOCALAPPDATA%\Otter\Credentials | Credentials.cs `OtterCredentials` (CryptProtectData) | native | no | sys_windows.ot | - |
| `store secret N with value V` / `delete secret N` | StoreSecret / DeleteSecret | pre-switch check; OtterCredentialApi (CredWrite/CredDelete) | Credentials.cs `OtterVault` | native | no | sys_windows.ot, sys_syntax_errors.ot | - |
| `secret N` / `secret N exists` | SecretRead / SecretExists | Read-OtterValue; OtterCredentialApi (CredRead) | TryReadSystemValue; `OtterVault` | native | no | sys_windows.ot | - |
| `lock [the] computer` / `sign out` / `restart [the] computer` / `shut down [the] computer` | PowerAction | `Lock`/`Sign`/`Restart`/`Shut`; Invoke-OtterPowerAction | `RunPowerAction` (shutdown.exe, rundll32.exe) | native | no | sys_windows.ot (off Windows only, never run on Windows), sys_syntax_errors.ot | - |
| `print F to P` | PrintFile | `Print`; Send-OtterFileToPrinter (PrintDocument) | Desktop.cs `Print` (EnumPrinters, GDI) | native | no | sys_print_notify.ot | - |
| `zip folder S into A` / `unzip A into D` | ZipFolder / UnzipFile | `Zip`/`Unzip` | Parser.cs, Files.cs | native | no | zip.ot | - |
| `copy folder S to D` / `move folder S to D` | CopyFolder / MoveFolder | `Copy`/`Move` | Parser.cs, Files.cs | native | no | folders.ot, files_errors.ot | - |
| `get environment variable N into x` / `set environment variable N to V` | Get/SetEnvironmentVariable | `Get`/`Set` | Parser.cs, Interpreter.cs | native | no | environment.ot, environment_names.ot, err_setenv_*.ot | - |
| `get system folder N into x` / `get current directory\|folder into x` | GetSystemFolder | `Get` | Parser.cs, Interpreter.cs | native | no | environment.ot | - |
| `set current directory\|folder to P` | SetCurrentDirectory | `Set` | Parser.cs, Interpreter.cs | native | no | environment.ot | - |
| `get arguments into x` | (Assign, D94) | `Get` | ReadSystemGet -> AssignStmt | native | no | sys_ssh_arguments.ot | - |

Every syntax error these forms report is ported with its message and
position; `tests/compat/sys_syntax_errors.ot` has one of each kind.

## Platform notes

- Windows-only features give otterPS's own message elsewhere: "<feature> is
  only available on Windows, and this program is running on macOS/Linux." and,
  for the credential vault, "The credential vault needs an operating-system
  credential store, ...". The check comes first, as in otterPS.
- Where Windows PowerShell 5.1 and PowerShell 7 differ, otter-net follows 5.1
  on every platform: the .NET Framework wording of Windows error messages
  (`The system cannot find the file specified`), and killing a process's
  children (Q9).
- Windows PowerShell reads "os", "cpu" and "memory" through WMI. otter-net
  reads the sources WMI itself uses: the registry's `ProductName` (as
  "Microsoft " and the name, with "Windows 10" read as "Windows 11" from build
  22000, as WMI reports it), `ProcessorNameString` and GlobalMemoryStatusEx
  (whole kilobytes, as WMI counts them).
- Windows Forms and System.Drawing (notifications, dialogs, printing) are not
  available to a native program; the Windows calls they make are made
  directly. The printed page is laid out like PrintDocument's: 10-point
  Consolas, one-inch margins, as many lines on each page as fit.
- A program that uses a form still handed to the PowerShell Otter (on
  Windows: remote commands) runs entirely in the PowerShell Otter,
  decided before anything runs. Everywhere else every form here is native.
- The event log is read as Get-WinEvent reads it, through the Windows Event
  Log API (`EvtOpenChannelEnum`, `EvtQuery` newest first, `EvtRender`,
  `EvtOpenPublisherMetadata`, `EvtFormatMessage`): `-LogName` wildcards, a
  debug or analytic log skipped when matched by a wildcard and refused when
  named, the level name and message gone when the provider's metadata or
  message cannot be found (EventLogRecord's own rule; Get-WinEvent's "Cannot
  retrieve event message text." is replaced by that gone), "No events were
  found" as an empty list, and Get-WinEvent's other texts after "I could not
  read the event log". Written from PowerShell's and .NET's sources on macOS:
  it still needs a run of `tests/compat/sys_desktop.ot` on Windows.

## Remaining on Windows, and why

- **Remote commands** (`Parser.System.cs` ReadSystemRun, `Unsupported("remote commands")`).
  Invoke-OtterRemoteCommand runs `Invoke-Command -ComputerName H -Credential
  C -ScriptBlock { ... & $exe $args 2>&1 | Out-String }`: PowerShell
  remoting, i.e. the PowerShell Remoting Protocol (PSRP) carried over
  WS-Management, to the remote `microsoft.powershell` endpoint, which runs a
  PowerShell script block there and sends back serialized PowerShell objects
  (output strings, and error records for the command's standard error,
  formatted by the remote Out-String). The local side must speak PSRP: the
  WinRM client API (`WSManCreateShell` in wsmsvc.dll) can open a plain
  `cmd` shell, but that runs a different command line, merges standard error
  differently and does not produce what Out-String produces, so it is not
  the same statement. Options for the owner:
  1. Keep handing programs that use it to the PowerShell Otter on Windows
     (today's behaviour; the only place PowerShell is still needed there).
  2. Run only this statement through Windows' built-in `powershell.exe`
     (always present on Windows), passing the command, host and secret
     through standard input, and the output back as text. No PowerShell for
     anything else; exact otterPS output, error text included.
  3. Implement PSRP over WS-Management in C# (a large protocol: fragments,
     CLIXML serialization, runspace pools, NTLM/Kerberos through
     WinRM's own client API). Months of work for one statement.
  4. Change the statement's meaning (for example a plain WinRM `cmd`
     shell), which is a semantic change and needs a SPEC-DECISIONS entry.
  Nothing is implemented beyond option 1 until that decision.
