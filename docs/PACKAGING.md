# Packaging otter-net

Status (2026-10-21): **built and tested, not published.** The current
packages are an unsigned **internal preview**, `0.9.0-preview.1`: its
identity, version, results and the owner's decisions are in
[PREVIEW_RELEASE.md](PREVIEW_RELEASE.md); the signing plan is in
[SIGNING.md](SIGNING.md). Nothing has been released, tagged, uploaded to a
package registry or submitted to winget or the Microsoft Store, and the
current Otter release (otterPS 1.0.1) is unchanged.

## Version

`VERSION` at the root of the repository is the one source: `0.9.0-preview.1`
(why: [PREVIEW_RELEASE.md](PREVIEW_RELEASE.md#version)).
MSBuild reads it (`Directory.Build.props`: assembly and file version,
company, product), and `tools/package.ps1` reads it for every file name and
installer. `otter --version` keeps printing the Otter **language** version,
`Otter 1.0.1` (`Program.Version`, defined by otterPS), and `package.ps1`
checks that it does.

| Format | Version as written |
|---|---|
| file names, `VERSION` in a package, `.pkg` | `0.9.0-preview.1` |
| MSI ProductVersion (numbers only) | `0.9.0`; previews of one version upgrade each other (`AllowSameVersionUpgrades`) |
| `.deb` | `0.9.0~preview.1` (sorts before `0.9.0`) |

What the version says it is (`preview` -> "internal preview", `rc` ->
"release candidate") is shown by every installer (`package.ps1`'s
`$channel`).

## The packages

`pwsh tools/package.ps1 -Runtime <rid> [-Installer]` builds the native `otter`
(Native AOT, `-p:UseAppHost=true`) and writes to `artifacts/packages/`
(git-ignored). Every package has a `<file>.sha256`, and `SHA256SUMS` lists
every package in the folder. Archives also carry a `SHA256SUMS` of their own
files.

| Runtime | Archive | Installer (`-Installer`, built on that platform) |
|---|---|---|
| `win-x64`, `win-arm64` | `otter-net-<v>-<rid>.zip` | `otter-net-<v>-<rid>.msi` (WiX 5) |
| `osx-arm64`, `osx-x64` | `otter-net-<v>-<rid>.tar.gz` | `otter-net-<v>-<rid>.pkg` (pkgbuild + productbuild) |
| `linux-x64`, `linux-arm64` (also Chromebooks) | `otter-net-<v>-<rid>.tar.gz` | `otter-net_<v>_amd64.deb`, `otter-net_<v>_arm64.deb` (dpkg-deb) |

What every package carries: the native `otter` (`otter.exe`), `LICENSE`
(Otter Free Use License 1.1), `README.md` (`distribution/package/README.md`:
what this is and how to install and uninstall it), `VERSION` and
`THIRD-PARTY-NOTICES.txt`. The notices are needed: a Native AOT executable
contains the .NET runtime (MIT), so the file holds .NET's MIT license and
the .NET runtime's own third-party notices, taken from the Native AOT
compiler package the build used (`microsoft.dotnet.ilcompiler/<version>/THIRD-PARTY-NOTICES.TXT`).
otter-net has no NuGet dependencies of its own.

On Windows, `otter-desktop.exe` (desktop windows, WPF) is in the zip and the
MSI, **self-contained**: `package.ps1` publishes `src/Otter.Desktop` for the
same runtime with `--self-contained true -p:PublishSingleFile=true`, not
trimmed (WPF does not support trimming), not compressed, English resources
only; WPF's five native libraries (`D3DCompiler_47_cor3.dll`,
`PenImc_cor3.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`,
`wpfgfx_cor3.dll`) stay beside it, where its host loads them (nothing is
extracted to `%TEMP%`). No .NET runtime is needed on the machine. It
refuses a framework-dependent result, and adds the Windows Desktop
runtime's license to `THIRD-PARTY-NOTICES.txt`. Size: see
[PREVIEW_RELEASE.md](PREVIEW_RELEASE.md#the-packages).

### Windows: `.zip` and `.msi`

- **zip**: `otter-net-<v>-<rid>/` with `otter.exe`, `otter-desktop.exe` and
  its `*_cor3.dll`, and the documents; run in place, no installation.
- **msi** (`distribution/windows/Package.wxs`, `otter-net.wixproj`, WiX 5 SDK
  from NuGet; not in `otter-net.sln`):
  - per user, no administrator rights: `%LOCALAPPDATA%\Programs\otter-net`;
  - that folder is put **first** on the user's PATH, so a new terminal's
    `otter` is otter-net's even when otterPS's folder is on the PATH too;
  - `HKCU\Software\otter-net` (InstallFolder, Version, one value per file:
    a per-user component needs a registry key path, ICE38);
  - one Settings > Apps entry, "Otter (otter-net) <v>";
  - uninstall removes the files, the folder, the PATH entry, the registry
    key and the Apps entry;
  - upgrade: its own UpgradeCode `B6DDDF92-EF9A-4FC0-ACA2-E1E3B3413E9B`; a
    newer otter-net MSI replaces the older one, a downgrade is refused
    ("A newer version of otter-net is already installed.");
  - the cabinet is compressed with `CompressionLevel="high"`.
  - Windows Installer validation (ICE) runs with every build; ICE91 (per-user
    files on a per-machine install, not applicable) and ICE61 (same-version
    upgrades, on purpose) are suppressed.

### macOS: `.pkg` and `.tar.gz`

- **pkg** (`distribution/macos/`), unsigned:
  - installs `/usr/local/lib/otter-net/<v>/` (otter, documents,
    `uninstall.sh`, an `.otter-net-install` marker); `postinstall` links
    `/usr/local/bin/otter` to it. The payload holds only otter-net's own
    folder, so the package never takes over `/usr/local/bin`'s owner and
    permissions (Homebrew's, on Intel Macs);
  - `preinstall` stops the installation, changing nothing, when
    `/usr/local/bin/otter` exists and is not otter-net's link;
  - upgrade: `postinstall` removes older otter-net version folders (macOS
    Installer never removes an older package's files);
  - uninstall: `sudo /usr/local/lib/otter-net/<v>/uninstall.sh` (the link,
    the folder, and `pkgutil --forget com.otterprogramming.otter-net`);
  - needs an administrator password; macOS 12 or later (the executable's
    minimum); `hostArchitectures` is the package's own processor.
- **tar.gz**, Homebrew-style (`bin/otter` in a versioned folder) and per user,
  like Linux below.

### Linux and Chromebooks: `.deb` and `.tar.gz`

- **deb** (`distribution/linux/control`): package `otter-net`,
  `/usr/lib/otter-net/otter` (+ `VERSION`), `/usr/bin/otter` link,
  `/usr/share/doc/otter-net/` (copyright with the license, README,
  notices). `Depends` is computed from the executable: `libc6 (>= <newest
  GLIBC_ symbol it uses>)`, the libraries it links, and `libssl3 | libssl3t64`
  (OpenSSL is loaded at run time for HTTPS, TLS and hashing); `Recommends:
  libsqlite3-0, ca-certificates`. Built on Debian 12 like ci.yml's Linux
  executables, so it installs on Debian 12 (Chromebooks' Linux) and newer.
  Install `sudo apt install ./otter-net_*.deb` (on a Chromebook: double-click
  it in Files); uninstall `sudo apt remove otter-net`; a newer `.deb`
  upgrades it.
- **tar.gz** (`distribution/unix/install.sh`, `uninstall.sh`), per user, no
  administrator rights, mirroring otterPS's Install-Otter.ps1 layout:
  - files in `${XDG_DATA_HOME:-~/.local/share}/otter-net/<v>/` (otterPS
    uses `.../otter/<v>/`, which otter-net looks in for the PowerShell
    Otter, so the two never share a folder), with an `.otter-net-install`
    marker written first;
  - a launcher `~/.local/bin/otter` (`exec '<folder>/otter' "$@"`, marked on
    its second line like otterPS's); if `~/.local/bin` is not on the PATH
    the script prints the line to add, and changes no shell profile;
  - an otterPS launcher there is replaced only with
    `--replace-otterps-launcher`, kept, and put back by `uninstall.sh`; any
    other `otter` is never replaced;
  - installing a newer version removes the older otter-net versions (marked
    folders only); the same version again needs `--force`;
  - `uninstall.sh` removes the folder, the launcher (when it is otter-net's)
    and `~/.local/share/otter-net` when empty; it puts otterPS's launcher
    back only while the otterPS installation it runs still exists (otherwise
    it would leave a dangling `otter`);
  - installing an older version over a newer one is allowed and leaves only
    the one installed (no downgrade check).

## How this relates to otterPS's installers and identities

| | otterPS (released 1.0.1 and unpublished MSI) | otter-net release candidate |
|---|---|---|
| Release ZIP install (Windows) | `%LOCALAPPDATA%\Otter\<v>\otter.cmd`, user PATH | zip runs in place; MSI below |
| Release ZIP install (Linux, macOS) | `~/.local/share/otter/<v>/`, `~/.local/bin/otter` launcher | `~/.local/share/otter-net/<v>/`, the same launcher path (see above) |
| MSI (otterPS's: built and tested, not published) | `%LOCALAPPDATA%\Programs\Otter`, `HKCU\Software\Otter`, UpgradeCode `F2065F2E-9E7A-405C-810C-3826A1DAAE39` | `%LOCALAPPDATA%\Programs\otter-net`, `HKCU\Software\otter-net`, UpgradeCode `B6DDDF92-...` |
| winget | `JeffreyMacy.Otter`, moniker `otter` (manifest written, not submitted) | none - not reused |
| Microsoft Store | `JeffreyMacy.OtterProgrammingLanguage` (published) | none - not reused |
| macOS / Debian identifiers | none | `com.otterprogramming.otter-net` (.pkg), `otter-net` (.deb) |

The two can be installed side by side: otter-net's installers never touch
otterPS's folders, registry key or upgrade code, and otter-net never starts
PowerShell (since `c474ab8`). With both on the PATH, otter-net's MSI comes
first; the tarball takes `~/.local/bin/otter` only when asked; the `.deb`
(`/usr/bin`) and `.pkg` (`/usr/local/bin`) come after otterPS's
`~/.local/bin/otter` whenever that folder is on the PATH. The coexistence
tests (`tools/test-coexistence.*`) check every case in both install and
uninstall orders; results in [PREVIEW_RELEASE.md](PREVIEW_RELEASE.md).

## To decide before publishing

1. **Code signing** (the blocker). Symantec quarantined the first unsigned
   otter-net `otter.exe` (Heur.AdvML.D, 2026-10-15); `docs/ARCHITECTURE.md`
   phase 5 and otterPS's `docs/OTTER_WINDOWS_INSTALLER.md` list the options
   (Azure Trusted Signing, an OV certificate). Sign `otter.exe`,
   `otter-desktop.exe` and the MSI. On macOS an Apple Developer ID
   (US$99/year) is needed to sign the executable and the `.pkg`
   (`productsign`) and to notarize them; unsigned, Gatekeeper blocks the
   `.pkg` when opened from Finder and a downloaded `otter` is quarantined
   (`xattr -d com.apple.quarantine` works around it, which users should not
   be asked to do). A `.deb` is normally unsigned; an apt repository would
   need its own key.
2. **Identity: replace otterPS or sit beside it.** Decide whether otter-net
   ships as the next Otter (1.0.2 or 1.1) under otterPS's identities - the
   MSI UpgradeCode `F2065F2E-...` (then its MSI replaces otterPS's MSI, and
   should also remove or leave the ZIP install at `%LOCALAPPDATA%\Otter`),
   winget `JeffreyMacy.Otter`, the Store package - or under new ones as
   here. The release candidates deliberately use new identities so nothing
   published is replaced by accident.
3. ~~The PowerShell Otter for what is still handed over~~ - resolved:
   otter-net runs everything natively (desktop windows in the
   self-contained `otter-desktop.exe`, remote commands in C#).
4. **The version** otter-net is released as, and what `otter --version`
   prints then (today the language version, `Otter 1.0.1`); the preview's
   proposal is in [PREVIEW_RELEASE.md](PREVIEW_RELEASE.md).
5. **macOS package identifier** `com.otterprogramming.otter-net` and the
   `.deb` maintainer address (the GitHub no-reply address) - placeholders to
   confirm.
6. **Workflow artifacts on a public repository.** This repository is public:
   anyone signed in to GitHub can download the release-candidate artifacts
   while they are kept (now: only manual and `cert/preview` runs keep the
   preview artifact, 30 days; others one day between jobs). otterPS's MSI
   workflow kept only logs for that reason. If that is not wanted, make the
   repository private or delete the run's artifact after review.

## Building locally

Needs the .NET 10 SDK and PowerShell 7, plus:

- **Windows**: Visual Studio's C++ build tools (Native AOT's linker); the
  WiX SDK comes from NuGet. `pwsh tools\package.ps1 -Runtime win-x64 -Installer`.
  (Native executables are not built on the owner's Windows machine until
  they can be signed; CI builds them.)
- **macOS**: Xcode command line tools. Use Microsoft's .NET SDK
  (`dotnet-install.sh`), **not Homebrew's**: Homebrew's source-built SDK
  links the executable against OpenSSL (`-lssl`), which fails without it and
  would tie the executable to Homebrew. Homebrew's `pwsh` also sets
  `DOTNET_ROOT` to Homebrew's .NET; set it back inside the command:

      pwsh -NoProfile -c '$env:DOTNET_ROOT="<sdk folder>"; ./tools/package.ps1 -Runtime osx-arm64 -Installer'

  The `.pkg` needs macOS (`pkgbuild`, `productbuild`).
- **Linux**: `clang zlib1g-dev binutils` and `dpkg-deb` (Debian 12 for
  release builds, as in CI).

Testing (`tools/test-package.sh`, POSIX `sh`, no PowerShell;
`tools/test-package.ps1` on Windows):

    sh tools/test-package.sh tarball artifacts/packages/otter-net-*.tar.gz   # temporary HOME, safe anywhere
    sh tools/test-package.sh pkg-payload artifacts/packages/otter-net-*.pkg   # no installation
    sh tools/test-package.sh deb <deb> [<older deb> [<newer deb>]]            # root, disposable container
    sh tools/test-package.sh pkg <pkg> [<older pkg> [<newer pkg>]]            # sudo, disposable Mac
    sh tools/test-coexistence.sh tarball|deb|pkg <package> <otter-1.0.1.zip>   # beside otterPS; disposable machine

On Windows (disposable runner): `tools/test-package.ps1 -Msi -OlderMsi
-NewerMsi -Zip` (install, upgrade, PATH, a new terminal through a fresh Task
Scheduler process, uninstall, downgrade refused, the ZIP),
`tools/test-desktop-nodotnet.ps1` (desktop windows from the installed
package with no .NET findable) and `tools/test-coexistence.ps1` (beside
otterPS).

Each runs the installed `otter` with `PATH=/usr/bin:/bin:/usr/sbin:/sbin`
plus its own folder, and fails if `pwsh` or `powershell` is found there:
`otter --version`, every `tests/compat` program (none may say it needs the
PowerShell Otter, crash, or print an unhandled exception), `otter new console
demo` then `run`, `check` and `test`, and startup time. With
`OTTER_TEST_REFERENCE=<folder of tools/capture.ps1 outputs>` it also
compares each program's output.

## Test results

The preview's results (run ids, sizes, checksums) are in
[PREVIEW_RELEASE.md](PREVIEW_RELEASE.md). The release-candidate results
below (`1.0.1-rc.1`, framework-dependent `otter-desktop`) are kept for
history.

### CI: `.github/workflows/package.yml` (1.0.1-rc.1)

Run **39183118927** (2026-10-19, commit "tools: test-package.ps1 counts one
Apps entry in Windows PowerShell 5.1 too"): all 12 jobs passed - 6 builds,
6 clean installs. Earlier runs 39180426607, 39181286933 and 39182152692
failed only on the test script's own check of the Windows Apps entry (a
per-user MSI is registered in Windows Installer's UserData, and a single
PSCustomObject has no `Count` in Windows PowerShell 5.1); every other check
passed in them too.

| Clean machine | Package(s) | Result |
|---|---|---|
| Windows Server (windows-latest), x64 | MSI over an older MSI, then ZIP | installed per user; one Apps entry, install folder once and first on the user PATH; a new terminal's `otter` is the installed one with no `powershell.exe`/`pwsh.exe` on its PATH; tests/compat 184 of 188 native, the other 4 (desktop windows, remote commands) handed over as on every Windows and stop with "needs the PowerShell Otter" since none is installed; `otter new/run/check/test`; uninstall left nothing (files, folder, PATH entry, `HKCU\Software\otter-net`, Apps entry); ZIP checksums match and `otter.exe` runs in place |
| Windows 11 on Arm (windows-11-arm) | the same, win-arm64 | the same |
| Debian 12 container, x64 (= Chromebook Linux) | `.deb` over an older `.deb`, then `.tar.gz` | `apt-get install` pulled only what `Depends` names (`libc6 (>= 2.34), libssl3 \| libssl3t64`); tests/compat 188 of 188 native; new project; `apt-get remove --purge` left nothing; tarball install/upgrade/uninstall, otterPS-launcher handling: all pass |
| Debian 12 container, arm64 | the same, arm64 | the same |
| macOS (macos-latest), Apple silicon | `.pkg` over an older `.pkg`, then `.tar.gz` | refuses another program's `/usr/local/bin/otter`; upgrade leaves one version; tests/compat 188 of 188 native (PowerShell removed from the test PATH); new project; `uninstall.sh` left nothing and forgot the receipt; tarball as on Linux |
| macOS Intel (macos-15-intel) | the same, x64 | the same |

The packages of run 39183118927, kept as the workflow artifacts
`otter-net-1.0.1-rc.1-<rid>-UNSIGNED-release-candidate` until 2026-11-02
(checksums verified after download):

| File | Bytes | SHA-256 |
|---|---:|---|
| `otter-net-1.0.1-rc.1-win-x64.msi` | 3,915,776 | `44948329586bc5b720af3e42a58d8b8f00ce86f5632d3d0afd55e33d3903b7af` |
| `otter-net-1.0.1-rc.1-win-x64.zip` | 4,993,802 | `7705108653eb11b57a6ec0afc240b14eb6e4e90bd9c227c4ad04fdc708e4ff8e` |
| `otter-net-1.0.1-rc.1-win-arm64.msi` | 3,796,992 | `03ced6feece48e7fe72a4e7018894cef2bea9e4420b548ea8eb23c6d7d04a300` |
| `otter-net-1.0.1-rc.1-win-arm64.zip` | 4,722,904 | `df4695e23381544f65434587a3f148b837577466ff71c5a789f904435fa0bc50` |
| `otter-net-1.0.1-rc.1-osx-arm64.pkg` | 4,783,026 | `ff976b8ff4a6cb05db94c6ac131eca4ceedbffcea1b1b9056df909ebc8c63529` |
| `otter-net-1.0.1-rc.1-osx-arm64.tar.gz` | 4,777,350 | `3fd20f4eab02e20a175bc019289e9506c7372d60777600f8c750d8766bba0808` |
| `otter-net-1.0.1-rc.1-osx-x64.pkg` | 5,243,048 | `cbe272c1999bec967c4776c5e7486d15fb4ecdadec3caf51ad410ab6a62051ec` |
| `otter-net-1.0.1-rc.1-osx-x64.tar.gz` | 5,240,677 | `c3f26d871b2ff0ca82550c15e0826d10ee3ad70c471b1222dc16ddb7334f4319` |
| `otter-net_1.0.1~rc.1_amd64.deb` | 4,310,892 | `2183ab6e7d5020c0f156070c224028ad1ebd5e375801787b0b7780c2394cbcd8` |
| `otter-net-1.0.1-rc.1-linux-x64.tar.gz` | 5,537,164 | `dba9d679a91ac5c3a820edcdb1acb560dc62a413a9eccb0d3d86af2c4ac8b948` |
| `otter-net_1.0.1~rc.1_arm64.deb` | 3,717,540 | `3e1b1b7fb368079429573782b2b5ea7c9f34f338ffa7c855b669d197be1bd3dc` |
| `otter-net-1.0.1-rc.1-linux-arm64.tar.gz` | 5,177,040 | `9a31c85d30e80b0f65a2b79324384db84fe9166800850c9b63d7c47204db6c28` |

Builds are not byte-identical: each run's packages have their own checksums.

In every row `output_before_hang.ot` (a program that never ends, on purpose)
is stopped by the 60-second limit and counted as native.

Startup, `otter --version`, median of 11 runs, measured from the test
script (includes starting `env`): Debian x64 9 ms, Debian arm64 5 ms, macOS
Apple silicon 16 ms, Windows x64 14 ms, Windows on Arm 19 ms (run
39180426607).

### Locally on this Mac (Apple M4, macOS 26), osx-arm64

Built with Microsoft's .NET SDK 10.0.402 in a scratch folder (see above):
`artifacts/packages/` (git-ignored):

| File | Bytes | SHA-256 |
|---|---:|---|
| `otter-net-1.0.1-rc.1-osx-arm64.tar.gz` | 4,777,766 | `151f4e7405cf8c120f9e69fb8f8859f103e8fe5560d2e3ac560fd78cc7fa5fda` |
| `otter-net-1.0.1-rc.1-osx-arm64.pkg` | 4,784,277 | `90879c1a8605710c87adecce90c971106cc7e3cce5e80224f9fc3376798d490c` |

The executable is 11,085,784 bytes, links only system libraries and
frameworks (`otool -L`), minimum macOS 12.0.

- **The `.pkg` was not installed** on this Mac: installing needs `sudo` and
  writes to `/usr/local`. It was tested with `tools/test-package.sh
  pkg-payload`: `pkgutil --expand-full`, the scripts parse, and the payload's
  `otter` passes all checks with `PATH=/usr/bin:/bin:/usr/sbin:/sbin` (no
  PowerShell): tests/compat 188 of 188 native; `otter --version`, `new`,
  `run`, `check`, `test`. Real installation, upgrade and uninstall of the
  `.pkg` were tested on CI's disposable Macs (above).
- **The `.tar.gz` was installed** into a temporary `HOME` (`tools/test-package.sh
  tarball`): upgrade, otterPS-launcher handling, 188 of 188 native,
  uninstall left nothing.
- Outputs compared (`OTTER_TEST_REFERENCE`, `TZ=UTC`) with CI run
  39158442548's capture of the same programs on macOS: identical except
  `net_jobs.ot`, which starts `net_job_helper.ps1` as an outside program and
  so needs PowerShell on the machine - expected without it. Against otterPS
  1.0.1's Windows output, only the differences ci.yml already declares for
  macOS, plus `net_jobs.ot`.
- Startup of the packaged executable, run directly (perl `system`, 21
  runs): `otter --version` median 17.2 ms, `otter run hello.ot` median
  17.7 ms (the very first run of a new executable took 572 ms: macOS
  scanning it once).
- A `.pkg` built in this session carries empty `._` entries: macOS tags files
  written by the sandboxed session with `com.apple.provenance`, which cannot
  be removed. Harmless (they become that attribute again on install), and
  absent from CI's build; `package.ps1` warns when it happens.
