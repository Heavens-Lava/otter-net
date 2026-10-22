# otter-net 0.9.0-preview.1 - review package

**For:** the project owner's review. **Nothing is published:** no release,
tag, winget or Store submission; otterPS 1.0.1 is untouched; no money spent.

## Recommendation

**Ready for an internal preview** (unsigned packages, given to trusted
testers who know how to get past SmartScreen and Gatekeeper).
**Blocked for a public preview** until the packages are code-signed and
notarized, and the release identity decisions below are made.

## 1. The tested commit

Branch `release/0.9.0-preview.1`, commit
**`fbc46a42ea4f6ab2775ffe4a280a7b7b158da140`** = `main` at `c474ab8` (the
native port, PowerShell fallback removed) + the WinRM certification fixes
(`cert/winrm`) + the preview packaging (`cert/preview`) + the certification
tooling (`cert/certify`). Not merged into `main` yet.

## 2. Certification results (all on `fbc46a4`)

| Suite | Run | Result |
|---|---|---|
| Release certification, all six platforms (`certify.yml`, `docs/CERTIFICATION.md`) | 39747899302 | **every gate passed on win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64** |
| - build | | 0 warnings (solution, Native AOT, otter-desktop) |
| - conformance | | 19 programs; manifest fixtures 15/15 |
| - programs vs otterPS 1.0.1 (Windows PowerShell 5.1) | | 271 compared, 0 different, all native |
| - network and events | | 22 |
| - CLI | | 398 cases |
| - web pages, byte for byte | | 363 |
| - differential fuzz | | 512 per platform |
| - real projects and examples | | 84 cases (9 projects: check, run, test, build; 43 examples) |
| - the Otter website (otter-docs) | | 218 of 218 files identical to otterPS's own build |
| - no implicit PowerShell | | 1,867 runs per platform, 0 implicit launches (Windows: confirmed by the process-creation audit) |
| - native coverage | | 0 places hand anything to PowerShell; 225/225 node kinds; 0 messages missing |
| - desktop windows (WPF, real windows) | | 26/26 on win-x64 and win-arm64 |
| - Windows machine state (printers, tasks, events; German and Finnish formats) | | 32/32, 11/11, 11/11 |
| - every platform against Windows | | 0 unexpected differences (declared, with reasons: win-arm64 9, macOS 50, Linux 51) |
| Remote commands on a real, isolated WinRM endpoint (`winrm.yml`, `docs/port/winrm-certification.md`) | 39747899586 | 12/12 cases, 2/2 with WinRM stopped, 2/2 real DNS failure and connection timeout - identical to otterPS's Invoke-Command; 0 shells or host processes left |
| Packages, install tests, coexistence (`package.yml`, `docs/PACKAGING.md`, `docs/PREVIEW_RELEASE.md`) | 39747899383 | 22/22 jobs |

Record: `release/certification/fbc46a42ea4f6ab2775ffe4a280a7b7b158da140.md`
(+ `.json`, per-platform comparisons).

## 3. Remaining issues by release severity

| Severity | Issue |
|---|---|
| **Blocker (public preview only)** | Packages are unsigned: Windows SmartScreen warns, macOS Gatekeeper blocks the `.pkg` and quarantines the binary. Needs signing and notarization (section 5). |
| **Blocker (public preview only)** | Release identity not decided: stay beside otterPS (as now) or take over its MSI UpgradeCode, winget `JeffreyMacy.Otter` and Store identity. |
| Major | None. |
| Minor | Windows downloads are 41-58 MB larger: `otter-desktop.exe` is self-contained (runs without .NET). |
| Minor | With both installed on Debian/Chromebooks, `otter` is otterPS's (`~/.local/bin` comes before `/usr/bin`); on Windows (MSI) and macOS (`.pkg`) it is otter-net's. |
| Minor | macOS `.pkg` and the tarballs allow a downgrade (macOS Installer has no downgrade check); the MSI and `.deb` refuse one. |
| Minor | Remote commands not tested against a second machine, in a domain (Kerberos), or over HTTPS (otterPS never uses HTTPS). The protocol, authentication and encryption are WinRM's own. |
| Minor | Fuzz is 512 programs per platform; otterPS's own certification used 1,000 + 1,000 (`-FuzzCount 1000` reproduces that, about +1 h). |
| Minor | Not verifiable on hosted runners: a session started at high DPI; the Store package's `otter` alias. |
| Known differences | All declared with reasons (`tests/cross-platform.psd1`): Windows-only features off Windows, the OS's or .NET's own wording, path separators, machine-specific output. |
| Specification | 58 open questions in `docs/SPEC-QUESTIONS.md` (otterPS behaviours copied faithfully pending your decision); none blocks a preview. |

## 4. Installers: coexistence and uninstall

Tested on clean runners without PowerShell, x64 and Arm, in run 39747899383:

- **Fresh install, upgrade (from 0.8.0), uninstall:** MSI, ZIP, `.pkg`,
  `.deb`, tarballs - nothing left behind; 202 of 202 test programs native
  from every installed package; `otter new`/`run`/`check`/`test` work.
