# Testing the otter-net internal preview

For trusted testers of **otter-net 0.9.0-preview.1**: the native (C#)
implementation of the Otter 1.0.1 language. Thank you for trying it.

**Please read first.**

- This is an **internal preview, unsigned, and not production-ready.** Use
  it on a machine where an unexpected warning or a bug costs nothing; don't
  rely on it for real work yet.
- **The download is not private.** It is a GitHub Actions artifact of a
  public repository: anyone signed in to GitHub can download it while it is
  kept (until 2026-11-22). Please don't pass the link on.
- It installs a separate command, **`otter-net`**. It never installs,
  replaces or removes `otter`: if you have the released Otter (otterPS
  1.0.1), `otter` keeps running it, before, during and after this test.
- `otter-net --version` prints `Otter 1.0.1` - the **language** version it
  implements. The preview's own version, 0.9.0-preview.1, is in the file
  names and in the `VERSION` file.

Built from commit `f4e513431b23d64898ac44543f057dc5699952a0` (branch `release/0.9.0-preview.1`).

## 1. Download and check

1. Sign in to GitHub and open
   <https://github.com/Heavens-Lava/otter-net/actions/runs/39881403663>.
2. Under **Artifacts**, download
   `otter-net-0.9.0-preview.1-INTERNAL-PREVIEW-UNSIGNED` (one zip, about
   260 MB, with the packages for every platform) and extract it.
3. Check the files are the ones built (optional, but a good habit):

   - Windows (PowerShell): `Get-FileHash .\<file> -Algorithm SHA256` and
     compare with the line for that file in `SHA256SUMS`.
   - macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`
   - Linux: `sha256sum -c SHA256SUMS --ignore-missing`

Pick the package for your machine:

| Your machine | Recommended | Alternative |
|---|---|---|
| Windows 10/11, Intel/AMD | `otter-net-0.9.0-preview.1-win-x64.msi` | `...-win-x64.zip` (no installation) |
| Windows 11 on Arm | `otter-net-0.9.0-preview.1-win-arm64.msi` | `...-win-arm64.zip` |
| Mac with Apple silicon (M1 and later) | `otter-net-0.9.0-preview.1-osx-arm64.pkg` | `...-osx-arm64.tar.gz` (per user) |
| Mac with Intel | `otter-net-0.9.0-preview.1-osx-x64.pkg` | `...-osx-x64.tar.gz` |
| Debian/Ubuntu or a Chromebook (Linux), Intel/AMD | `otter-net_0.9.0~preview.1_amd64.deb` | `...-linux-x64.tar.gz` (per user) |
| Same, Arm (most Arm Chromebooks) | `otter-net_0.9.0~preview.1_arm64.deb` | `...-linux-arm64.tar.gz` |

Not sure? Windows: Settings > System > About, "System type". Mac: Apple
menu > About This Mac, "Chip". Linux: `dpkg --print-architecture`.

## 2. Install

Nothing else is needed on the machine: no PowerShell, no .NET.

### Windows

1. Double-click the `.msi`. It installs for your user only (no
   administrator rights) into `%LOCALAPPDATA%\Programs\otter-net`.
2. Because the preview is unsigned, **SmartScreen** may say "Windows
   protected your PC": click **More info**, then **Run anyway**. Some
   antivirus products may warn or quarantine it; please tell us which one
   and what it said (that is useful feedback), and don't lower your
   antivirus settings for this test.
3. Open a **new** terminal (Command Prompt or PowerShell) and run:

       otter-net --version

   It prints `Otter 1.0.1`. Desktop windows work too: they run in
   `otter-desktop.exe`, installed beside it.

**ZIP instead:** extract it anywhere and run `.\otter-net.exe` from that
folder. Nothing is installed; deleting the folder removes it.

### macOS (12 Monterey or later)

The `.pkg` is unsigned, so macOS blocks it when you double-click it. Install
it from Terminal instead (administrator password needed):

    sudo installer -pkg ~/Downloads/otter-net-0.9.0-preview.1-osx-arm64.pkg -target /

(Use your file's actual path and name.) Or double-click it, close the
warning, then System Settings > Privacy & Security > **Open Anyway**. It
installs into `/usr/local/lib/otter-net/0.9.0-preview.1` and links
`/usr/local/bin/otter-net`. Open a new Terminal window and run
`otter-net --version`.

**tar.gz instead** (no administrator rights):

    tar -xzf otter-net-0.9.0-preview.1-osx-arm64.tar.gz
    cd otter-net-0.9.0-preview.1-osx-arm64
    ./install.sh

It installs into `~/.local/share/otter-net/0.9.0-preview.1` with the command
`~/.local/bin/otter-net`; if `~/.local/bin` is not on your PATH, the script
prints the line to add to `~/.zshrc`. If macOS says the program "cannot be
opened because the developer cannot be verified", the download is
quarantined (the preview is not notarized); for this test only, clear it
with `xattr -dr com.apple.quarantine otter-net-0.9.0-preview.1-osx-arm64`
before `./install.sh`.

### Linux and Chromebooks

`.deb` (Debian 12 or newer, Ubuntu 22.04 or newer, a Chromebook's Linux):

    sudo apt install ./otter-net_0.9.0~preview.1_amd64.deb

On a Chromebook you can also double-click the `.deb` in the Files app. It
installs `/usr/bin/otter-net`. Then: `otter-net --version`.

`.tar.gz` (any Linux with glibc, per user, no administrator rights):

    tar -xzf otter-net-0.9.0-preview.1-linux-x64.tar.gz
    cd otter-net-0.9.0-preview.1-linux-x64
    ./install.sh

Databases need the system's SQLite: `sudo apt install libsqlite3-0` if a
program says SQLite could not be loaded.

## 3. Use it

`otter-net` takes exactly what `otter` takes:

    otter-net run hello.ot
    otter-net new console demo
    cd demo
    otter-net run .
    otter-net check .
    otter-net test .
    otter-net help

Messages it prints (help, errors) say `otter`, word for word as Otter 1.0.1
prints them: that is intended. If you have otterPS 1.0.1, run the same
program with both and compare:

    otter run myprogram.ot
    otter-net run myprogram.ot

The output is meant to be **identical** - any difference is worth a report.

## 4. Uninstall

| Package | Uninstall |
|---|---|
| Windows `.msi` | Settings > Apps > Installed apps > "Otter (otter-net)" > Uninstall |
| Windows `.zip` | delete the folder |
| macOS `.pkg` | `sudo /usr/local/lib/otter-net/0.9.0-preview.1/uninstall.sh` |
| `.deb` | `sudo apt remove otter-net` |
| `.tar.gz` (macOS, Linux) | `~/.local/share/otter-net/0.9.0-preview.1/uninstall.sh` |

Afterwards a new terminal has no `otter-net`, and `otter` is what it was
before (otterPS, or nothing).

## 5. Feedback checklist

Please send what you can of the following. Reports go to
<https://github.com/Heavens-Lava/otter-net/issues> (title starting
"[preview]"; issues are public, so leave out anything private), or straight
to the project owner.

**About your machine**

- [ ] Operating system and version (e.g. Windows 11 24H2, macOS 15.3,
      Debian 12, ChromeOS 13x)
- [ ] Processor: Intel/AMD or Arm
- [ ] Package used (`.msi`, `.zip`, `.pkg`, `.tar.gz`, `.deb`)
- [ ] otterPS 1.0.1 installed too? (yes/no; how: its installer, the Store,
      winget)

**Installing**

- [ ] The install worked (or: the exact message and where it stopped)
- [ ] Warnings seen: SmartScreen, Gatekeeper, antivirus (product and its
      exact message)
- [ ] A **new** terminal finds `otter-net`, and `otter-net --version`
      prints `Otter 1.0.1`
- [ ] `otter` is unchanged: still otterPS if you had it, otherwise not found
      (`where.exe otter` on Windows, `command -v otter` elsewhere)

**Using it**

- [ ] `otter-net new console demo`, then `run .`, `check .` and `test .`
      in it
- [ ] Your own programs and projects: same output as `otter` (otterPS)?
      Send the program (or the smallest part that shows it), what
      `otter-net` printed and what you expected
- [ ] Windows: a program with a desktop window (`create window`), if you
      use them
- [ ] Anything that uses the network, files, databases, web pages or
      `otter-net build`, if you use them
- [ ] Anything slow, a crash, or a message that mentions .NET or an
      "unhandled exception" (send it in full)

**Uninstalling**

- [ ] The uninstall worked and left nothing behind (no `otter-net` in a new
      terminal)
- [ ] `otter` still works as before (if you have otterPS)

**For every problem:** the exact command, the full output (copy the text,
or a screenshot), and whether it happens again.
