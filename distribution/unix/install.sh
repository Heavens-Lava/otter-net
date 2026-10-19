#!/bin/sh
# Installs otter-net from this folder (the extracted .tar.gz) for the current
# user only - no administrator rights - in otterPS's per-user layout:
#
#   ${XDG_DATA_HOME:-~/.local/share}/otter-net/<version>/   the files
#   ~/.local/bin/otter                                       a small launcher
#
# otterPS's Install-Otter.ps1 uses ~/.local/share/otter/<version> (which
# otter-net looks in for the PowerShell Otter, so the two never share a
# folder) and the same ~/.local/bin/otter launcher. An otterPS launcher there
# is only replaced with --replace-otterps-launcher; it is kept and put back by
# uninstall.sh. Any other file called otter is never replaced.
#
# Installing a newer version removes the older otter-net versions it finds
# (only folders carrying otter-net's installation marker).
#
#   ./install.sh [--destination DIR] [--bin-dir DIR] [--no-launcher]
#                [--replace-otterps-launcher] [--force]
set -eu

here=$(cd "$(dirname "$0")" && pwd)
version=$(tr -d ' \t\r\n' < "$here/VERSION")
data_home=${XDG_DATA_HOME:-$HOME/.local/share}
root="$data_home/otter-net"
dest=""
bin_dir="$HOME/.local/bin"
launcher=yes
replace_otterps=no
force=no

marker_line='otter-net installation marker - written by install.sh'
launcher_marker='# otter-net launcher - written by install.sh'
otterps_marker='# Otter launcher - written by Install-Otter.ps1'

fail() { echo "install.sh: $*" >&2; exit 1; }

while [ $# -gt 0 ]; do
    case "$1" in
        --destination) [ $# -ge 2 ] || fail "--destination needs a folder"; dest=$2; shift 2 ;;
        --bin-dir) [ $# -ge 2 ] || fail "--bin-dir needs a folder"; bin_dir=$2; shift 2 ;;
        --no-launcher) launcher=no; shift ;;
        --replace-otterps-launcher) replace_otterps=yes; shift ;;
        --force) force=yes; shift ;;
        -h|--help) sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) fail "unknown option $1 (see --help)" ;;
    esac
done

[ -f "$here/bin/otter" ] || fail "this is not a complete otter-net package: bin/otter is missing"
[ -n "$version" ] || fail "this is not a complete otter-net package: VERSION is empty"
[ -n "$dest" ] || dest="$root/$version"
case "$dest" in /*) ;; *) fail "--destination must be a full path, not $dest" ;; esac
case "$dest" in /|"$HOME"|"$HOME/"|"$data_home"|"$data_home/") fail "refusing to install into $dest itself; choose a dedicated folder" ;; esac

# The launcher is checked before anything is copied, so a refusal leaves
# nothing behind.
shim="$bin_dir/otter"
shim_kind=none
if [ "$launcher" = yes ] && { [ -e "$shim" ] || [ -L "$shim" ]; }; then
    second=$(sed -n '2p' "$shim" 2>/dev/null || true)
    case "$second" in
        "$launcher_marker"*) shim_kind=ours ;;
        "$otterps_marker"*)
            [ "$replace_otterps" = yes ] || fail "$shim is the PowerShell Otter's launcher (otterPS). Pass --replace-otterps-launcher to let otter-net take the otter command (uninstall.sh puts it back), or --no-launcher."
            shim_kind=otterps ;;
        *) fail "refusing to replace $shim: it was not written by an Otter installer. Remove or rename it, or pass --no-launcher." ;;
    esac
fi

if [ -e "$dest" ]; then
    [ -d "$dest" ] || fail "$dest is a file, not a folder"
    if [ -n "$(ls -A "$dest")" ]; then
        [ -f "$dest/.otter-net-install" ] || fail "refusing to install into $dest: it is not empty and is not an otter-net installation"
        [ "$force" = yes ] || fail "otter-net $version is already installed at $dest. Run again with --force to replace it."
        rm -rf "$dest"
    fi
fi
mkdir -p "$dest"
# The marker first, so an interrupted install is still recognised.
printf '%s\nVersion: %s\n' "$marker_line" "$version" > "$dest/.otter-net-install"
cp "$here/bin/otter" "$dest/otter"
chmod 755 "$dest/otter"
for file in LICENSE README.md VERSION THIRD-PARTY-NOTICES.txt uninstall.sh; do
    [ -f "$here/$file" ] && cp "$here/$file" "$dest/$file"
done
chmod 755 "$dest/uninstall.sh"

"$dest/otter" --version || fail "the installed otter failed its --version check"

if [ "$launcher" = yes ]; then
    mkdir -p "$bin_dir"
    if [ "$shim_kind" = otterps ] && [ ! -f "$root/otterps-launcher" ]; then
        mkdir -p "$root"
        cp "$shim" "$root/otterps-launcher"
    fi
    escaped=$(printf '%s' "$dest/otter" | sed "s/'/'\\\\''/g")
    tmp="$shim.otter-net.$$"
    printf '#!/bin/sh\n%s (%s)\nexec '"'"'%s'"'"' "$@"\n' "$launcher_marker" "$version" "$escaped" > "$tmp"
    chmod 755 "$tmp"
    mv -f "$tmp" "$shim"
fi

# Older otter-net versions installed here are replaced by this one.
for old in "$root"/*/; do
    old=${old%/}
    [ "$old" = "$dest" ] && continue
    [ -f "$old/.otter-net-install" ] || continue
    rm -rf "$old" && echo "Removed otter-net $(basename "$old") from $old."
done

echo "otter-net $version installed to $dest"
if [ "$launcher" = yes ]; then
    case ":$PATH:" in
        *":$bin_dir:"*|*":$bin_dir/:"*) echo "Added the otter command to $bin_dir." ;;
        *) echo "Added the otter command to $bin_dir, which is not on your PATH yet. Add this line to your shell profile (~/.zshrc or ~/.bashrc) and open a new terminal:"
           echo "    export PATH=\"$bin_dir:\$PATH\"" ;;
    esac
fi
