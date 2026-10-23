#!/bin/sh
# Removes an otter-net installation made by install.sh: its folder (only one
# carrying otter-net's installation marker) and the ~/.local/bin/otter-net
# launcher when install.sh wrote it for that folder. The stable `otter`
# command is left alone, except a ~/.local/bin/otter launcher an earlier
# otter-net preview wrote for that folder: it is removed, and the PowerShell
# Otter's launcher it replaced is put back while that installation exists.
#
#   uninstall.sh [--destination DIR] [--bin-dir DIR]
#
# Run from an installation it removes that installation; run from the
# extracted package it removes the same version's default installation.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
data_home=${XDG_DATA_HOME:-$HOME/.local/share}
root="$data_home/otter-net"
bin_dir="$HOME/.local/bin"
dest=""
launcher_marker='# otter-net launcher - written by install.sh'

fail() { echo "uninstall.sh: $*" >&2; exit 1; }

while [ $# -gt 0 ]; do
    case "$1" in
        --destination) [ $# -ge 2 ] || fail "--destination needs a folder"; dest=$2; shift 2 ;;
        --bin-dir) [ $# -ge 2 ] || fail "--bin-dir needs a folder"; bin_dir=$2; shift 2 ;;
        -h|--help) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) fail "unknown option $1 (see --help)" ;;
    esac
done

if [ -z "$dest" ]; then
    if [ -f "$here/.otter-net-install" ]; then dest=$here
    else dest="$root/$(tr -d ' \t\r\n' < "$here/VERSION")"
    fi
fi

# ours <launcher>: a regular file (not a link) install.sh wrote for this
# installation (the path quoted as install.sh quotes it).
escaped_dest=$(printf '%s' "$dest" | sed "s/'/'\\\\''/g")
ours() {
    [ -f "$1" ] && [ ! -L "$1" ] || return 1
    case "$(sed -n '2p' "$1" 2>/dev/null || true)" in "$launcher_marker"*) ;; *) return 1 ;; esac
    grep -qF "exec '$escaped_dest/" "$1"
}

shim="$bin_dir/otter-net"
if ours "$shim"; then
    rm -f "$shim"
    echo "Removed the otter-net command from $bin_dir."
fi

legacy="$bin_dir/otter"
if ours "$legacy"; then
    otterps_target=""
    if [ -f "$root/otterps-launcher" ]; then
        otterps_target=$(sed -n "3s/^exec '\\(.*\\)' \"\\\$@\"\$/\\1/p" "$root/otterps-launcher" | sed "s/'\\\\''/'/g")
    fi
    if [ -f "$root/otterps-launcher" ] && { [ -z "$otterps_target" ] || [ -e "$otterps_target" ]; }; then
        mv -f "$root/otterps-launcher" "$legacy"
        echo "Put back the PowerShell Otter's otter command in $bin_dir."
    else
        rm -f "$root/otterps-launcher" "$legacy"
        echo "Removed an earlier otter-net preview's otter command from $bin_dir."
    fi
fi

if [ ! -e "$dest" ]; then
    echo "otter-net is not installed at $dest (already uninstalled)."
else
    [ -f "$dest/.otter-net-install" ] || fail "refusing to remove $dest: it is not an otter-net installation"
    rm -rf "$dest"
    echo "Removed otter-net from $dest."
fi
# The otter-net folder goes too once nothing else is in it.
rmdir "$root" 2>/dev/null || true
