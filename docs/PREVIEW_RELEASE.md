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
| `otter-net --version` (and `otter-desktop.exe --version`) | `Otter 1.0.1` - unchanged: the **language** version (`Program.Version`, defined by otterPS) |
| package file names, `VERSION` in every install, the `.pkg` | `0.9.0-preview.1` |
| Windows Settings > Apps | "Otter (otter-net) 0.9.0-preview.1", version `0.9.0`, comment "Otter 1.0.1, native implementation (otter-net) - internal preview 0.9.0-preview.1, unsigned. Not an Otter release." |
| `otter-net.exe` / `otter-desktop.exe` file properties | product "Otter (otter-net)", version 0.9.0-preview.1 |
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
| Command | **`otter`** | **`otter-net`** only - no otter-net package installs, replaces or removes an `otter` command |
| Windows MSI UpgradeCode | `F2065F2E-9E7A-405C-810C-3826A1DAAE39` (built, not published) | **`B6DDDF92-EF9A-4FC0-ACA2-E1E3B3413E9B`** |
| Windows install folder | ZIP: `%LOCALAPPDATA%\Otter\1.0.1`; MSI: `%LOCALAPPDATA%\Programs\Otter` | **`%LOCALAPPDATA%\Programs\otter-net`** (MSI); the ZIP runs where it is extracted |
| Windows PATH | its folder **appended** to the user PATH (`Install-Otter.ps1 -AddToUserPath`; its MSI: `Part="last"`) | its folder **appended** to the user PATH (MSI, `Part="last"`); it holds `otter-net.exe` and `otter-desktop.exe`, no `otter`; removed on uninstall; the machine PATH is never touched |
| Windows registry | `HKCU\Software\Otter` (MSI) | **`HKCU\Software\otter-net`** |
| Settings > Apps | "Otter Programming Language" (MSI) | **"Otter (otter-net) 0.9.0-preview.1"** |
| winget | `JeffreyMacy.Otter`, moniker `otter` (manifest written, not submitted) | **none** - not reused, nothing submitted |
| Microsoft Store | `JeffreyMacy.OtterProgrammingLanguage` (published; app execution alias `otter.exe`) | **none** |
| macOS | ZIP: `~/.local/share/otter/1.0.1`, launcher `~/.local/bin/otter`; no pkg | **pkgutil id `com.otterprogramming.otter-net`**, `/usr/local/lib/otter-net/<v>/otter-net`, link `/usr/local/bin/otter-net`; tarball: `~/.local/share/otter-net/<v>`, launcher `~/.local/bin/otter-net` |
| Debian / Chromebook | ZIP as on macOS; no .deb | **dpkg package `otter-net`**, `/usr/lib/otter-net/otter-net`, link `/usr/bin/otter-net`, docs `/usr/share/doc/otter-net` |
| `--version` | `otter --version`: `Otter 1.0.1` | `otter-net --version`: `Otter 1.0.1` (same language) |

No installer of otter-net writes, moves or deletes anything of otterPS's,
and otter-net no longer reads otterPS's installation at all (since
`c474ab8` it never starts PowerShell). The coexistence tests below check
this in both install and uninstall orders.

**`otter` is never otter-net's** (the owner's decision, 2026-10-22):
otter-net is invoked explicitly as `otter-net`, with exactly `otter`'s
arguments (its messages still say `otter`, word for word as Otter 1.0.1).
Installing or uninstalling otter-net does not change which runtime `otter`
selects: otterPS's while it is installed, otherwise none. The executable is
published as `otter` (`otter.exe`) and every package carries it as
`otter-net` (`otter-net.exe`); it locates `otter-desktop.exe` and runs
itself (`otter test`) by its own path, never by name. Earlier preview
builds (before this change) did take `otter`; installing this one undoes
that: the `.pkg` removes an earlier otter-net `/usr/local/bin/otter` link,
`install.sh` gives `~/.local/bin/otter` back to otterPS (or removes it when
that otterPS is gone; `--replace-otterps-launcher` is refused now), and
the MSI's and `.deb`'s upgrades remove the old files themselves.

