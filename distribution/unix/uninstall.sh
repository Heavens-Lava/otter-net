#!/bin/sh
# Removes an otter-net installation made by install.sh: its folder (only one
# carrying otter-net's installation marker), the ~/.local/bin/otter launcher
# when install.sh wrote it, and puts back the PowerShell Otter's launcher if
# install.sh replaced it.
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
        -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) fail "unknown option $1 (see --help)" ;;
    esac
done

if [ -z "$dest" ]; then
    if [ -f "$here/.otter-net-install" ]; then dest=$here
    else dest="$root/$(tr -d ' \t\r\n' < "$here/VERSION")"
    fi
fi

shim="$bin_dir/otter"
if [ -f "$shim" ]; then
    second=$(sed -n '2p' "$shim" 2>/dev/null || true)
    case "$second" in
        "$launcher_marker"*)
            if grep -qF "$dest/otter" "$shim"; then
                # The otterPS launcher install.sh kept runs
                # exec '<its folder>/otter' "$@"; it goes back only while that
                # installation still exists (otterPS may have been uninstalled
                # since, which leaves otter-net's launcher in place).
                otterps_target=""
                if [ -f "$root/otterps-launcher" ]; then
                    otterps_target=$(sed -n "3s/^exec '\\(.*\\)' \"\\\$@\"\$/\\1/p" "$root/otterps-launcher" | sed "s/'\\\\''/'/g")
                fi
                if [ -f "$root/otterps-launcher" ] && { [ -z "$otterps_target" ] || [ -e "$otterps_target" ]; }; then
                    mv -f "$root/otterps-launcher" "$shim"
                    echo "Put back the PowerShell Otter's otter command in $bin_dir."
                elif [ -f "$root/otterps-launcher" ]; then
                    rm -f "$root/otterps-launcher" "$shim"
                    echo "Removed the otter command from $bin_dir (the PowerShell Otter it replaced, $otterps_target, is no longer installed)."
                else
                    rm -f "$shim"
                    echo "Removed the otter command from $bin_dir."
                fi
            fi ;;
    esac
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
