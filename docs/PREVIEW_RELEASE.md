# otter-net internal preview: identity, version and results

Status (2026-10-21): **built and tested, not published.** The preview
packages are unsigned workflow artifacts for internal testing only. Nothing
was released, tagged, uploaded to a registry, or submitted to winget or the
Microsoft Store; otterPS 1.0.1 (the released Otter) is unchanged and is not
replaced by anything here.

## Version

**`0.9.0-preview.1`**, in `VERSION` (the one source: MSBuild's assembly and
file versions, `tools/package.ps1`'s file names and every installer read
it).

| Where | Shows |
|---|---|
| `otter --version` (and `otter-desktop.exe --version`) | `Otter 1.0.1` - unchanged: the **language** version (`Program.Version`, defined by otterPS) |
| package file names, `VERSION` in every install, the `.pkg` | `0.9.0-preview.1` |
| Windows Settings > Apps | "Otter (otter-net) 0.9.0-preview.1", version `0.9.0`, comment "Otter 1.0.1, native implementation (otter-net) - internal preview 0.9.0-preview.1, unsigned. Not an Otter release." |
| `otter.exe` / `otter-desktop.exe` file properties | product "Otter (otter-net)", version 0.9.0-preview.1 |
| MSI ProductVersion (numbers only) | `0.9.0` (previews of 0.9.0 upgrade each other: `AllowSameVersionUpgrades`) |
| `.deb` | `0.9.0~preview.1` (sorts before `0.9.0`); description "otter-net internal preview" |
| `.pkg` title | "Otter (otter-net) 0.9.0-preview.1 - internal preview" |

### Why 0.9.0-preview.1 (and not 1.0.1-preview.1)

- **otter-net's version is the product's, not the language's.** otter-net
  is a second implementation of Otter 1.0.1, the way a compiler has its own
  version and implements a language version. `otter --version` keeps saying
  `Otter 1.0.1` because changing it would change behaviour that is
  compared byte for byte with otterPS (out of scope here; see the decisions).
- **`1.0.1-preview.1` would read as a pre-release of a version that is
  already released** (otterPS 1.0.1, 2026-10-15), and sorts *before*
  `1.0.1` and before the earlier `1.0.1-rc.1` candidates (semver and Debian
  both order `preview` < `rc`), so apt would call it a downgrade.
- **`1.0.2-preview.1` or `1.1.0-preview.1`** would announce otter-net as
  the next Otter release before the owner decides whether it replaces
  otterPS (decision 2 below).
- **`0.9.0` says "not yet 1.0 of this product"**, sorts below any 1.x
  number a final release could take (so a final 1.0.x or 1.1 MSI/.deb/.pkg
  upgrades it naturally), and a later `0.9.0-preview.2` / `0.9.x` sorts
  after it. The trade-off: the internal `1.0.1-rc.1` builds (artifacts that
  expire 2026-11-02, never released) count as *newer*, so a tester who
  installed one uninstalls it first (the MSI refuses the downgrade with "A
  newer version of otter-net is already installed."; apt refuses it
  without `--allow-downgrades`).

## Installation identity: separate from otterPS