**Identifiers:** `com.otterprogramming.otter-net` for macOS (confirmed by
the owner, 2026-10-22); the MSI Manufacturer is "Jeffrey Macy" (as otterPS's
MSI). The `.deb` Maintainer is still the GitHub no-reply address
(`114191752+Heavens-Lava@users.noreply.github.com`, which receives no
mail) - **proposed replacement, not applied:** `Jeffrey Macy
<otter-net@otterprogramming.com>`. otterprogramming.com's DNS has mail
forwarding (MX `eforward1-5.registrar-servers.com`, SPF
`include:spf.efwd.registrar-servers.com`, checked 2026-10-22), so a
forwarding address there is project-controlled and keeps the owner's own
address private. Before it goes into `distribution/linux/control`: create
the forward in the registrar's mail-forwarding settings and confirm a test
message arrives. (otterPS's `SECURITY.md` names `security@otter-lang.org`;
otter-lang.org does not resolve, so that address cannot receive mail -
reported to the owner, otterPS unchanged.)

## The packages

Built by `.github/workflows/package.yml` on each platform, kept only from a
manual run or a push to `cert/preview` or `release/0.9.0-preview.1`, after
every test passed, as **one workflow artifact
`otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED`** (30 days) with
`SHA256SUMS` and a `README.txt`. Other runs (main, pull requests) keep
their packages for one day, between jobs only. The repository is public:
anyone signed in to GitHub can download a kept artifact, so it is not
private.

### Run 39881403663 (2026-10-23, commit `f4e5134`, branch `release/0.9.0-preview.1`) - the internal preview

All 22 jobs passed. Artifact **`otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED`**
(artifact id 12471417045, 257 MB zipped, kept until 2026-11-22). This is
the first preview with the separate `otter-net` command; testers'
instructions are in [PREVIEW_TESTING.md](PREVIEW_TESTING.md).

    gh run download 39881403663 -R Heavens-Lava/otter-net -n otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED
    sha256sum -c SHA256SUMS      # macOS: shasum -a 256 -c SHA256SUMS

```
e2fb32251c590c0204a29dd864f2e33ae36873210b3741f38eb90eb06ee57a47  otter-net-0.9.0-preview.1-linux-arm64.tar.gz
afc7a4b75fb5c6d11f7f8cc8629fa9d54c82cffaa1d9626db56246489bbd6574  otter-net-0.9.0-preview.1-linux-x64.tar.gz
4439872cf26fabb963cd480c79f269d77461479d6c44df5f92a33ede8a80d23b  otter-net-0.9.0-preview.1-osx-arm64.pkg
529f20c52887bf2847c5c9712a9cf34683a097e0a27102670ac2e5646b871ce4  otter-net-0.9.0-preview.1-osx-arm64.tar.gz
58f481a1e8daa45c2f0828c81f258471edab96b8d52ba97f668dcdffc3e4c8f5  otter-net-0.9.0-preview.1-osx-x64.pkg
4bfe97b6b07fb65285d7cc52ba923f6de3815543cbedc5b6fe2725754f8bc613  otter-net-0.9.0-preview.1-osx-x64.tar.gz
3ddbdaa4568322e0c65b03e7475696c446181f51e69aa100f7cdade1aa2d0fa4  otter-net-0.9.0-preview.1-win-arm64.msi
4500f4be0defe4b361439fd4e9780b157a1fb7d6fcd215f3a5e44fcbc33603c2  otter-net-0.9.0-preview.1-win-arm64.zip
0ace7679d4d926a552d506aca08826e7577b463e23648c8b573333df0542ea52  otter-net-0.9.0-preview.1-win-x64.msi
215e30805cf33410c8f4f2f7805088b65cc601a36e18a8fceedecf541030f3af  otter-net-0.9.0-preview.1-win-x64.zip
9dc0482ea909181ecbaabd06252700ccc15afcd49e893b12648e5171593ca676  otter-net_0.9.0~preview.1_amd64.deb
46c3ef85791e011e791da7ef9b411ce787ab6a726c9e33c347dba3ed97288cdd  otter-net_0.9.0~preview.1_arm64.deb
```

