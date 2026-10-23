# otter-net 0.9.0-preview.1 - review package

**For:** the project owner. **Nothing is published:** no release, tag,
package registry, winget or Store submission; otterPS 1.0.1 is untouched;
no money spent.

## Status

**The internal preview is approved (2026-10-22) and ready for trusted
testers.** A public preview stays blocked until the packages are
code-signed and notarized (section 5).

The preview packages are **unsigned** and **not production-ready**, and the
download is **not private**: it is a workflow artifact of a public
repository, so anyone signed in to GitHub can download it while it is kept.

## 1. The tested commit

**`f4e513431b23d64898ac44543f057dc5699952a0`** on `release/0.9.0-preview.1`.

It is `main` at `c474ab8` (the native port, with the PowerShell fallback
removed) plus:

- the WinRM certification fixes;
- the preview packaging;
- the certification tooling;
- the owner's 2026-10-22 decisions: the separate `otter-net` command and
  the signing hooks.

The documentation commit after it changes no code or tests.

## 2. Certification results (all on `f4e5134`)

| Suite | Run | Result |
|---|---|---|
| Release certification, six platforms (`certify.yml`) | 39881403736 | **every gate passed on win-x64, win-arm64, osx-arm64, osx-x64, linux-x64 and linux-arm64** (details below) |
| CI (`ci.yml`) | 39887986894 | passed |
| Desktop windows (`desktop.yml`) | 39887989710 | passed |
| Remote commands on an isolated WinRM endpoint (`winrm.yml`) | 39881403580 | passed: 12/12 cases, plus the WinRM-stopped, unresolvable-host and timeout cases; identical to otterPS's `Invoke-Command` |
| Packages, clean installs, upgrades, coexistence, signing rehearsal (`package.yml`) | 39881403663 | 22/22 jobs passed |

What the certification run checked on every platform:

- **build:** 0 warnings.
- **conformance:** 19 programs.
- **programs:** 271, identical to otterPS 1.0.1 on Windows PowerShell 5.1.
- **network:** 22 programs.
- **CLI:** 398 cases.
- **web:** 363 pages, byte for byte.
- **fuzz:** 512 programs.
- **real projects and examples:** 84 cases.
- **website:** 218 of 218 files on win-x64, 101 elsewhere.
- **no implicit PowerShell:** 0 launches in 1,867 runs.
- **native coverage:** passed.
- **desktop windows:** 26/26 on Windows x64 and Arm64.
- **Windows machine state:** 32 programs.
- **repository:** left clean.
- **every platform against Windows:** 0 unexpected differences. The
  declared differences, each with its reason: win-arm64 9, macOS 50,
  Linux 51.

The record is in `release/certification/f4e513431b23d64898ac44543f057dc5699952a0.md`,
with the `.json` and the per-platform comparisons.

## 3. Remaining issues by severity

| Severity | Issue |
|---|---|
| **Blocker (public preview only)** | Unsigned packages. SmartScreen and antivirus products may warn. Gatekeeper blocks the `.pkg` from Finder and quarantines downloaded executables. The signing infrastructure is ready but not active (section 5). |
| Major | None. |
| Minor | The `.deb` maintainer is still the GitHub no-reply address. A replacement is proposed in section 6. |
| Resolved | The two earlier preview artifacts (runs 39747899383 and 39647277612, same name and version, packages that installed `otter`) were deleted on 2026-10-23; run 39881403663's is the only one. Installing over an old package removes its `otter` (tested). |
| Minor | Windows downloads are 41-58 MB larger because the desktop companion is self-contained. This is the owner's choice. |
| Minor | The macOS `.pkg` and the tarballs allow a downgrade. The MSI and `.deb` refuse one. |
| Minor | Remote commands are not tested against a second machine, in a domain (Kerberos), or over HTTPS. |
| Minor | The fuzz suite runs 512 programs per platform. otterPS's own certification ran 1,000 + 1,000. |
| Minor | Not verifiable on hosted runners: a session started at high DPI, and the Store package's `otter` alias. otter-net installs no `otter`, so it can never shadow that alias. |
| Minor | In one run (39879518726), the Arm64 desktop harness reported 25/26. UI Automation failed on **otterPS's** window (the reference), not on otter-net's. It passed 26/26 on the next run. |
| Specification | 58 open questions in `docs/SPEC-QUESTIONS.md`. None blocks a preview. |

## 4. Installers: `otter` stays otterPS's

What the packages install:

