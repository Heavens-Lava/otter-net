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

RESULTS_PLACEHOLDER

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
   about 55 MB to each Windows download (see the table). Alternatives: ship
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
