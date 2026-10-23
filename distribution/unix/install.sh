#!/bin/sh
# Installs otter-net from this folder (the extracted .tar.gz) for the current
# user only - no administrator rights - in otterPS's per-user layout:
#
#   ${XDG_DATA_HOME:-~/.local/share}/otter-net/<version>/   the files
#   ~/.local/bin/otter-net                                   a small launcher
#
# otter-net installs the otter-net command only. It never writes, replaces
# or removes the stable `otter` command: otterPS's Install-Otter.ps1 uses
# ~/.local/share/otter/<version> and the ~/.local/bin/otter launcher, and
# those stay as they are. (Earlier otter-net previews could take
# ~/.local/bin/otter; installing this one gives it back - see below.)
# Any other file called otter-net is never replaced.
#
# Installing a newer version removes the older otter-net versions it finds
# (only folders carrying otter-net's installation marker).
#
#   ./install.sh [--destination DIR] [--bin-dir DIR] [--no-launcher] [--force]
set -eu

here=$(cd "$(dirname "$0")" && pwd)
version=$(tr -d ' \t\r\n' < "$here/VERSION")
data_home=${XDG_DATA_HOME:-$HOME/.local/share}
root="$data_home/otter-net"
dest=""
bin_dir="$HOME/.local/bin"
launcher=yes
force=no

marker_line='otter-net installation marker - written by install.sh'
launcher_marker='# otter-net launcher - written by install.sh'

fail() { echo "install.sh: $*" >&2; exit 1; }

while [ $# -gt 0 ]; do
    case "$1" in
        --destination) [ $# -ge 2 ] || fail "--destination needs a folder"; dest=$2; shift 2 ;;
        --bin-dir) [ $# -ge 2 ] || fail "--bin-dir needs a folder"; bin_dir=$2; shift 2 ;;
        --no-launcher) launcher=no; shift ;;
        --force) force=yes; shift ;;
        --replace-otterps-launcher) fail "--replace-otterps-launcher is no longer supported: otter-net installs the otter-net command and leaves otter as it is." ;;
        -h|--help) sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) fail "unknown option $1 (see --help)" ;;
    esac
done

[ -f "$here/bin/otter-net" ] || fail "this is not a complete otter-net package: bin/otter-net is missing"
[ -n "$version" ] || fail "this is not a complete otter-net package: VERSION is empty"
[ -n "$dest" ] || dest="$root/$version"
case "$dest" in /*) ;; *) fail "--destination must be a full path, not $dest" ;; esac
case "$dest" in /|"$HOME"|"$HOME/"|"$data_home"|"$data_home/") fail "refusing to install into $dest itself; choose a dedicated folder" ;; esac

# The launcher is checked before anything is copied, so a refusal leaves
# nothing behind.
shim="$bin_dir/otter-net"
if [ "$launcher" = yes ] && { [ -e "$shim" ] || [ -L "$shim" ]; }; then
    case "$(sed -n '2p' "$shim" 2>/dev/null || true)" in
        "$launcher_marker"*) ;;
        *) fail "refusing to replace $shim: it was not written by otter-net's install.sh. Remove or rename it, or pass --no-launcher." ;;
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
cp "$here/bin/otter-net" "$dest/otter-net"
chmod 755 "$dest/otter-net"
for file in LICENSE README.md VERSION THIRD-PARTY-NOTICES.txt uninstall.sh; do
    [ -f "$here/$file" ] && cp "$here/$file" "$dest/$file"
done
chmod 755 "$dest/uninstall.sh"

"$dest/otter-net" --version || fail "the installed otter-net failed its --version check"

if [ "$launcher" = yes ]; then
    mkdir -p "$bin_dir"
    escaped=$(printf '%s' "$dest/otter-net" | sed "s/'/'\\\\''/g")
    tmp="$shim.otter-net.$$"
    printf '#!/bin/sh\n%s (%s)\nexec '"'"'%s'"'"' "$@"\n' "$launcher_marker" "$version" "$escaped" > "$tmp"
    chmod 755 "$tmp"
    mv -f "$tmp" "$shim"
fi

# An earlier otter-net preview's otter launcher goes: only a regular file
# (not a link) with otter-net's marker that runs something under
# $root/, in --bin-dir or ~/.local/bin (where earlier previews wrote it).
# otterPS's launcher, if that preview kept it and its installation still
# exists, comes back; otherwise there is no otter.
launcher_target() {
    sed -n "3s/^exec '\\(.*\\)' \"\\\$@\"\$/\\1/p" "$1" | sed "s/'\\\\''/'/g"
}
give_back_otter() {
    legacy=$1
    [ -f "$legacy" ] && [ ! -L "$legacy" ] || return 0
    case "$(sed -n '2p' "$legacy" 2>/dev/null || true)" in "$launcher_marker"*) ;; *) return 0 ;; esac
    case "$(launcher_target "$legacy")" in "$root"/*) ;; *) return 0 ;; esac
    otterps_target=""
    if [ -f "$root/otterps-launcher" ]; then otterps_target=$(launcher_target "$root/otterps-launcher"); fi
    if [ -f "$root/otterps-launcher" ] && { [ -z "$otterps_target" ] || [ -e "$otterps_target" ]; }; then
        mv -f "$root/otterps-launcher" "$legacy"
        echo "Gave $legacy back to the PowerShell Otter (an earlier otter-net preview had taken it)."
    else
        rm -f "$root/otterps-launcher" "$legacy"
        echo "Removed an earlier otter-net preview's $legacy; otter-net is now the otter-net command."
    fi
}
give_back_otter "$bin_dir/otter"
[ "$bin_dir/otter" = "$HOME/.local/bin/otter" ] || give_back_otter "$HOME/.local/bin/otter"

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
        *":$bin_dir:"*|*":$bin_dir/:"*) echo "Added the otter-net command to $bin_dir." ;;
        *) echo "Added the otter-net command to $bin_dir, which is not on your PATH yet. Add this line to your shell profile (~/.zshrc or ~/.bashrc) and open a new terminal:"
           echo "    export PATH=\"$bin_dir:\$PATH\"" ;;
    esac
fi
