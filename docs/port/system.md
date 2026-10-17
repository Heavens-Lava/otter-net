# Port inventory: System (operating-system integration)

One row per form otterPS 1.0.1 accepts in this area, found by reading
`src/Otter.Parser.psm1` (Read-OtterStatement, Read-OtterValue,
Read-OtterConditionPrimary), with the interpreter case
(`src/Otter.Interpreter.psm1`) and library function
(`src/Otter.Library.psm1`) that run it.

Status: **native** (runs in C#), **partial** (some variants or platforms
still fall back), **missing** (falls back to the PowerShell Otter before
anything runs).

_Status as of the start of the port; updated as forms are ported._

| Form | Node kind | otterPS (parser / runtime) | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|---|
| `run command C [into r]` | RunProgram (IsCommand) | Read-OtterStatement `Run`; `RunProgram` -> Invoke-OtterCommand | Parser.cs ReadRun, Commands.cs | native | no | commands.ot | - |
| `run P [into p]` | RunProgram | same; Start-OtterProgram, New-OtterProcessObject | - | missing | yes | - | port |
| `run command C on remote H using credential N [into r]` | RunRemoteCommand | `Run`; Invoke-OtterRemoteCommand (WinRM) | - | missing | yes | - | port |
| `run command C over ssh to H [into r]` | RunSshCommand | `Run`; Invoke-OtterSshCommand | - | missing | yes | - | port |
| `start command C and call it J` | StartCommand | `Start`; Start-OtterCommandJob | - | missing | yes | - | port |
| `received output` / `received error output` | JobContext | Read-OtterValue; `JobContext` | - | missing | yes | - | port |
| `get system information K into x` (os, cpu, memory, disk, network, user, groups, software, tasks, printers, services) | GetSystemInfo | `Get`; Get-OtterSystemInfoValue | - | missing | yes | - | port |
| `get processes into x` | GetProcesses | `Get`; Get-OtterProcessList | - | missing | yes | - | port |
| `kill process p [and its children]` | KillProcess | `Kill`; Stop-OtterProcess | - | missing | yes | - | port |
| `set priority of process p to L` | SetProcessPriority | `Set`; Set-OtterProcessPriority | - | missing | yes | - | port |
| `wait for process p up to N seconds [into x]` | WaitForProcess | `Wait`; Wait-OtterProcess | - | missing | yes | - | port |
| `copy X to clipboard` | CopyToClipboard | `Copy`; Set-Clipboard | - | missing | yes | - | port |
| `get clipboard into x` | GetClipboard | `Get`; Get-Clipboard | - | missing | yes | - | port |
| `notify T with M` | Notify | `Notify`; Show-OtterNotification | - | missing | yes | - | port |
| `choose file into x` / `choose folder into x` / `choose file to save into x` | ChooseFile / ChooseFolder / ChooseSaveFile | `Choose`; Show-OtterFileDialog | - | missing | yes | - | port |
| `create symbolic link L pointing to T` | CreateSymbolicLink | `Create`; New-OtterSymbolicLink | - | missing | yes | - | port |
| `get symbolic link target of L into x` | GetSymbolicLinkTarget | `Get`; Get-OtterSymbolicLinkTarget | - | missing | yes | - | port |
| `file L is a symbolic link` (condition) | FileIsSymbolicLink | Read-OtterConditionPrimary; Test-OtterSymbolicLink | - | missing | yes | - | port |
| `get owner of P into x` | GetFileOwner | `Get`; Get-OtterFileOwner | - | missing | yes | - | port |
| `file P is read only` (condition) | FileIsReadOnly | Read-OtterConditionPrimary; Test-OtterFileReadOnly | - | missing | yes | - | port |
| `set file P to read only` / `to writable` | SetFileReadOnly | `Set`; Set-OtterFileReadOnly | - | missing | yes | - | port |
| `get registry value N from K into x` | GetRegistryValue | `Get`; Get-OtterRegistryValue | - | missing | yes | - | port |
| `set registry value N to D in K` | SetRegistryValue | `Set`; Set-OtterRegistryValue | - | missing | yes | - | port |
| `delete registry value N from K` | DeleteRegistryValue | `Delete`; Remove-OtterRegistryValue | - | missing | yes | - | port |
| `registry key K exists` (condition) | RegistryKeyExists | Read-OtterConditionPrimary; Test-OtterRegistryKeyExists | - | missing | yes | - | port |
| `get event log entries from L up to N into x` | GetEventLogEntries | `Get`; Get-OtterEventLogEntries | - | missing | yes | - | port |
| `set credential N to S` / `get credential N into x` / `delete credential N` | SetCredential / GetCredential / DeleteCredential | `Set`/`Get`/`Delete`; DPAPI files | - | missing | yes | - | port |
| `lock [the] computer` / `sign out` / `restart [the] computer` / `shut down [the] computer` | PowerAction | `Lock`/`Sign`/`Restart`/`Shut`; Invoke-OtterPowerAction | - | missing | yes | - | port |
| `print F to P` | PrintFile | `Print`; Send-OtterFileToPrinter | - | missing | yes | - | port |
| `store secret N with value V` / `delete secret N` | StoreSecret / DeleteSecret | pre-switch check; OtterCredentialApi | - | missing | yes | - | port |
| `secret N` / `secret N exists` | SecretRead / SecretExists | Read-OtterValue; OtterCredentialApi | - | missing | yes | - | port |
| `zip folder S into A` / `unzip A into D` | ZipFolder / UnzipFile | `Zip`/`Unzip` | Parser.cs, Files.cs | native | no | zip.ot | - |
| `copy folder S to D` / `move folder S to D` | CopyFolder / MoveFolder | `Copy`/`Move` | Parser.cs, Files.cs | native | no | folders.ot, files_errors.ot | - |
| `get environment variable N into x` / `set environment variable N to V` | Get/SetEnvironmentVariable | `Get`/`Set` | Parser.cs, Interpreter.cs | native | no | environment.ot, environment_names.ot, err_setenv_*.ot | - |
| `get system folder N into x` / `get current directory\|folder into x` | GetSystemFolder | `Get` | Parser.cs, Interpreter.cs | native | no | environment.ot | - |
| `set current directory\|folder to P` | SetCurrentDirectory | `Set` | Parser.cs, Interpreter.cs | native | no | environment.ot | - |
| `get arguments into x` | (Assign) | `Get` (D94) | - | missing | yes | - | port |
