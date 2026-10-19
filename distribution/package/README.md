# Otter (otter-net) - release candidate

This is a **release candidate** of otter-net, the native implementation of the
[Otter](https://otterprogramming.com) programming language: one self-contained
`otter` command that needs nothing else installed. It is **unsigned** and is
**not** an Otter release; the released Otter is at
<https://otterprogramming.com/download/>.

otter-net runs the same language as Otter 1.0.1 (otterPS), with the same
output and error messages, so `otter --version` prints `Otter 1.0.1`. The
package version is in `VERSION`.

    otter --version
    otter run hello.ot
    otter new console demo
    otter help

A few things are still handed to the PowerShell Otter (desktop windows and
remote commands on Windows). When a program needs them, `otter` says so; with
Otter 1.0.1 installed by its own installer, `otter` runs them through it.

## Installing and uninstalling

**Windows.** The `.msi` installs for the current user only (no administrator
rights) into `%LOCALAPPDATA%\Programs\otter-net` and puts that folder on the
user's PATH. Uninstall it from Settings > Apps. The `.zip` holds the same
files to run in place: `otter.exe --version`.

**macOS.** The `.pkg` installs into `/usr/local/lib/otter-net/<version>` and
links `/usr/local/bin/otter` to it (administrator password needed). To
uninstall:

    sudo /usr/local/lib/otter-net/<version>/uninstall.sh

The `.tar.gz` installs for the current user only, like Linux below.

**Linux and Chromebooks.** The `.deb` (`sudo apt install ./otter-net_*.deb`;
on a Chromebook, double-click it in the Files app) installs `/usr/bin/otter`;
uninstall with `sudo apt remove otter-net`. The `.tar.gz` installs for the
current user only, no administrator rights:

    ./install.sh        # into ~/.local/share/otter-net/<version>, command in ~/.local/bin
    ~/.local/share/otter-net/<version>/uninstall.sh

Databases use the system's SQLite; on Linux install it with
`sudo apt install libsqlite3-0` if a program says SQLite could not be loaded.

## License

Otter Free Use License 1.1 - see `LICENSE`. The executable includes the .NET
runtime; see `THIRD-PARTY-NOTICES.txt`.
