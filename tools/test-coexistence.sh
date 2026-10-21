#!/bin/sh
# Tests otter-net installed beside the PowerShell Otter (otterPS 1.0.1,
# installed from its own release ZIP with its own Install-Otter.ps1
# -AddToUserPath): which `otter` a new terminal gets, that both still work,
# and that uninstalling either one, in both orders, leaves the other intact.
# Changes the machine: run it on a disposable runner or container only.
#
#   tools/test-coexistence.sh tarball <otter-net-*.tar.gz> <otter-1.0.1.zip>
#   tools/test-coexistence.sh deb <otter-net_*.deb> <otter-1.0.1.zip>   as root (Debian container)
#   tools/test-coexistence.sh pkg <otter-net-*.pkg> <otter-1.0.1.zip>   with sudo (macOS runner)
#
# otterPS needs PowerShell 7 (pwsh) on the PATH. A "new terminal" is a login
# shell of the test user: on Linux `su - <user>` (Debian's ~/.profile puts
# ~/.local/bin first on the PATH when it exists, as for any new user); on
# macOS an interactive login zsh (`zsh -il`, as Terminal starts it) in a
# fresh HOME, once without and once with the line otterPS's installer asks
# users to add to ~/.zshrc (export PATH="$HOME/.local/bin:$PATH").
set -eu

