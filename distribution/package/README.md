# Otter (otter-net) - internal preview

This is an **internal preview** of otter-net, the native implementation of
the [Otter](https://otterprogramming.com) programming language: one
self-contained `otter-net` command that needs nothing else installed - no
PowerShell and no .NET. It is **unsigned** and is **not** an Otter release;
the released Otter is at <https://otterprogramming.com/download/>.

otter-net runs the same language as Otter 1.0.1 (otterPS), with the same
output and error messages, so `otter-net --version` prints `Otter 1.0.1`.
The package's own version (this preview's) is in `VERSION`, and in the name
of the package you installed.

    otter-net --version
    otter-net run hello.ot
    otter-net new console demo
    otter-net help

`otter-net` takes exactly the arguments `otter` takes. Messages it prints
(help, errors) say `otter`, word for word as Otter 1.0.1 prints them.

On Windows, desktop windows run in `otter-desktop.exe`, installed beside
`otter-net.exe`; it carries its own .NET runtime, so nothing else is needed.

## otter and otter-net

otter-net installs **only** the `otter-net` command. It never installs,
replaces or removes an `otter` command: if you have the released Otter
(otterPS), `otter` keeps running it, before and after otter-net is installed
or uninstalled. To compare the two, run the same program with both:

    otter run hello.ot        # otterPS (if installed)
    otter-net run hello.ot    # otter-net

## Installing and uninstalling

**Windows.** The `.msi` installs for the current user only (no administrator
rights) into `%LOCALAPPDATA%\Programs\otter-net` and adds that folder to the
end of the user's PATH: open a new terminal and type `otter-net --version`.
Uninstall it from Settings > Apps ("Otter (otter-net)"). The `.zip` holds
the same files to run in place: `.\otter-net.exe --version`.

**macOS.** The `.pkg` installs into `/usr/local/lib/otter-net/<version>` and
links `/usr/local/bin/otter-net` to it (administrator password needed). To
uninstall:

    sudo /usr/local/lib/otter-net/<version>/uninstall.sh

The `.tar.gz` installs for the current user only, like Linux below.

**Linux and Chromebooks.** The `.deb` (`sudo apt install ./otter-net_*.deb`;
on a Chromebook, double-click it in the Files app) installs
`/usr/bin/otter-net`; uninstall with `sudo apt remove otter-net`. The
`.tar.gz` installs for the current user only, no administrator rights:

    ./install.sh        # into ~/.local/share/otter-net/<version>, command ~/.local/bin/otter-net
    ~/.local/share/otter-net/<version>/uninstall.sh

If an earlier otter-net preview took `~/.local/bin/otter`, installing this
one gives it back to the PowerShell Otter (or removes it if otterPS is no
longer installed); the `.pkg` likewise removes an earlier preview's
`/usr/local/bin/otter` link.

Databases use the system's SQLite; on Linux install it with
`sudo apt install libsqlite3-0` if a program says SQLite could not be loaded.

## License

Otter Free Use License 1.1 - see `LICENSE`. The executables include the .NET
runtime (and, in `otter-desktop.exe`, the Windows Desktop runtime); see
`THIRD-PARTY-NOTICES.txt`.