| | otterPS 1.0.1 (released) and its unpublished installers | otter-net internal preview |
|---|---|---|
| Product name | Otter / "Otter Programming Language" (MSI, Store) | **Otter (otter-net)** |
| Windows MSI UpgradeCode | `F2065F2E-9E7A-405C-810C-3826A1DAAE39` (built, not published) | **`B6DDDF92-EF9A-4FC0-ACA2-E1E3B3413E9B`** |
| Windows install folder | ZIP: `%LOCALAPPDATA%\Otter\1.0.1`; MSI: `%LOCALAPPDATA%\Programs\Otter` | **`%LOCALAPPDATA%\Programs\otter-net`** (MSI); the ZIP runs where it is extracted |
| Windows PATH | its folder **appended** to the user PATH (`Install-Otter.ps1 -AddToUserPath`; its MSI: `Part="last"`) | its folder **first** in the user PATH (MSI, `Part="first"`); removed on uninstall; the machine PATH is never touched |
| Windows registry | `HKCU\Software\Otter` (MSI) | **`HKCU\Software\otter-net`** |
| Settings > Apps | "Otter Programming Language" (MSI) | **"Otter (otter-net) 0.9.0-preview.1"** |
| winget | `JeffreyMacy.Otter`, moniker `otter` (manifest written, not submitted) | **none** - not reused, nothing submitted |
| Microsoft Store | `JeffreyMacy.OtterProgrammingLanguage` (published; app execution alias `otter.exe`) | **none** |
| macOS | ZIP: `~/.local/share/otter/1.0.1`, launcher `~/.local/bin/otter`; no pkg | **pkgutil id `com.otterprogramming.otter-net`**, `/usr/local/lib/otter-net/<v>`, link `/usr/local/bin/otter`; tarball: `~/.local/share/otter-net/<v>` |
| Debian / Chromebook | ZIP as on macOS; no .deb | **dpkg package `otter-net`**, `/usr/lib/otter-net/otter`, link `/usr/bin/otter`, docs `/usr/share/doc/otter-net` |
| `otter --version` | `Otter 1.0.1` | `Otter 1.0.1` (same language) |

No installer of otter-net writes, moves or deletes anything of otterPS's,
and otter-net no longer reads otterPS's installation at all (since
`c474ab8` it never starts PowerShell). The coexistence tests below check
this in both install and uninstall orders.

**How a user tells them apart** (both print `Otter 1.0.1`): the command's
path - `where.exe otter` on Windows (otter-net:
`...\Programs\otter-net\otter.exe`; otterPS: `...\Otter\1.0.1\otter.cmd`),
`ls -l "$(command -v otter)"` on macOS and Linux - the package's `VERSION`
file, Settings > Apps / `pkgutil --pkgs` / `dpkg -l otter-net`. Each
package's README says so ("Which otter am I running?").