- **Every package installs only the `otter-net` command.** It takes
  exactly `otter`'s arguments and prints Otter 1.0.1's messages word for
  word.
- **No package installs, replaces or removes `otter`.** Installing or
  uninstalling otter-net never changes which runtime `otter` selects.

Tested in run 39881403663 on clean machines without PowerShell (Windows x64
and Arm64, Debian 12 x64 and Arm64, macOS arm64 and x64):

- **Install, upgrade, uninstall.** Covered for the MSI, ZIP, `.pkg`, `.deb`
  and tarballs. tests/compat ran 202 of 202 natively. `otter-net new`,
  `run`, `check` and `test` work. Uninstalling leaves nothing behind.
- **Downgrade.** Refused by the MSI and `apt`; allowed by the `.pkg` and
  the tarballs.
- **Over the real earlier preview** (run 39747899383, which installed
  `otter`). On all six platforms, installing this preview removes that
  `otter` and installs `otter-net`:
  - MSI: `otter.exe` removed; a new terminal has `otter-net` and no
    `otter`.
  - `.deb`: `/usr/bin/otter` removed.
  - `.pkg`: the `/usr/local/bin/otter` link removed.
  - Tarball: the `~/.local/bin/otter` launcher removed.
- **Beside otterPS 1.0.1**, built from its tag and installed with its own
  installer. Checked in both install orders and both uninstall orders, at
  every step:
  - A new terminal's `otter` is otterPS's while otterPS is installed
    (on macOS, once its PATH line is added), and otherwise none. It is
    never otter-net's.
  - `otter-net` is otter-net's while otter-net is installed.
  - Neither product touches the other's files.
  - otterPS's installer now also works after otter-net on every platform.
- **No .NET installed.** The self-contained `otter-desktop.exe` ran desktop
  windows from the installed MSI and from the ZIP, 26/26 identical on x64
  and Arm64.

## 5. Signing: prepared, not active

Nothing was bought or signed up for. Details are in `docs/SIGNING.md`.

What is in place:

- **`tools/sign.ps1`** supports four providers:
  - Azure Artifact Signing
  - `.pfx`
  - Apple Developer ID, with notarization and stapling
  - ad hoc
- **`tools/package.ps1 -Sign`** signs the executables before they are
  packed, and the MSI or `.pkg` after it is built.
- **A rehearsal runs in every `package.yml` run:**
  - Windows x64 and Arm64: `otter-net.exe`, `otter-desktop.exe` and the
    MSI are signed with a throwaway self-signed certificate, timestamped
    and checked.
  - macOS: `otter-net` is signed ad hoc with the Hardened Runtime, and the
    signed executable runs.
  - The results are discarded.

**To turn it on**, after approval:

1. The accounts: Azure Artifact Signing Basic at US$9.99/month
   (individuals in the US or Canada only; fallback SSL.com, about
   US$310/year), and the Apple Developer Program at US$99/year. About
   **US$220/year** in total.
2. A protected `signing` environment with the owner as required reviewer.
3. The workflow steps given in `docs/SIGNING.md`.

## 6. Identity

- **Version:** `0.9.0-preview.1`. `otter-net --version` prints
  `Otter 1.0.1`, the language version.
- **Windows:** MSI UpgradeCode `B6DDDF92-EF9A-4FC0-ACA2-E1E3B3413E9B`,
  installed to `%LOCALAPPDATA%\Programs\otter-net`.
- **macOS:** `com.otterprogramming.otter-net` (confirmed).
- **Debian:** package `otter-net`.
- **Proposed `.deb` maintainer:** `Jeffrey Macy <otter-net@otterprogramming.com>`.
  otterprogramming.com already forwards mail through the registrar's MX
  servers. To confirm it:
  1. Create the forward.
  2. Send a test message and check it arrives.
  3. Then the address goes into `distribution/linux/control`.

Separately, otterPS's `SECURITY.md` names `security@otter-lang.org`, but
otter-lang.org does not resolve. That address cannot receive mail.
otterPS was left unchanged.

## 7. For testers

- **Download:** <https://github.com/Heavens-Lava/otter-net/actions/runs/39881403663>,
  artifact `otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED` (id
  12471417045, 257 MB). It is kept until 2026-11-22.
- **Instructions and feedback checklist:**
  [PREVIEW_TESTING.md](PREVIEW_TESTING.md), covering Windows, macOS and
  Linux/Chromebooks.
- **Checksums:** in [PREVIEW_RELEASE.md](PREVIEW_RELEASE.md).