**Superseded:** run 39747899383 (commit `fbc46a4`) and run 39647277612
(commit `bf21fec`) kept artifacts with the same name and version whose
packages still installed an `otter` command. They are kept for now (the
owner's decision); tell testers to use run 39881403663's, and delete the
older ones when the testers have the new one (`gh api -X DELETE
repos/Heavens-Lava/otter-net/actions/artifacts/12412279631`, and
`.../12368661883`). Installing the new packages over an old one removes
the old `otter` command (tested below).

Builds are not byte-identical: each run's packages have their own checksums.

### The self-contained desktop companion: size

| | framework-dependent `otter-desktop` (main, run 39618736521) | self-contained (run 39647277612) | difference |
|---|---:|---:|---:|
| win-x64 MSI | 4,636,672 | 51,339,264 | +46.7 MB |
| win-x64 ZIP | 5,867,103 | 63,547,918 | +57.7 MB |
| win-arm64 MSI | 4,497,408 | 45,887,488 | +41.4 MB |
| win-arm64 ZIP | 5,570,820 | 58,374,305 | +52.8 MB |
| installed `otter-desktop.exe` (x64 / arm64) | 162,816 + 3 DLLs (~1.2 MB) | 124,046,523 / 139,672,681 | |

Beside it: `D3DCompiler_47_cor3.dll` (x64 only), `PenImc_cor3.dll`,
`PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`.
The MSI (LZX, `CompressionLevel="high"`) is about 12 MB smaller than the
ZIP (deflate). Installed size on Windows: about 145 MB (x64; `otter-net.exe` is about 12 MB of it).

### Desktop windows with no .NET (Windows x64 and Arm64)

`tools/test-desktop-nodotnet.ps1` in job "Desktop windows without .NET",
both architectures passed (run 39881403663; and 39747899383, 39647277612
before the rename). In run 39879518726 (commit `a87ae41`) the Arm64 harness
reported 25 of 26: in `repl_window` UI Automation got a COMException on
**otterPS's** window (the reference), while otter-net's side behaved
correctly; it passed 26 of 26 in the next run. How the runner was made a machine without .NET
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
with the installed `otter-net.exe`, against otterPS 1.0.1: **26 of 26 programs
identical** (window tree after every step, pixels, output, errors, exit
code) on **both x64 and Arm64** (desktop.yml covers x64 only).

### Install, upgrade, downgrade, uninstall, PATH (without PowerShell)

Run 39881403663. Every test also checks that no package installs, replaces
or removes an `otter` command.

| Platform | Package | Result |
|---|---|---|
| Windows x64 (windows-latest), Windows 11 Arm64 | MSI | over 0.8.0: one Apps entry, install folder once and **appended** to the user PATH, machine PATH untouched, `otter-net.exe` and no `otter`; a **new terminal** (registry PATH and a fresh Task Scheduler process) finds `...\Programs\otter-net\otter-net.exe` and **no `otter`**; tests\compat 202 of 202 native; new/run/check/test; uninstall removes files, folder, PATH entry, `HKCU\Software\otter-net`, Apps entry, and a new terminal finds neither command; **downgrade** (over 0.9.1) refused; **over the real pre-rename MSI** (run 39747899383, `otter.exe` first on the PATH): `otter.exe` removed, a new terminal has `otter-net` and no `otter`, one PATH entry, one Apps entry |
| | ZIP | checksums match; `otter-net.exe` runs in place, no `otter.exe` in it |
| Debian 12 x64, arm64 (= Chromebook Linux) | .deb | over 0.8.0; `/usr/bin/otter-net`, no `/usr/bin/otter`; tests/compat 202 of 202; `apt-get remove --purge` leaves nothing; downgrade refused by apt; **over the real pre-rename .deb**: `/usr/bin/otter` removed, `/usr/bin/otter-net` installed |
| | .tar.gz | upgrade, 202 of 202, uninstall leaves nothing; otterPS's `~/.local/bin/otter` untouched by install and uninstall; `--replace-otterps-launcher` refused; an earlier preview's `otter` launcher given back to otterPS (or removed if otterPS is gone); a user's own `otter` link left alone; **over the real pre-rename tarball**: `~/.local/bin/otter` removed, `otter-net` added; downgrade allowed (no check), one version left |
| macOS Apple silicon, Intel | .pkg | refuses another program's `/usr/local/bin/otter-net`; leaves another program's `/usr/local/bin/otter` alone; removes an earlier preview's `/usr/local/bin/otter` link; over 0.8.0 leaves one version; 202 of 202; `uninstall.sh` leaves nothing; downgrade allowed (macOS Installer has no check); **over the real pre-rename .pkg**: `/usr/local/bin/otter` removed, `/usr/local/bin/otter-net` linked |
| | .tar.gz | as on Linux, including the real pre-rename tarball |

Signing rehearsal (same run): `otter-net.exe`, `otter-desktop.exe` and the
MSI signed on Windows x64 and Arm64 with a throwaway self-signed
certificate, timestamped by DigiCert, verified and discarded; `otter-net`
signed ad hoc with the Hardened Runtime on macOS arm64 and x64, runs, and
is what the `.tar.gz` and `.pkg` carry ([SIGNING.md](SIGNING.md)).

### Beside otterPS 1.0.1

otterPS's release ZIP was built in the run by its own
`tools/New-OtterDistribution.ps1` from the `v1.0.1` tag (Windows PowerShell
5.1, as released) and installed with its own `Install-Otter.ps1
-AddToUserPath` (Windows PowerShell 5.1 on Windows; PowerShell 7.4.6 on
Linux and macOS). Order A: otterPS, then otter-net; remove otter-net, then
otterPS. Order B: otter-net, then otterPS; remove otterPS, then otter-net.
At every step a new terminal's `otter` and `otter-net` were both checked;
both products worked through their own paths, removing one left the
other's files (hashed) and command intact, and nothing was left at the end.
otterPS's installer now also works after otter-net (order B) on every
platform.

| Platform | Package | A new terminal's `otter` | A new terminal's `otter-net` |
|---|---|---|---|
| Windows x64, Arm64 | MSI | otterPS while it is installed, otherwise none - in both orders, before and after otter-net's install and uninstall | otter-net while it is installed, otherwise none |
| | ZIP | otterPS (the ZIP changes no PATH) | none (run in place) |
| Debian 12 x64, arm64 | .deb, .tar.gz | otterPS while it is installed, otherwise none | otter-net (`/usr/bin/otter-net`, `~/.local/bin/otter-net`) while installed |
| macOS arm64, x64 | .pkg | none, or otterPS once the user adds otterPS's PATH line to `~/.zshrc` (macOS has no `~/.local/bin` on the PATH) | otter-net (`/usr/local/bin/otter-net`) while installed |
| | .tar.gz | as the .pkg | otter-net once `~/.local/bin` is on the PATH (`install.sh` prints the line) |

Not covered by CI: the Microsoft Store package's `otter.exe` alias (no
Store on the runners). otter-net installs no `otter`, so it cannot come
between that alias and the user.