- **Downgrade:** refused by the MSI ("A newer version of otter-net is already
  installed.") and `apt`; allowed by the `.pkg` and tarballs.
- **PATH:** the MSI puts `%LOCALAPPDATA%\Programs\otter-net` first on the
  user PATH, once; new terminals see it; removed on uninstall.
- **Coexistence with otterPS 1.0.1** (built from its v1.0.1 tag and installed
  with its own installer), both install orders and both uninstall orders:
  both keep working, neither touches the other's files, nothing is left at
  the end. Which `otter` a new terminal gets: otter-net with the MSI and
  `.pkg`; otterPS on Debian; with the tarball only with
  `--replace-otterps-launcher`.
- **No .NET installed:** the self-contained `otter-desktop.exe` ran desktop
  windows from the installed MSI and ZIP on machines with .NET removed (26/26
  identical on x64 and Arm64; a framework-dependent build failed there, as
  the control).

## 5. Signing and notarization plan

Details, sources and pipeline changes: `docs/SIGNING.md` (prices checked
2026-10-21). **Nothing has been bought or signed up for.**

| Platform | What | Recommended | Cost |
|---|---|---|---|
| Windows | `otter.exe`, `otter-desktop.exe`, then the MSI | Azure Artifact Signing (Basic), signing from GitHub Actions with OIDC (no stored key). Individuals: US or Canada only; otherwise SSL.com IV + eSigner | US$9.99/month (fallback about US$310/year) |
| macOS | `otter` (Developer ID Application, hardened runtime), the `.pkg` (Developer ID Installer), notarized and stapled | Apple Developer Program | US$99/year |
| Linux | `.deb` | unsigned, with published SHA-256 sums (an apt repository with a GPG key is optional and free) | - |

About **US$220 per year** in total. Decisions needed: approve the spend and
eligibility, the publisher name, and a protected `signing` environment with
you as required reviewer.

## 6. Download the internal preview

GitHub Actions artifact **otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED**
(run 39747899383, artifact 12412279631, 257 MB, kept until 2026-11-21):
https://github.com/Heavens-Lava/otter-net/actions/runs/39747899383

It holds the 12 packages, `SHA256SUMS` and a `README.txt`. The repository
is public: anyone signed in to GitHub can download it while it is kept.

`SHA256SUMS` (from the run's log; verify with `sha256sum -c SHA256SUMS`, or
`shasum -a 256 -c SHA256SUMS` on macOS):

```
4b751150517680b0c3abb1b4b6140bdeb94abec1753ead091fdf9bfc17265a61  otter-net-0.9.0-preview.1-linux-arm64.tar.gz
c3ea2e319f6ee7e28cef35ba807cf8a081d97df82cf550fda79ee8608b397de5  otter-net-0.9.0-preview.1-linux-x64.tar.gz
944437070865ccd61874a920d04b6d3f9137fa91917a9b7d1a4768df9b4bdc74  otter-net-0.9.0-preview.1-osx-arm64.pkg
f42a85b6fc92f0949aa2983e5ad8a5afa05726c41c0375e6c3302e598165bccb  otter-net-0.9.0-preview.1-osx-arm64.tar.gz
c5254c7c7674133f793b65e26d47bc59a1e4b14e054186ba4a683589437916a4  otter-net-0.9.0-preview.1-osx-x64.pkg
7e641869460ce41f71b327377c49a311dbd7ff1b2ab5a69bc555c62196acbe99  otter-net-0.9.0-preview.1-osx-x64.tar.gz
eea017cfbebca2e42f5cce0e7566060178d87e8ee3ffcb3f3768cb3ca96f0e76  otter-net-0.9.0-preview.1-win-arm64.msi
755d86ad1360b9697b6aa2007a835358c1880d6c60defe3d75fef3b3d6d1e2f7  otter-net-0.9.0-preview.1-win-arm64.zip
cc33b7006a45bcec7489a925b51365ffe43118d5b02cfe742b4b3a80ad56bd0d  otter-net-0.9.0-preview.1-win-x64.msi
7c55c196b7218f0660e0c78c305afed8a1e2f71bfbb1d5c2d39f31f98041f35f  otter-net-0.9.0-preview.1-win-x64.zip
e51e9d06711603a061a395ff2d6ac9a837b4af357ff02f05c867672b90252c38  otter-net_0.9.0~preview.1_amd64.deb
ae4c42a73a3e094074412318990d9352751f84bc0a8b3035cbbcc6f1572c1268  otter-net_0.9.0~preview.1_arm64.deb
```

## 7. Decisions for you

1. Internal preview: go / no go.
2. Version scheme `0.9.0-preview.N` (`otter --version` still prints the
   language version, `Otter 1.0.1`), and the name of a final release.
3. otter-net beside otterPS, or taking over its identities at release.
4. The self-contained desktop companion (+41-58 MB on Windows) - or optional.
5. Signing spend and setup (section 5).
6. Confirm `com.otterprogramming.otter-net` and the `.deb` maintainer address.
7. Keep or delete the public review artifact after testers have it.
8. Merge `release/0.9.0-preview.1` into `main`.