**Identifiers to confirm** (placeholders the owner should own):
`com.otterprogramming.otter-net` assumes the owner controls
otterprogramming.com (it is the project's homepage); the `.deb` Maintainer
is the GitHub no-reply address; the MSI Manufacturer is "Jeffrey Macy" (as
otterPS's MSI).

## The packages

Built by `.github/workflows/package.yml` on each platform, kept only from a
manual run or a push to `cert/preview`, after every test passed, as **one
workflow artifact `otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED`**
(30 days) with `SHA256SUMS` and a `README.txt`. Other runs (main, pull
requests) keep their packages for one day, between jobs only.

### Run 39647277612 (2026-10-21, commit `bf21fec`, branch `cert/preview`)

All 22 jobs passed. Artifact **`otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED`**
(artifact id 12368661883, 257 MB zipped, kept until 2026-11-20):

    gh run download 39647277612 -R Heavens-Lava/otter-net -n otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED
    sha256sum -c SHA256SUMS      # macOS: shasum -a 256 -c SHA256SUMS

Downloaded and verified (`shasum -a 256 -c SHA256SUMS`: all OK):

| File | Bytes | SHA-256 |
|---|---:|---|
| `otter-net-0.9.0-preview.1-win-x64.msi` | 51,339,264 | `e6c1ac292ff00d54045894189f3499fffb6c6ca2db3d82bbb6ebebed3a1f1a7e` |
| `otter-net-0.9.0-preview.1-win-x64.zip` | 63,547,918 | `3681e6dcde706bd1707ab23bd8a34861747076a91fc7a9b14a34d2fe3e37f7ce` |
| `otter-net-0.9.0-preview.1-win-arm64.msi` | 45,887,488 | `e3fa75ce2b24d9fc7ce4cd3704b43351f687b39b10b6ddfc49a197b160a83c13` |
| `otter-net-0.9.0-preview.1-win-arm64.zip` | 58,374,305 | `1ec596108becba6e99711775f290080bdaefe684ea6920d91900b26a30eda03f` |
| `otter-net-0.9.0-preview.1-osx-arm64.pkg` | 4,815,458 | `ca96e0d300eb7d3676ed5f851b97bb398e902fc1c9a65773fdeb371130c93d50` |
| `otter-net-0.9.0-preview.1-osx-arm64.tar.gz` | 4,806,694 | `9087504e6568926ebab9294120f78048636fc9a8ee37601af5dd77fbbdfcf8dc` |
| `otter-net-0.9.0-preview.1-osx-x64.pkg` | 5,277,694 | `cfa2d03e86ed429fffbac84ff24ba01abacd14fcaa4f7575183dc13a5dd0fcde` |
| `otter-net-0.9.0-preview.1-osx-x64.tar.gz` | 5,272,943 | `3b732d6f1338b1637da8d8771cf87cf719813d309227384acbbfee11f43ed0f3` |
| `otter-net_0.9.0~preview.1_amd64.deb` | 4,334,024 | `ab6761e2cabd6f072bd84b174dd1a5ef377d4709ade546577695da05807b44f6` |
| `otter-net-0.9.0-preview.1-linux-x64.tar.gz` | 5,570,278 | `9bba84ae8e321eb53ca843a742e9b116abc813103bd2b2fc77ee2bdce9d1d408` |
| `otter-net_0.9.0~preview.1_arm64.deb` | 3,740,208 | `723a4fd7fc8eed44d14f712aaada9592c3ae325c0fcda2d033aa261c65d73c3f` |
| `otter-net-0.9.0-preview.1-linux-arm64.tar.gz` | 5,208,541 | `d0d17580a33d15171a7c96f7d67caab83b3e9e274652df26a9785dc6ab502410` |

Builds are not byte-identical: each run's packages have their own checksums.

### The self-contained desktop companion: size

| | framework-dependent `otter-desktop` (main, run 39618736521) | self-contained (this preview) | difference |
|---|---:|---:|---:|
| win-x64 MSI | 4,636,672 | 51,339,264 | +46.7 MB |
| win-x64 ZIP | 5,867,103 | 63,547,918 | +57.7 MB |
| win-arm64 MSI | 4,497,408 | 45,887,488 | +41.4 MB |
| win-arm64 ZIP | 5,570,820 | 58,374,305 | +52.8 MB |
| installed `otter-desktop.exe` (x64 / arm64) | 162,816 + 3 DLLs (~1.2 MB) | 124,046,523 / 139,672,681 | |

Beside it: `D3DCompiler_47_cor3.dll` (x64 only), `PenImc_cor3.dll`,
`PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`.
The MSI (LZX, `CompressionLevel="high"`) is about 12 MB smaller than the
ZIP (deflate). Installed size on Windows: about 145 MB (x64; `otter.exe` is about 12 MB of it).

### Desktop windows with no .NET (Windows x64 and Arm64)

`tools/test-desktop-nodotnet.ps1` in job "Desktop windows without .NET",
both architectures passed. How the runner was made a machine without .NET
for the test process and everything it starts:

1. `C:\Program Files\dotnet` and `C:\Program Files (x86)\dotnet` renamed
   (the .NET host's default locations);
2. `HKLM\SOFTWARE\dotnet` and `HKLM\SOFTWARE\WOW6432Node\dotnet` renamed
   (the registered `InstallLocation`s);
3. every PATH folder holding `dotnet.exe` dropped from the test PATH;
4. `DOTNET_ROOT`, `DOTNET_ROOT_X64`, `DOTNET_ROOT_ARM64`, `DOTNET_ROOT(x86)`
   and `DOTNET_ROOT_X86` set to an empty folder, every other `DOTNET_*`
   variable removed, `DOTNET_MULTILEVEL_LOOKUP=0`;
5. **negative control:** a framework-dependent build of the same
   `otter-desktop.exe` must fail there - it did, exit -2147450749 (0x80008083)
   "You must install .NET to run this application." (the test stops if it
   starts).

Then, from the installed MSI (and again from the extracted ZIP):
`otter-desktop.exe --version` -> `Otter 1.0.1`; tests\compat
`ui_run_create.ot` and `ui_run_create_try.ot` (create WPF controls, end by
themselves) ran in `otter-desktop.exe` (`OTTER_NET_TRACE` shows the hand-off);
a program that shows a window, closed by the test with `WM_CLOSE`, ran its
`when ... is closed` handler and went on. And the desktop harness
(`tests/desktop`, published self-contained so it needs no .NET either),
with the installed `otter.exe`, against otterPS 1.0.1: **26 of 26 programs
identical** (window tree after every step, pixels, output, errors, exit
code) on **both x64 and Arm64** (desktop.yml covers x64 only).

### Install, upgrade, downgrade, uninstall, PATH (without PowerShell)

| Platform | Package | Result |
|---|---|---|
| Windows x64 (windows-latest), Windows 11 Arm64 | MSI | over 0.8.0: one Apps entry, install folder once and **first** on the user PATH, machine PATH untouched; a **new terminal** (checked from the registry PATH and from a fresh Task Scheduler process) finds `...\Programs\otter-net\otter.exe`; tests\compat 202 of 202 native; new/run/check/test; startup 11 ms (x64), 20 ms (Arm64); uninstall removes files, folder, PATH entry, `HKCU\Software\otter-net`, Apps entry, and a new terminal finds no `otter`; **downgrade** (over 0.9.1) refused, msiexec 1603 "A newer version of otter-net is already installed.", the newer stays |
| | ZIP | checksums match; `otter.exe` runs in place |
| Debian 12 x64, arm64 (= Chromebook Linux) | .deb | over 0.8.0; tests/compat 202 of 202; startup 7 ms / 6 ms; `apt-get remove --purge` leaves nothing; **downgrade** (over 0.9.1) refused by apt without `--allow-downgrades`, the newer stays |
| | .tar.gz | upgrade, 202 of 202, uninstall leaves nothing; otterPS's launcher kept unless asked and put back; a removed otterPS's launcher is not put back (fixed in this branch); downgrade allowed (no check), one version left |
| macOS Apple silicon, Intel | .pkg | refuses another program's `/usr/local/bin/otter`; over 0.8.0 leaves one version; 202 of 202; startup 18 ms / 54 ms; `uninstall.sh` leaves nothing and forgets the receipt; **downgrade allowed** (macOS Installer has no downgrade check): leaves only the installed version, linked and registered |
| | .tar.gz | as on Linux; startup 18 ms / 39 ms |

### Beside otterPS 1.0.1

otterPS's release ZIP was built in the run by its own
`tools/New-OtterDistribution.ps1` from the `v1.0.1` tag (Windows PowerShell
5.1, as released) and installed with its own `Install-Otter.ps1
-AddToUserPath` (Windows PowerShell 5.1 on Windows; PowerShell 7.4.6 on
Linux and macOS). Order A: otterPS, then otter-net; remove otter-net, then
otterPS. Order B: otter-net, then otterPS; remove otterPS, then otter-net.
In every case both `otter --version` and a program worked through each
one's own path, and removing one left the other's files (hashed) and
command intact; nothing of either was left at the end.

| Platform | Package | Which `otter` a new terminal gets with both installed |
|---|---|---|
| Windows x64, Arm64 | MSI | **otter-net** (its folder is first in the user PATH; otterPS's is appended), in both orders; after otter-net is uninstalled, otterPS again; after otterPS is uninstalled, otter-net |
| | ZIP | otterPS (the ZIP changes no PATH); deleting the folder is the uninstall |
| Debian 12 x64, arm64 | .deb | **otterPS** - Debian's `~/.profile` puts `~/.local/bin` (otterPS's launcher) before `/usr/bin`; otter-net is `/usr/bin/otter` by full path, and becomes the `otter` once otterPS is uninstalled |
| | .tar.gz | order A: `install.sh` refuses otterPS's launcher unless `--replace-otterps-launcher`; with it, **otter-net**, and uninstalling otter-net puts otterPS's launcher back. Order B: otterPS's `Install-Otter.ps1 -AddToUserPath` **refuses to replace otter-net's launcher** (its own rule: it replaces only its own) - its files install, its command does not; otter-net stays the `otter` |
| macOS arm64, x64 | .pkg | **otter-net** (`/usr/local/bin`), unless the user added otterPS's PATH line to `~/.zshrc` - then **otterPS** (`~/.local/bin` first) |
| | .tar.gz | `~/.local/bin` is not on a new Mac's PATH: neither launcher is found until the user adds the line otterPS (and `install.sh`) print; with it, as on Linux |

Findings for the owner (not fixable inside otter-net's packages without
touching otterPS or users' shell profiles):

- With otterPS installed per user, the **.deb and .pkg do not win** a new
  terminal where `~/.local/bin` is on the PATH (always on Debian/ChromeOS
  for a user whose `~/.local/bin` exists; on macOS once the user follows
  otterPS's instructions). The package README says so and how to check.
- otterPS's installer cannot be run with `-AddToUserPath` after otter-net's
  tarball took `~/.local/bin/otter`: install otterPS first, or without
  `-AddToUserPath`.
- The Windows Store package's `otter.exe` alias (in
  `%LOCALAPPDATA%\Microsoft\WindowsApps`, on the user PATH) was not tested
  (no Store on CI runners); otter-net's MSI folder is first in the user PATH,
  so it should come before the alias.

Run 39645030006 (the commit before) passed everything except the Windows
coexistence job's own check, which counted where.exe's extensionless match
(otterPS's macOS/Linux launcher `otter`, which cmd and PowerShell never
run) as the first `otter`; fixed in the test.

## Decisions for the owner

1. **The version scheme** (above): `0.9.0-preview.N` for previews, and
   what a final otter-net release is called (0.9.x/1.0.0 of its own, or
   Otter 1.0.2/1.1 under otterPS's identities).
2. **Identity at release: beside otterPS or replacing it.** The preview
   deliberately sits beside it. Replacing means taking otterPS's MSI
   UpgradeCode, winget `JeffreyMacy.Otter` and the Store package - and
   deciding what happens to ZIP installs at `%LOCALAPPDATA%\Otter`.
3. **`otter --version`**: keep `Otter 1.0.1` (today; byte-for-byte with
   otterPS), or add the implementation and package version (e.g. a second
   line or `--version --verbose`) - a behaviour change, so a language/CLI
   decision, not packaging.
4. **Desktop runtime size**: the self-contained `otter-desktop.exe` adds
   41-58 MB to each Windows download (see the table). Alternatives: ship
   it framework-dependent and require the .NET 10 Desktop Runtime, or make
   desktop windows an optional MSI feature/download.
5. **WPF resource languages**: the preview carries English WPF resources
   only (as verified so far); including WPF's other languages (measured
   for win-x64: +9.0 MB installed, +3.0 MB in the ZIP) would show WPF's own texts in the user's display language, as
   otterPS on Windows PowerShell does (SPEC-QUESTIONS W5-5).
6. **Public artifacts**: this repository is public, so anyone signed in to
   GitHub can download the preview artifact for 30 days. If that is not
   wanted: make the repository private, or delete the run's artifact
   after the internal testers have it (`gh api -X DELETE
   repos/Heavens-Lava/otter-net/actions/artifacts/<id>`).
7. **Signing**: `docs/SIGNING.md` (costs, recommendation, approvals).
8. **Identifiers above** (`com.otterprogramming.otter-net`, maintainer
   address).
