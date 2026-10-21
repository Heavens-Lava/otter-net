# Otter (otter-net) - internal preview

This is an **internal preview** of otter-net, the native implementation of
the [Otter](https://otterprogramming.com) programming language: one
self-contained `otter` command that needs nothing else installed - no
PowerShell and no .NET. It is **unsigned** and is **not** an Otter release;
the released Otter is at <https://otterprogramming.com/download/>.

otter-net runs the same language as Otter 1.0.1 (otterPS), with the same
output and error messages, so `otter --version` prints `Otter 1.0.1`. The
package's own version (this preview's) is in `VERSION`, and in the name of
the package you installed.

    otter --version
    otter run hello.ot
    otter new console demo
    otter help

On Windows, desktop windows run in `otter-desktop.exe`, installed beside
`otter.exe`; it carries its own .NET runtime, so nothing else is needed.

## Which otter am I running?

otter-net and otterPS both print `Otter 1.0.1`. To see which one a terminal
runs:

- Windows: `where.exe otter` - otter-net is
  `%LOCALAPPDATA%\Programs\otter-net\otter.exe`; otterPS is `otter.cmd` in
  `%LOCALAPPDATA%\Otter\1.0.1` or `%LOCALAPPDATA%\Programs\Otter`.
- macOS and Linux: `ls -l "$(command -v otter)"` - otter-net's is a link to
  `/usr/local/lib/otter-net/...` (.pkg) or `/usr/lib/otter-net/otter` (.deb),
  or a launcher whose second line says `otter-net launcher` (.tar.gz);
  otterPS's launcher says `Otter launcher - written by Install-Otter.ps1`.

## Installing and uninstalling

otter-net installs beside otterPS and never changes or removes it.

**Windows.** The `.msi` installs for the current user only (no administrator
rights) into `%LOCALAPPDATA%\Programs\otter-net` and puts that folder first
on the user's PATH, so a new terminal's `otter` is otter-net's even when
otterPS is installed. Uninstall it from Settings > Apps ("Otter (otter-net)");
otterPS's `otter` is then the one a new terminal finds again. The `.zip`
holds the same files to run in place: `otter.exe --version`.

**macOS.** The `.pkg` installs into `/usr/local/lib/otter-net/<version>` and
links `/usr/local/bin/otter` to it (administrator password needed). To
uninstall:

    sudo /usr/local/lib/otter-net/<version>/uninstall.sh

The `.tar.gz` installs for the current user only, like Linux below. If you
added otterPS's `~/.local/bin` to your PATH, its `otter` comes before
`/usr/local/bin/otter`.

**Linux and Chromebooks.** The `.deb` (`sudo apt install ./otter-net_*.deb`;
on a Chromebook, double-click it in the Files app) installs `/usr/bin/otter`;
uninstall with `sudo apt remove otter-net`. If otterPS is installed with its
`~/.local/bin/otter` command, that one comes first in a new terminal. The
`.tar.gz` installs for the current user only, no administrator rights:

    ./install.sh        # into ~/.local/share/otter-net/<version>, command in ~/.local/bin
    ~/.local/share/otter-net/<version>/uninstall.sh

When otterPS's `~/.local/bin/otter` is there, `install.sh` leaves it alone
unless you pass `--replace-otterps-launcher`; `uninstall.sh` then puts it
back.

Databases use the system's SQLite; on Linux install it with
`sudo apt install libsqlite3-0` if a program says SQLite could not be loaded.

## License

Otter Free Use License 1.1 - see `LICENSE`. The executables include the .NET
runtime (and, in `otter-desktop.exe`, the Windows Desktop runtime); see
`THIRD-PARTY-NOTICES.txt`.