repo=$(cd "$(dirname "$0")/.." && pwd)
package_version=$(tr -d ' \t\r\n' < "$repo/VERSION")
scratch=$(mktemp -d "${TMPDIR:-/tmp}/otter-coexist.XXXXXX")
chmod 755 "$scratch"
scratch=$(cd "$scratch" && pwd -P)
trap 'rm -rf "$scratch"' EXIT
failures=0
summary=""
say() { printf '%s\n' "$*"; }
fail() { say "FAIL: $*"; failures=$((failures + 1)); }
pass() { say "ok: $*"; }
note() { summary="$summary
$*"; say "$*"; }
abort() { say "FAIL: $*"; exit 1; }

mode=${1:-}
[ $# -eq 3 ] || { sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
package=$2; case "$package" in /*) ;; *) package="$PWD/$package" ;; esac
otterps_zip=$3; case "$otterps_zip" in /*) ;; *) otterps_zip="$PWD/$otterps_zip" ;; esac
command -v pwsh > /dev/null || abort "otterPS needs PowerShell 7 (pwsh) on the PATH"

# ---- The test user and its "new terminal" ----------------------------------

if [ "$(uname)" = Darwin ]; then
    user_home="$scratch/home"
    mkdir -p "$user_home"
    pwsh_dir=$(dirname "$(command -v pwsh)")
    as_user() { env HOME="$user_home" PATH="$pwsh_dir:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin" sh -c "$1"; }
    # Interactive login zsh, as Terminal opens it; env -i so the runner's
    # own variables do not leak in. $1 = with otterPS's ~/.zshrc line or not.
    new_terminal() {
        if [ "$1" = with-profile-line ]; then printf 'export PATH="$HOME/.local/bin:$PATH"\n' > "$user_home/.zshrc"; else rm -f "$user_home/.zshrc"; fi
        env -i HOME="$user_home" USER="$(id -un)" TERM=dumb zsh -il -c "$2" 2>/dev/null | tail -1
    }
else
    [ "$(id -u)" -eq 0 ] || abort "on Linux this runs as root in a disposable container (it creates a user)"
    id otteruser > /dev/null 2>&1 || useradd -m -s /bin/bash otteruser
    user_home=$(getent passwd otteruser | cut -d: -f6)
    as_user() { su -s /bin/sh otteruser -c "$1"; }
    # A login shell (su -): Debian's ~/.profile adds ~/.local/bin when it exists.
    new_terminal() { su - otteruser -c "$2" 2>/dev/null | tail -1; }
fi
say "test user home: $user_home"

# Which Otter is this path? otter-net or otterPS (or something else).
classify() {
    target=$1
    case "$target" in "") echo none; return ;; esac
    real=$target
    if [ -L "$target" ]; then real=$(readlink "$target"); fi
    case "$real" in
        */otter-net/*) echo otter-net; return ;;
    esac
    second=$(sed -n '2p' "$target" 2>/dev/null || true)
    case "$second" in
        "# otter-net launcher"*) echo otter-net ;;
        "# Otter launcher - written by Install-Otter.ps1"*) echo otterPS ;;
        *) echo "other ($target)" ;;
    esac
}

# The otter a new terminal finds, and which one it is.
terminal_otter() {
    found=$(new_terminal "$1" 'command -v otter || true')
    printf '%s %s' "$(classify "$found")" "$found"
}

hello="$scratch/hello.ot"
printf 'say "Hello from" "both"\n' > "$hello"
chmod 644 "$hello"

# works <label> <command path>: --version and a program.
works() {
    label=$1; cmd=$2
    printed=$(as_user "cd '$scratch' && '$cmd' --version" 2>&1 || true)
    ran=$(as_user "cd '$scratch' && '$cmd' run hello.ot" 2>&1 || true)
    if [ "$printed" = "Otter 1.0.1" ] && [ "$ran" = "Hello from both" ]; then pass "$label works ($cmd): $printed"
    else fail "$label ($cmd): --version '$printed', run '$ran'"; fi
}

# ---- otterPS, from its own release ZIP --------------------------------------

otterps_payload="$scratch/otterps"
mkdir -p "$otterps_payload"
pwsh -NoProfile -Command "Expand-Archive -LiteralPath '$otterps_zip' -DestinationPath '$otterps_payload'"
otterps_dir=$(find "$otterps_payload" -mindepth 1 -maxdepth 1 -type d | head -1)
chmod -R a+rX "$otterps_payload"
otterps_home="$user_home/.local/share/otter/1.0.1"
otterps_shim="$user_home/.local/bin/otter"

otterps_install() {
    as_user "cd '$otterps_dir' && pwsh -NoProfile -File ./Install-Otter.ps1 -AddToUserPath" > "$scratch/otterps-install.txt" 2>&1
}
otterps_uninstall() {
    as_user "pwsh -NoProfile -File '$otterps_home/Uninstall-Otter.ps1'" > "$scratch/otterps-uninstall.txt" 2>&1 || fail "otterPS's Uninstall-Otter.ps1: $(cat "$scratch/otterps-uninstall.txt")"
}
# A fingerprint of otterPS's installed files, to show the other product
# changed nothing of it.
otterps_fingerprint() {
    (cd "$otterps_home" && find . -type f | LC_ALL=C sort | while read -r f; do cksum "$f"; done) 2>/dev/null
}
otterps_intact() {
    label=$1
    if [ "$(otterps_fingerprint)" = "$otterps_before" ] && [ "$(classify "$otterps_shim")" = otterPS ]; then pass "$label: otterPS's files and its ~/.local/bin/otter launcher are unchanged"
    else fail "$label: otterPS changed (files or launcher $(classify "$otterps_shim"))"; fi
    works "otterPS" "$otterps_home/otter"
}
otterps_gone() {
    if [ -e "$user_home/.local/share/otter" ] || [ "$(classify "$otterps_shim")" = otterPS ]; then fail "$1: otterPS left: $(ls -A "$user_home/.local/share/otter" 2>/dev/null) $(ls -l "$otterps_shim" 2>/dev/null)"
    else pass "$1: otterPS fully removed"; fi
}

# ---- otter-net, per format ----------------------------------------------------

case "$mode" in
    tarball)
        mkdir -p "$scratch/net"
        tar -xzf "$package" -C "$scratch/net"
        net_pkg=$(find "$scratch/net" -mindepth 1 -maxdepth 1 -type d | head -1)
        chmod -R a+rX "$scratch/net"
        net_home="$user_home/.local/share/otter-net/$package_version"
        net_cmd="$net_home/otter"
        net_install() { as_user "'$net_pkg/install.sh' $1" > "$scratch/net-install.txt" 2>&1; }
        net_uninstall() { as_user "'$net_home/uninstall.sh'" > "$scratch/net-uninstall.txt" 2>&1 || fail "otter-net uninstall.sh: $(cat "$scratch/net-uninstall.txt")"; }
        net_present() { [ -x "$net_cmd" ]; }
        net_gone() {
            if [ -e "$user_home/.local/share/otter-net" ]; then fail "$1: otter-net left $user_home/.local/share/otter-net"
            else pass "$1: otter-net fully removed"; fi
        }
        ;;
    deb)
        [ "$(id -u)" -eq 0 ] || abort "deb: run as root in a disposable container"
        net_cmd=/usr/bin/otter
        net_install() { apt-get install -y "$package" > "$scratch/net-install.txt" 2>&1; }
        net_uninstall() { apt-get remove --purge -y otter-net > "$scratch/net-uninstall.txt" 2>&1 || fail "apt-get remove: $(tail -5 "$scratch/net-uninstall.txt")"; }
        net_present() { dpkg-query -W otter-net > /dev/null 2>&1 && [ -x /usr/lib/otter-net/otter ]; }
        net_gone() {
            if dpkg-query -W otter-net > /dev/null 2>&1 || [ -e /usr/bin/otter ] || [ -e /usr/lib/otter-net ]; then fail "$1: otter-net left behind"
            else pass "$1: otter-net fully removed"; fi
        }
        apt-get update -qq > /dev/null
        ;;
    pkg)
        sudo -n true 2>/dev/null || abort "pkg: needs sudo (a disposable Mac)"
        [ ! -e /usr/local/bin/otter ] || abort "/usr/local/bin/otter already exists"
        net_cmd=/usr/local/bin/otter
        net_install() { sudo installer -pkg "$package" -target / > "$scratch/net-install.txt" 2>&1; }
        net_uninstall() { sudo "/usr/local/lib/otter-net/$package_version/uninstall.sh" > "$scratch/net-uninstall.txt" 2>&1 || fail "uninstall.sh: $(cat "$scratch/net-uninstall.txt")"; }
        net_present() { [ -x "/usr/local/lib/otter-net/$package_version/otter" ] && [ -L /usr/local/bin/otter ]; }
        net_gone() {
            if [ -e /usr/local/lib/otter-net ] || [ -e /usr/local/bin/otter ] || pkgutil --pkgs | grep -qx com.otterprogramming.otter-net; then fail "$1: otter-net left behind"
            else pass "$1: otter-net fully removed"; fi
        }
        ;;
    *) abort "unknown mode $mode" ;;
esac

net_fingerprint() {
    for f in "$net_cmd" "$(readlink "$net_cmd" 2>/dev/null || true)"; do if [ -f "$f" ]; then cksum < "$f"; fi; done 2>/dev/null
    return 0
}

# Reports which otter new terminals get. On Linux one answer; on macOS
# without and with otterPS's PATH line.
report_terminal() {
    label=$1; expect=$2; expect_with_line=${3:-$2}
    if [ "$(uname)" = Darwin ]; then
        plain=$(terminal_otter without-profile-line)
        with=$(terminal_otter with-profile-line)
        note "- $mode, $label: a new terminal's otter is $plain; with otterPS's PATH line in ~/.zshrc: $with"
        case "$plain" in "$expect"*) pass "$label: new terminal (no ~/.local/bin on PATH): $plain" ;; *) fail "$label: new terminal (no ~/.local/bin on PATH): $plain, expected $expect" ;; esac
        case "$with" in "$expect_with_line"*) pass "$label: new terminal (otterPS's PATH line): $with" ;; *) fail "$label: new terminal (otterPS's PATH line): $with, expected $expect_with_line" ;; esac
    else
        found=$(terminal_otter -)
        note "- $mode, $label: a new terminal's otter is $found"
        case "$found" in "$expect"*) pass "$label: new terminal: $found" ;; *) fail "$label: new terminal: $found, expected $expect" ;; esac
    fi
}

# What a new terminal gets with both installed: the .tar.gz takes
# ~/.local/bin/otter only when told to; the .deb (/usr/bin) and the .pkg
# (/usr/local/bin) come after ~/.local/bin wherever that is on the PATH.
case "$mode" in
    tarball) both_plain=otter-net; both_line=otter-net ;;
    deb) both_plain=otterPS; both_line=otterPS ;;
    pkg) both_plain=otter-net; both_line=otterPS ;;
esac
# macOS has no ~/.local/bin on the PATH until the user adds otterPS's line:
# without it, neither launcher in ~/.local/bin is found.
ps_plain=otterPS; net_plain=otter-net
if [ "$(uname)" = Darwin ]; then
    ps_plain=none
    [ "$mode" = tarball ] && { both_plain=none; net_plain=none; }
fi

# ---- Order A: otterPS first, then otter-net; uninstall otter-net first --------

say "== A: otterPS, then otter-net; remove otter-net, then otterPS"
otterps_install || abort "otterPS's Install-Otter.ps1 -AddToUserPath: $(cat "$scratch/otterps-install.txt")"
cat "$scratch/otterps-install.txt"
otterps_before=$(otterps_fingerprint)
[ "$(classify "$otterps_shim")" = otterPS ] || abort "otterPS did not write its ~/.local/bin/otter launcher"
works "otterPS (alone)" "$otterps_home/otter"
report_terminal "otterPS alone" "$ps_plain" otterPS

if [ "$mode" = tarball ]; then
    if net_install ""; then fail "install.sh replaced otterPS's launcher unasked"
    else pass "install.sh refuses to take otterPS's otter command unasked: $(tail -1 "$scratch/net-install.txt")"; fi
    net_install --replace-otterps-launcher || abort "install.sh --replace-otterps-launcher: $(cat "$scratch/net-install.txt")"
else
    net_install || abort "installing otter-net: $(tail -20 "$scratch/net-install.txt")"
fi
net_present || abort "otter-net is not installed"
works "otter-net" "$net_cmd"
if [ "$mode" = tarball ]; then
    works "otterPS (beside otter-net)" "$otterps_home/otter"
    [ "$(otterps_fingerprint)" = "$otterps_before" ] && pass "otterPS's files unchanged" || fail "otterPS's files changed"
else
    otterps_intact "otter-net installed"
fi
report_terminal "both installed" "$both_plain" "$both_line"
net_uninstall
net_gone "A: otter-net removed"
otterps_intact "A: after otter-net was removed"
report_terminal "A: after otter-net was removed" "$ps_plain" otterPS
otterps_uninstall
otterps_gone "A: otterPS removed"
report_terminal "A: both removed" none none
note "- $mode, order A (otterPS, otter-net; remove otter-net, then otterPS): see the checks above"

# ---- Order B: otter-net first, then otterPS; uninstall otterPS first ---------

say "== B: otter-net, then otterPS; remove otterPS, then otter-net"
if [ "$mode" = tarball ]; then net_install "" || abort "install.sh: $(cat "$scratch/net-install.txt")"
else net_install || abort "installing otter-net: $(tail -20 "$scratch/net-install.txt")"; fi
net_before=$(net_fingerprint)
works "otter-net (alone)" "$net_cmd"
if otterps_install; then
    otterps_ok=yes
    pass "otterPS's Install-Otter.ps1 -AddToUserPath after otter-net"
else
    otterps_ok=no
    if [ "$mode" = tarball ] && grep -q "Refusing to replace" "$scratch/otterps-install.txt"; then
        pass "otterPS's installer refuses to replace otter-net's ~/.local/bin/otter (otterPS's own rule); its files are installed, its command is not"
        note "- tarball, order B: otterPS's Install-Otter.ps1 -AddToUserPath stops at otter-net's launcher (\"Refusing to replace ...\"): install otterPS first, or run it without -AddToUserPath"
    else
        fail "otterPS's installer after otter-net: $(cat "$scratch/otterps-install.txt")"
    fi
fi
if [ -x "$otterps_home/otter" ]; then works "otterPS (beside otter-net)" "$otterps_home/otter"; fi
works "otter-net (beside otterPS)" "$net_cmd"
if [ "$mode" = tarball ]; then report_terminal "B: both installed" "$net_plain" otter-net
else report_terminal "B: both installed" "$both_plain" "$both_line"; fi
if [ -d "$otterps_home" ]; then otterps_uninstall; fi
otterps_gone "B: otterPS removed"
if net_present && [ "$(net_fingerprint)" = "$net_before" ]; then pass "B: otter-net intact after otterPS was removed"; else fail "B: otter-net changed after otterPS was removed"; fi
works "otter-net (otterPS removed)" "$net_cmd"
if [ "$mode" = deb ] || [ "$mode" = pkg ]; then report_terminal "B: after otterPS was removed" otter-net otter-net
else report_terminal "B: after otterPS was removed" "$net_plain" otter-net; fi
net_uninstall
net_gone "B: otter-net removed"
report_terminal "B: both removed" none none
[ "$otterps_ok" = yes ] || true
note "- $mode, order B (otter-net, otterPS; remove otterPS, then otter-net): see the checks above"

say ""
say "Summary (coexistence $mode $(basename "$package")):$summary"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    printf '### coexistence with otterPS 1.0.1: %s %s\n%s\n\n' "$mode" "$(basename "$package")" "$summary" >> "$GITHUB_STEP_SUMMARY"
fi
if [ "$failures" -ne 0 ]; then say "$failures check(s) failed"; exit 1; fi
say "All checks passed"