## The owner's decisions (2026-10-22)

1. **Internal preview approved** for trusted testing
   ([PREVIEW_TESTING.md](PREVIEW_TESTING.md): installation and feedback).
2. **Version:** `0.9.0-preview.1` is the native-engine product version;
   the language stays Otter 1.0.1 (`--version` prints `Otter 1.0.1`).
3. **Separate from otterPS:** its installer, Store and winget identities and
   its stable `otter` command are not overwritten; otter-net is the
   `otter-net` command.
4. **The self-contained Windows desktop companion stays.**
5. **Signing:** prepare the Windows and Apple infrastructure, buy nothing
   and sign up for nothing yet ([SIGNING.md](SIGNING.md)).
6. **`com.otterprogramming.otter-net`** for macOS; a verified
   project-controlled Debian maintainer address is proposed above, to be
   confirmed before it is used.
7. **The internal-preview artifact is kept for now.** It is unsigned and
   publicly downloadable (public repository): never described as private
   or production-ready.
8. Merge into `main` after the final review, once the intended changes and
   the required checks are verified. No public release, package
   publication, tag, Store or winget submission, or expense.

Still open: WPF's resource languages (English only today; other
languages +9.0 MB installed for win-x64), and `otter-net --version` showing
the product version (a CLI behaviour change, so a language/CLI decision).
