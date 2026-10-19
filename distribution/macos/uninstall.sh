#!/bin/sh
# Uninstalls otter-net @VERSION@ installed by its .pkg:
#
#   sudo /usr/local/lib/otter-net/@VERSION@/uninstall.sh
#
# Removes /usr/local/bin/otter when it is otter-net's link, this version's
# folder, /usr/local/lib/otter-net when it is then empty, and the package
# receipt (pkgutil --forget). macOS has no uninstaller for .pkg files.
set -eu
dest=/usr/local/lib/otter-net/@VERSION@
link=/usr/local/bin/otter

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this with sudo: sudo $dest/uninstall.sh" >&2
    exit 1
fi
if [ -L "$link" ]; then
    case "$(readlink "$link")" in
        "$dest"/*|../lib/otter-net/@VERSION@/*) rm -f "$link"; echo "Removed $link." ;;
    esac
fi
if [ -f "$dest/.otter-net-install" ]; then
    rm -rf "$dest"
    echo "Removed $dest."
fi
rmdir /usr/local/lib/otter-net 2>/dev/null || true
pkgutil --forget @IDENTIFIER@ >/dev/null 2>&1 || true
echo "otter-net @VERSION@ uninstalled."
