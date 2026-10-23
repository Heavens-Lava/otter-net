#!/bin/sh
# Uninstalls otter-net @VERSION@ installed by its .pkg:
#
#   sudo /usr/local/lib/otter-net/@VERSION@/uninstall.sh
#
# Removes /usr/local/bin/otter-net when it is otter-net's link, this
# version's folder, /usr/local/lib/otter-net when it is then empty, and the
# package receipt (pkgutil --forget). macOS has no uninstaller for .pkg files.
# /usr/local/bin/otter is removed only when it is a link an earlier otter-net
# package left to this folder.
set -eu
dest=/usr/local/lib/otter-net/@VERSION@

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this with sudo: sudo $dest/uninstall.sh" >&2
    exit 1
fi
for link in /usr/local/bin/otter-net /usr/local/bin/otter; do
    if [ -L "$link" ]; then
        case "$(readlink "$link")" in
            "$dest"/*|../lib/otter-net/@VERSION@/*) rm -f "$link"; echo "Removed $link." ;;
        esac
    fi
done
if [ -f "$dest/.otter-net-install" ]; then
    rm -rf "$dest"
    echo "Removed $dest."
fi
rmdir /usr/local/lib/otter-net 2>/dev/null || true
pkgutil --forget @IDENTIFIER@ >/dev/null 2>&1 || true
echo "otter-net @VERSION@ uninstalled."
