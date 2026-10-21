# Remote commands (D84) - WinRM certification

`run command "..." on remote "host" using credential "n" [into result]` runs
natively in otter-net on Windows: the PowerShell Remoting Protocol
(`src/Otter.Runtime/Psrp.cs`) over Windows' own WinRM client
(`src/Otter.Runtime/RemoteCommands.cs`, `WsmSvc.dll`), as otterPS 1.0.1's
`Invoke-Command` does.

## Environment

`.github/workflows/winrm.yml`, on a GitHub-hosted `windows-latest` runner:

- **Isolated.** WinRM is enabled for the run only; its firewall rules for
  remote addresses are disabled (loopback is never filtered), and
  TrustedHosts names only the machine itself (`localhost`, `127.0.0.1`, its
  name). Nothing outside the runner can reach the endpoint.
- **No real credentials.** Two throwaway local accounts with random passwords
  made in the run and masked in the log: `otterwinrm` (a local administrator,
  because WinRM cannot start the PowerShell host for a new non-admin account
  that has never logged on; `LocalAccountTokenFilterPolicy` for the run) and
  `otterwinrmbad` (Remote Management Users, stored with a wrong password).
  They are stored with Otter's own `set credential` (the D81 vault both
  implementations read) and removed at the end, with the credentials, the
  policy value and WinRM's configuration.
- Each case runs through otterPS 1.0.1 (Windows PowerShell 5.1,
  `Invoke-Command`) and otter-net (`tools/compare.ps1 -NativeOnly`), and is
  compared byte for byte: standard output, standard error, exit code.

## Results (run 39645983015, branch `cert/winrm`)

| Suite | Compared | Different |
|---|---:|---:|
| `tests/winrm/r4_*.ot`: success (`hostname`), arguments, the account it runs as (`whoami`), standard error merged into the output, a non-zero exit code, no `into`, several commands in one program, a program that does not exist remotely, a missing credential, a wrong password ("Access is denied."), an unknown host and an unreachable host (both refused by WinRM's TrustedHosts rule before connecting, as for otterPS) | 12 | **0** |
| The WinRM service stopped | 2 | **0** |
| A host that does not resolve and one that never answers (TEST-NET-1), each trusted for that step so WinRM really tries: DNS failure and connection timeout | 2 | **0** |
| Resource cleanup: WinRM shells open before / after the comparison; `wsmprovhost` processes left | 0 / 0; 0 | - |

## Defects found on the real endpoint and fixed

| Run | Defect | Fix |
|---|---|---|
| 39640099648 | otter crashed (access violation, exit -1073741819) when WinRM was stopped | `WSMAN_SHELL_STARTUP_INFO` lacked the V11 layout's last field (`name`), which WsmSvc.dll reads for API version 1.1 |
| (review) | a receive kept waiting on WinRM's long poll after it had its answer; a cancelled receive's final callback could reach a freed context | receives are cancelled at once; a callback context is freed only after WinRM's last callback |
| 39642299696 | WinRM refused the shell: "invalid WS-Management OptionSet element" | the option set (`protocolversion` 2.3) is sent with `optionsMustUnderstand` - measured with a probe of four layouts (run 39643406248) |
| 39644139816 | unknown/unreachable hosts gave another WinRM error; a remote error record was ignored | Invoke-Command's default authentication (WinRM chooses Negotiate/Kerberos and applies TrustedHosts); a remote ERROR_RECORD ends the command as `-ErrorAction Stop` does |

## Not covered

- A remote machine other than the runner itself (a second VM on a private
  network): the protocol, authentication and encryption are WinRM's own, the
  same for loopback and for another host; only name resolution and routing
  differ, and the DNS failure and timeout paths are covered above.
- Kerberos (a domain): hosted runners are not domain-joined. With default
  authentication WinRM chooses Kerberos in a domain exactly as it does for
  Invoke-Command.
- HTTPS listeners (port 5986): otterPS 1.0.1's Invoke-Command never uses
  them (no `-UseSSL`), so neither does otter-net.
