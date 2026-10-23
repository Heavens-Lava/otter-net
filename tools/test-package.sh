#!/bin/sh
# Tests an otter-net package the way a user gets it, on a machine without
# PowerShell: installs it, runs the installed `otter-net` (its version,
# tests/compat, and a new project), uninstalls it and checks that nothing is
# left behind and that the `otter` command was never installed or changed. A POSIX shell script, so it needs nothing PowerShell; run it
# from the repository (it reads tests/compat and src/Otter.Cli/Program.cs).
#
#   tools/test-package.sh tarball <otter-net-*.tar.gz>          per-user, in a temporary HOME
#   tools/test-package.sh deb <otter-net_*.deb> [<older .deb> [<newer .deb>]]  as root (a disposable container)
#   tools/test-package.sh pkg <otter-net-*.pkg> [<older .pkg> [<newer .pkg>]]  with sudo (a disposable Mac)
#   tools/test-package.sh pkg-payload <otter-net-*.pkg>         no installation: the expanded payload
#   tools/test-package.sh binary <path to otter-net or otter>   the checks alone
#
# OTTER_PRE_RENAME=<file> (tarball, deb, pkg): a real package from before
# the command was renamed (it installed `otter`); this one is installed over
# it and must take that `otter` away again.
#
# Every `otter-net` runs with PATH=/usr/bin:/bin (and the folder of the otter
# under test), and the script fails if PowerShell (pwsh, powershell) is found
# there. OTTER_TEST_REFERENCE=<folder of tools/capture.ps1 .out files> also
# compares each program's output and exit code with it (reported, not fatal).
set -eu

repo=$(cd "$(dirname "$0")/.." && pwd)
language_version=$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$repo/src/Otter.Cli/Program.cs")
package_version=$(tr -d ' \t\r\n' < "$repo/VERSION")
scratch=$(mktemp -d "${TMPDIR:-/tmp}/otter-net-test.XXXXXX")
scratch=$(cd "$scratch" && pwd -P)
trap 'rm -rf "$scratch"' EXIT
failures=0
summary=""

say() { printf '%s\n' "$*"; }
fail() { say "FAIL: $*"; failures=$((failures + 1)); }
pass() { say "ok: $*"; }
note() { summary="$summary
$*"; say "$*"; }
# The whole run stops here (a step later checks depend on failed).
abort() { say "FAIL: $*"; exit 1; }

# A clean PATH: the system's own folders and the otter-net under test. $1 is
# the folder that holds the `otter-net` command.
clean_path() {
    printf '%s' "$1:/usr/bin:/bin:/usr/sbin:/sbin"
}

# Milliseconds, for timing: perl's Time::HiRes (macOS), or GNU date's %N.
now_ms() {
    perl -MTime::HiRes=time -e 'printf "%.0f", time * 1000' 2>/dev/null || echo $(( $(date +%s%N) / 1000000 ))
}

# check_otter <folder holding otter-net> <label>
check_otter() {
    bin_dir=$1
    label=$2
    path=$(clean_path "$bin_dir")
    say "== $label: otter-net from $bin_dir, PATH=$path"
    for shell_name in pwsh powershell powershell.exe pwsh.exe; do
        if env PATH="$path" sh -c "command -v $shell_name" >/dev/null 2>&1; then
            abort "$shell_name is on the test PATH; this is not a machine without PowerShell"
        fi
    done
    resolved=$(env PATH="$path" sh -c 'command -v otter-net' || true)
    [ "$resolved" = "$bin_dir/otter-net" ] || abort "otter-net resolves to '$resolved', not $bin_dir/otter-net"
    if [ -e "$bin_dir/otter" ] || [ -L "$bin_dir/otter" ]; then fail "$bin_dir also has an otter command: $(ls -l "$bin_dir/otter")"; fi

    printed=$(env PATH="$path" otter-net --version 2>&1 || true)
    if [ "$printed" = "Otter $language_version" ]; then pass "otter-net --version: $printed"
    else fail "otter-net --version printed '$printed', not 'Otter $language_version'"; fi
    if env PATH="$path" otter-net --help >/dev/null 2>&1; then pass "otter-net --help"; else fail "otter-net --help failed"; fi

    # tests/compat: each program in a fresh copy of the folder (its files,
    # not the subfolders), with its .input as standard input, like
    # tools/capture.ps1 and ci.yml's clean-machine check.
    count=0; native=0; handed=""; crashed=""; timed_out=""; differs=""; compared=0
    for program in "$repo"/tests/compat/*.ot; do
        count=$((count + 1))
        name=$(basename "$program")
        work="$scratch/run-$count"
        mkdir -p "$work"
        for item in "$repo"/tests/compat/*; do if [ -f "$item" ]; then cp "$item" "$work/"; fi; done
        input=/dev/null
        [ -f "$program.input" ] && input="$program.input"
        set +e
        # A 60-second limit through perl (on every Mac, and Essential on
        # Debian), since macOS has no timeout(1); a timeout ends with 142.
        (cd "$work" && env PATH="$path" perl -e 'alarm 60; exec @ARGV' otter-net run "$name" < "$input" > "$scratch/stdout" 2> "$scratch/stderr")
        code=$?
        set -e
        if grep -q "needs the PowerShell Otter" "$scratch/stderr"; then
            handed="$handed $name"
        elif [ "$code" -eq 142 ]; then
            # Stopped at the limit: programs that never end on purpose
            # (output_before_hang.ot) or wait for something.
            timed_out="$timed_out $name"
            native=$((native + 1))
        elif [ "$code" -ge 128 ] || grep -q "Unhandled exception" "$scratch/stderr"; then
            crashed="$crashed $name($code)"
        else
            native=$((native + 1))
        fi
        if [ -n "${OTTER_TEST_REFERENCE:-}" ] && [ -f "$OTTER_TEST_REFERENCE/$name.out" ]; then
            compared=$((compared + 1))
            shown=$code; [ "$code" -eq 142 ] && shown=timeout
            { printf 'exit: %s\n' "$shown"; WORK="$work" perl -pe 's/\r\n/\n/g; s/\Q$ENV{WORK}\E/<work>/g; my $w = $ENV{WORK}; $w =~ s{^/private}{}; s/\Q$w\E/<work>/g' < "$scratch/stdout"; } > "$scratch/actual"
            if ! cmp -s "$scratch/actual" "$OTTER_TEST_REFERENCE/$name.out"; then
                # Get-Content -Raw comparisons in ci.yml ignore nothing; neither does cmp.
                differs="$differs ${name%.ot}"
            fi
        fi
        rm -rf "$work"
    done
    if [ -z "$handed" ] && [ -z "$crashed" ]; then pass "tests/compat: $native of $count programs ran natively"
    else
        [ -n "$handed" ] && fail "tests/compat: needed the PowerShell Otter:$handed"
        [ -n "$crashed" ] && fail "tests/compat: crashed or timed out:$crashed"
    fi
    note "- $label: tests/compat $native of $count native, without PowerShell${timed_out:+ (stopped after 60 s:$timed_out)}"
    if [ "$compared" -gt 0 ]; then
        note "- $label: $compared compared with the reference; different:${differs:- none}"
    fi

    # A new project, as a new user would start.
    project="$scratch/project"
    rm -rf "$project"; mkdir -p "$project"
    if (cd "$project" && env PATH="$path" otter-net new console demo > "$scratch/new.txt" 2>&1) && [ -f "$project/demo/otter.json" ]; then
        pass "otter-net new console demo"
    else
        fail "otter-net new console demo: $(cat "$scratch/new.txt")"
    fi
    ran=$(cd "$project/demo" 2>/dev/null && env PATH="$path" otter-net run . 2>&1 || true)
    if [ "$ran" = "Hello from demo!" ]; then pass "otter-net run . printed: $ran"; else fail "otter-net run . printed '$ran'"; fi
    if (cd "$project/demo" && env PATH="$path" otter-net check . >/dev/null 2>&1); then pass "otter-net check ."; else fail "otter-net check ."; fi
    tested=$(cd "$project/demo" 2>/dev/null && env PATH="$path" otter-net test . 2>&1 || true)
    case "$tested" in *"1 passed"*) pass "otter-net test ." ;; *) fail "otter-net test . printed: $tested" ;; esac
    note "- $label: otter-net --version, new, run, check, test"

    # Startup: the median of 11 runs of `otter-net --version`, in milliseconds.
    times=""
    i=0
    while [ $i -lt 11 ]; do
        start=$(now_ms)
        env PATH="$path" otter-net --version >/dev/null
        end=$(now_ms)
        times="$times $((end - start))"
        i=$((i + 1))
    done
    median=$(printf '%s\n' $times | sort -n | sed -n '6p')
    note "- $label: startup (otter-net --version, median of 11): ${median} ms"
}

# Fails if any of the paths exists (also a dangling link).
check_gone() {
    label=$1; shift
    left=""
    for item in "$@"; do
        if [ -e "$item" ] || [ -L "$item" ]; then left="$left $item"; fi
    done
    if [ -z "$left" ]; then pass "$label: nothing left behind"; note "- $label: nothing left behind"
    else fail "$label: left behind:$left"; fi
}

# The PowerShell Otter's ~/.local/bin/otter launcher, as Install-Otter.ps1
# writes it, pointing at $1.
otterps_launcher() {
    printf '#!/bin/sh\n# Otter launcher - written by Install-Otter.ps1 (1.0.1)\nexec '"'"'%s'"'"' "$@"\n' "$1"
}
# An earlier otter-net preview's ~/.local/bin/otter launcher, pointing at $1.
legacy_launcher() {
    printf '#!/bin/sh\n# otter-net launcher - written by install.sh (0.9.0-preview.1)\nexec '"'"'%s'"'"' "$@"\n' "$1"
}

test_tarball() {
    archive=$1
    home="$scratch/home"
    mkdir -p "$home" "$scratch/extract"
    tar -xzf "$archive" -C "$scratch/extract"
    package=$(find "$scratch/extract" -mindepth 1 -maxdepth 1 -type d | head -1)
    [ -x "$package/install.sh" ] || abort "no install.sh in $archive"
    [ -x "$package/bin/otter-net" ] || abort "no bin/otter-net in $archive"
    [ ! -e "$package/bin/otter" ] || fail "the archive has bin/otter"
    data="$home/.local/share/otter-net"
    shim="$home/.local/bin/otter-net"
    otter_cmd="$home/.local/bin/otter"
    run_in_home() { env -u XDG_DATA_HOME HOME="$home" PATH="/usr/bin:/bin" "$@"; }

    # An older version first, to test the upgrade: the same files, VERSION 0.8.0.
    cp -R "$package" "$scratch/older"
    printf '0.8.0\n' > "$scratch/older/VERSION"
    run_in_home "$scratch/older/install.sh" > "$scratch/install.txt" 2>&1 || abort "install.sh (older): $(cat "$scratch/install.txt")"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || abort "install.sh: $(cat "$scratch/install.txt")"
    cat "$scratch/install.txt"
    if [ ! -e "$data/0.8.0" ] && [ -x "$data/$package_version/otter-net" ] && grep -q "$data/$package_version/otter-net" "$shim"; then
        pass "tarball upgrade: 0.8.0 replaced by $package_version, launcher updated"
        note "- tarball: upgrade from an older version replaces it"
    else
        fail "tarball upgrade: $(ls "$data")"
    fi
    check_otter "$home/.local/bin" "tarball (~/.local/bin/otter-net)"
    run_in_home "$data/$package_version/uninstall.sh" || fail "uninstall.sh failed"
    check_gone "tarball uninstall" "$data" "$shim" "$otter_cmd"

    # otterPS's ~/.local/bin/otter is never touched, by install or uninstall.
    mkdir -p "$home/.local/share/otter/1.0.1" "$home/.local/bin"
    printf '#!/bin/sh\necho otterPS\n' > "$home/.local/share/otter/1.0.1/otter"
    otterps_launcher "$home/.local/share/otter/1.0.1/otter" > "$otter_cmd"
    cp "$otter_cmd" "$scratch/otterps-launcher"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || fail "install.sh beside otterPS's launcher: $(cat "$scratch/install.txt")"
    if cmp -s "$otter_cmd" "$scratch/otterps-launcher" && [ -x "$shim" ]; then pass "install.sh adds otter-net and leaves otterPS's otter as it was"
    else fail "install.sh beside otterPS: otter is now $(sed -n 2p "$otter_cmd")"; fi
    if run_in_home "$package/install.sh" --replace-otterps-launcher --force > "$scratch/install.txt" 2>&1; then fail "install.sh accepted --replace-otterps-launcher"
    elif cmp -s "$otter_cmd" "$scratch/otterps-launcher"; then pass "install.sh refuses --replace-otterps-launcher: $(tail -1 "$scratch/install.txt")"
    else fail "--replace-otterps-launcher changed otter"; fi
    run_in_home "$package/uninstall.sh" > /dev/null || fail "uninstall.sh (from the package) failed"
    if cmp -s "$otter_cmd" "$scratch/otterps-launcher"; then pass "uninstall.sh leaves otterPS's otter as it was"; note "- tarball: otterPS's ~/.local/bin/otter is left exactly as it was by install.sh and uninstall.sh; --replace-otterps-launcher is refused"
    else fail "uninstall.sh changed otterPS's otter"; fi
    check_gone "tarball beside otterPS's launcher" "$data" "$shim"

    # An earlier preview took ~/.local/bin/otter (keeping otterPS's launcher
    # in otterps-launcher): installing this one gives it back.
    mkdir -p "$data/0.8.0"
    printf 'otter-net installation marker - written by install.sh\nVersion: 0.8.0\n' > "$data/0.8.0/.otter-net-install"
    printf '#!/bin/sh\necho old\n' > "$data/0.8.0/otter"
    cp "$scratch/otterps-launcher" "$data/otterps-launcher"
    legacy_launcher "$data/0.8.0/otter" > "$otter_cmd"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || fail "install.sh over an earlier preview: $(cat "$scratch/install.txt")"
    if cmp -s "$otter_cmd" "$scratch/otterps-launcher" && [ ! -e "$data/otterps-launcher" ] && [ ! -e "$data/0.8.0" ] && [ -x "$shim" ]; then
        pass "install.sh over an earlier preview gives otter back to otterPS: $(grep -i 'gave' "$scratch/install.txt")"
        note "- tarball: over an earlier preview that took ~/.local/bin/otter, install.sh gives it back to otterPS"
    else fail "after an earlier preview: otter is $(sed -n 2p "$otter_cmd"); $(ls -A "$data")"; fi
    run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the earlier preview"
    check_gone "tarball after an earlier preview" "$data" "$shim"
    cmp -s "$otter_cmd" "$scratch/otterps-launcher" || fail "otterPS's otter changed after uninstall"

    # The same, with otterPS uninstalled since: no dangling otter is left.
    rm -rf "$home/.local/share/otter" "$otter_cmd"
    mkdir -p "$data/0.8.0"
    printf 'otter-net installation marker - written by install.sh\nVersion: 0.8.0\n' > "$data/0.8.0/.otter-net-install"
    cp "$scratch/otterps-launcher" "$data/otterps-launcher"
    legacy_launcher "$data/0.8.0/otter" > "$otter_cmd"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || fail "install.sh over an earlier preview (otterPS gone)"
    if [ -e "$otter_cmd" ] || [ -L "$otter_cmd" ]; then fail "install.sh left an otter: $(cat "$otter_cmd")"
    else pass "install.sh removes an earlier preview's otter when otterPS is gone"; note "- tarball: an earlier preview's otter is removed (not handed to an otterPS that is gone)"; fi
    run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh (otterPS gone)"
    check_gone "tarball after otterPS was removed" "$data" "$shim" "$otter_cmd"

    # Downgrade: a newer version, then this one. install.sh installs what it
    # is given and removes the other marked versions: one version is left.
    cp -R "$package" "$scratch/newer"
    printf '99.0.0\n' > "$scratch/newer/VERSION"
    run_in_home "$scratch/newer/install.sh" > /dev/null 2>&1 || abort "install.sh (newer)"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || fail "install.sh over a newer version: $(cat "$scratch/install.txt")"
    if [ "$(ls "$data")" = "$package_version" ] && grep -q "$data/$package_version/otter-net" "$shim"; then
        pass "tarball downgrade: allowed, leaves only $package_version"; note "- tarball: installing an older version over a newer one is allowed and leaves only that one (install.sh has no downgrade check)"
    else fail "tarball downgrade left: $(ls "$data")"; fi
    run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the downgrade"
    check_gone "tarball after the downgrade" "$data" "$shim"

    # Any other otter-net is never replaced; any other otter is never touched.
    mkdir -p "$home/.local/bin"
    printf '#!/bin/sh\necho mine\n' > "$shim"
    if run_in_home "$package/install.sh" > /dev/null 2>&1; then fail "install.sh replaced another program's otter-net"; else pass "install.sh refuses another program's otter-net"; fi
    rm -f "$shim"
    printf '#!/bin/sh\necho mine\n' > "$otter_cmd"
    cp "$otter_cmd" "$scratch/other-otter"
    run_in_home "$package/install.sh" > /dev/null 2>&1 || fail "install.sh beside another program's otter"
    run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh beside another program's otter"
    if cmp -s "$otter_cmd" "$scratch/other-otter"; then pass "another program's otter is left alone"; else fail "another program's otter changed"; fi
    rm -f "$otter_cmd"
    check_gone "tarball beside another program's otter" "$data" "$shim"

    # A user's own link called otter (to otter-net) is not an old launcher.
    run_in_home "$package/install.sh" > /dev/null 2>&1 || fail "install.sh (user link)"
    ln -s otter-net "$otter_cmd"
    run_in_home "$package/install.sh" --force > /dev/null 2>&1 || fail "install.sh --force (user link)"
    if [ -L "$otter_cmd" ]; then pass "a user's own otter link is left alone"; else fail "install.sh removed a user's otter link"; fi
    rm -f "$otter_cmd"
    run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh (user link)"
    check_gone "tarball beside a user's otter link" "$data" "$shim"

    if [ -n "${OTTER_PRE_RENAME:-}" ]; then
        mkdir -p "$scratch/pre"
        tar -xzf "$OTTER_PRE_RENAME" -C "$scratch/pre"
        pre=$(find "$scratch/pre" -mindepth 1 -maxdepth 1 -type d | head -1)
        run_in_home "$pre/install.sh" > /dev/null 2>&1 || abort "the pre-rename install.sh failed"
        [ -f "$otter_cmd" ] && pass "the pre-rename package took ~/.local/bin/otter" || fail "the pre-rename package wrote no otter"
        run_in_home "$package/install.sh" --force > "$scratch/install.txt" 2>&1 || fail "install.sh over the pre-rename package: $(cat "$scratch/install.txt")"
        if [ ! -e "$otter_cmd" ] && [ -x "$shim" ]; then pass "install.sh over a real pre-rename package: otter removed, otter-net added"; note "- tarball: over a real pre-rename package ($(basename "$OTTER_PRE_RENAME")), ~/.local/bin/otter is removed and otter-net added"
        else fail "over the pre-rename package: otter $(ls -l "$otter_cmd" 2>&1)"; fi
        run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the pre-rename package"
        check_gone "tarball after a pre-rename package" "$data" "$shim" "$otter_cmd"
    fi
}

test_deb() {
    deb=$1
    older=${2:-}
    newer=${3:-}
    [ "$(id -u)" -eq 0 ] || abort "the deb test installs packages: run it as root in a disposable container"
    command -v apt-get >/dev/null || abort "apt-get is needed"
    [ ! -e /usr/bin/otter ] || abort "/usr/bin/otter already exists here"
    apt-get update -qq > /dev/null
    if [ -n "$older" ]; then
        apt-get install -y "$older" > "$scratch/apt.txt" 2>&1 || abort "installing $older: $(tail -20 "$scratch/apt.txt")"
        say "installed $(dpkg-query -W -f='${Package} ${Version}' otter-net)"
    fi
    apt-get install -y "$deb" > "$scratch/apt.txt" 2>&1 || abort "installing $deb: $(tail -20 "$scratch/apt.txt")"
    installed=$(dpkg-query -W -f='${Version}' otter-net)
    say "installed otter-net $installed; it depends on: $(dpkg-query -W -f='${Depends}' otter-net)"
    case "$installed" in "$(printf '%s' "$package_version" | sed 's/-/~/')") pass "dpkg: otter-net $installed" ;; *) fail "dpkg has otter-net $installed" ;; esac
    if [ -n "$older" ]; then note "- deb: upgrade from an older .deb to $installed"; fi
    [ "$(readlink -f /usr/bin/otter-net)" = /usr/lib/otter-net/otter-net ] && pass "/usr/bin/otter-net -> /usr/lib/otter-net/otter-net" || fail "/usr/bin/otter-net is $(readlink -f /usr/bin/otter-net)"
    if dpkg -L otter-net | grep -qx '/usr/bin/otter'; then fail "the .deb installs /usr/bin/otter"; else pass "the .deb has no /usr/bin/otter"; fi
    check_otter /usr/bin "deb (/usr/bin/otter-net)"
    apt-get remove --purge -y otter-net > "$scratch/apt.txt" 2>&1 || fail "apt-get remove: $(tail -20 "$scratch/apt.txt")"
    if dpkg-query -W otter-net >/dev/null 2>&1; then fail "dpkg still lists otter-net"; fi
    check_gone "deb remove" /usr/bin/otter-net /usr/bin/otter /usr/lib/otter-net /usr/share/doc/otter-net

    if [ -n "$newer" ]; then
        apt-get install -y "$newer" > "$scratch/apt.txt" 2>&1 || abort "installing $newer: $(tail -20 "$scratch/apt.txt")"
        newer_version=$(dpkg-query -W -f='${Version}' otter-net)
        if apt-get install -y "$deb" > "$scratch/apt.txt" 2>&1; then
            fail "apt-get installed $installed over the newer $newer_version without --allow-downgrades"
        elif [ "$(dpkg-query -W -f='${Version}' otter-net)" = "$newer_version" ] && [ -x /usr/lib/otter-net/otter-net ]; then
            pass "deb downgrade refused: $newer_version stays"; note "- deb: a downgrade ($newer_version to $installed) is refused by apt (needs --allow-downgrades); the newer one stays intact"
        else fail "after the refused downgrade: $(dpkg-query -W -f='${Version}' otter-net 2>&1)"; fi
        grep -i "downgrade" "$scratch/apt.txt" | head -3
        apt-get remove --purge -y otter-net > /dev/null 2>&1 || fail "apt-get remove (newer)"
        check_gone "deb remove (after the downgrade test)" /usr/bin/otter-net /usr/lib/otter-net /usr/share/doc/otter-net
    fi

    if [ -n "${OTTER_PRE_RENAME:-}" ]; then
        apt-get install -y "$OTTER_PRE_RENAME" > "$scratch/apt.txt" 2>&1 || abort "installing the pre-rename $OTTER_PRE_RENAME: $(tail -20 "$scratch/apt.txt")"
        [ -e /usr/bin/otter ] && pass "the pre-rename .deb installed /usr/bin/otter" || fail "the pre-rename .deb has no /usr/bin/otter"
        # The same version number (0.9.0~preview.1): apt would call it
        # installed already; dpkg installs the file over it.
        dpkg -i "$deb" > "$scratch/apt.txt" 2>&1 || abort "installing over the pre-rename .deb: $(tail -20 "$scratch/apt.txt")"
        if [ ! -e /usr/bin/otter ] && [ ! -L /usr/bin/otter ] && [ -x /usr/bin/otter-net ]; then pass "upgrade over a real pre-rename .deb: /usr/bin/otter gone, /usr/bin/otter-net there"; note "- deb: over a real pre-rename .deb ($(basename "$OTTER_PRE_RENAME")), /usr/bin/otter is removed and /usr/bin/otter-net installed"
        else fail "after upgrading the pre-rename .deb: $(ls -l /usr/bin/otter /usr/bin/otter-net 2>&1)"; fi
        apt-get remove --purge -y otter-net > /dev/null 2>&1 || fail "apt-get remove (pre-rename test)"
        check_gone "deb remove (after the pre-rename test)" /usr/bin/otter /usr/bin/otter-net /usr/lib/otter-net /usr/share/doc/otter-net
    fi
}

test_pkg() {
    pkg=$1
    older=${2:-}
    newer=${3:-}
    identifier=com.otterprogramming.otter-net
    lib=/usr/local/lib/otter-net
    link=/usr/local/bin/otter-net
    otter_cmd=/usr/local/bin/otter
    sudo -n true 2>/dev/null || abort "the pkg test installs into /usr/local: run it on a disposable Mac with sudo"
    for item in "$link" "$otter_cmd"; do
        if [ -e "$item" ] || [ -L "$item" ]; then abort "$item already exists on this machine"; fi
    done

    # Something else called otter-net is never replaced.
    sudo mkdir -p /usr/local/bin
    printf '#!/bin/sh\necho mine\n' | sudo tee "$link" > /dev/null
    if sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1; then fail "the .pkg replaced another program's $link"
    else pass "the .pkg refuses to replace another program's $link"; note "- pkg: another program's /usr/local/bin/otter-net is never replaced"; fi
    sudo rm -f "$link"
    sudo rm -rf "$lib"; sudo pkgutil --forget "$identifier" > /dev/null 2>&1 || true

    # Another program's otter is never touched, by install or uninstall.
    printf '#!/bin/sh\necho mine\n' | sudo tee "$otter_cmd" > /dev/null
    sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1 || abort "installing $pkg beside another otter: $(cat "$scratch/installer.txt")"
    sudo "$lib/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh beside another otter"
    if [ "$(cat "$otter_cmd")" = "$(printf '#!/bin/sh\necho mine')" ] && [ ! -L "$otter_cmd" ]; then pass "the .pkg leaves another program's $otter_cmd alone"; note "- pkg: another program's /usr/local/bin/otter is left alone by install and uninstall"
    else fail "$otter_cmd changed"; fi
    sudo rm -f "$otter_cmd"
    check_gone "pkg beside another otter" "$link" "$lib"

    # An earlier preview linked /usr/local/bin/otter: this one removes that link.
    sudo mkdir -p "$lib/0.8.0"
    printf 'otter-net installation marker - written by the otter-net .pkg\nVersion: 0.8.0\n' | sudo tee "$lib/0.8.0/.otter-net-install" > /dev/null
    sudo touch "$lib/0.8.0/otter"
    sudo ln -s "$lib/0.8.0/otter" "$otter_cmd"
    sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1 || abort "installing $pkg over an earlier preview: $(cat "$scratch/installer.txt")"
    if [ ! -e "$otter_cmd" ] && [ ! -L "$otter_cmd" ] && [ ! -e "$lib/0.8.0" ] && [ -L "$link" ]; then pass "the .pkg removes an earlier preview's $otter_cmd link"; note "- pkg: an earlier preview's /usr/local/bin/otter link is removed"
    else fail "after an earlier preview: $(ls -l "$otter_cmd" 2>&1); $(ls "$lib")"; fi
    sudo "$lib/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the earlier preview"
    check_gone "pkg after an earlier preview" "$link" "$otter_cmd" "$lib"

    if [ -n "$older" ]; then
        sudo installer -pkg "$older" -target / || abort "installing $older"
    fi
    sudo installer -pkg "$pkg" -target / || abort "installing $pkg"
    say "installed: $(pkgutil --pkg-info "$identifier" | tr '\n' ' ')"
    versions=$(ls "$lib")
    if [ "$versions" = "$package_version" ]; then pass "$lib holds only $package_version"; else fail "$lib holds: $versions"; fi
    if [ -n "$older" ]; then note "- pkg: upgrade from an older .pkg leaves only $package_version"; fi
    [ "$(readlink "$link")" = "$lib/$package_version/otter-net" ] && pass "$link -> $lib/$package_version/otter-net" || fail "$link -> $(readlink "$link")"
    if [ -e "$otter_cmd" ] || [ -L "$otter_cmd" ]; then fail "the .pkg made $otter_cmd"; else pass "no $otter_cmd"; fi
    # The runner's /usr/local/bin has PowerShell in it: test through a folder
    # with only the installed link.
    mkdir -p "$scratch/bin"
    ln -s "$link" "$scratch/bin/otter-net"
    check_otter "$scratch/bin" "pkg (/usr/local/bin/otter-net)"
    sudo "$lib/$package_version/uninstall.sh" || fail "uninstall.sh failed"
    if pkgutil --pkgs | grep -qx "$identifier"; then fail "pkgutil still has $identifier"; fi
    check_gone "pkg uninstall" "$link" "$otter_cmd" "$lib"

    if [ -n "$newer" ]; then
        sudo installer -pkg "$newer" -target / > /dev/null || abort "installing $newer"
        newer_version=$(pkgutil --pkg-info "$identifier" | sed -n 's/^version: //p')
        if sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1; then
            got=$(pkgutil --pkg-info "$identifier" | sed -n 's/^version: //p')
            if [ "$(ls "$lib")" = "$package_version" ] && [ "$(readlink "$link")" = "$lib/$package_version/otter-net" ] && [ "$got" = "$package_version" ]; then
                pass "pkg downgrade: allowed by macOS Installer, leaves only $package_version"
                note "- pkg: installing an older .pkg over a newer one ($newer_version) is allowed (macOS Installer has no downgrade check) and leaves only $package_version, linked and registered"
            else fail "pkg downgrade left: $(ls "$lib") -> $(readlink "$link"), receipt $got"; fi
        else
            pass "pkg downgrade refused"; note "- pkg: installing an older .pkg over a newer one ($newer_version) is refused"
            package_version=$newer_version
        fi
        sudo "$lib/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the downgrade test"
        check_gone "pkg uninstall (after the downgrade test)" "$link" "$lib"
    fi

    if [ -n "${OTTER_PRE_RENAME:-}" ]; then
        package_version=$(tr -d ' \t\r\n' < "$repo/VERSION")
        sudo installer -pkg "$OTTER_PRE_RENAME" -target / > "$scratch/installer.txt" 2>&1 || abort "installing the pre-rename $OTTER_PRE_RENAME: $(cat "$scratch/installer.txt")"
        [ -L "$otter_cmd" ] && pass "the pre-rename .pkg linked $otter_cmd" || fail "the pre-rename .pkg made no $otter_cmd"
        sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1 || abort "installing over the pre-rename .pkg: $(cat "$scratch/installer.txt")"
        if [ ! -e "$otter_cmd" ] && [ ! -L "$otter_cmd" ] && [ "$(readlink "$link")" = "$lib/$package_version/otter-net" ] && [ -x "$lib/$package_version/otter-net" ]; then
            pass "over a real pre-rename .pkg: $otter_cmd removed, $link installed"; note "- pkg: over a real pre-rename .pkg ($(basename "$OTTER_PRE_RENAME")), /usr/local/bin/otter is removed and /usr/local/bin/otter-net linked"
        else fail "over the pre-rename .pkg: $(ls -l "$otter_cmd" "$link" 2>&1); $(ls "$lib/$package_version" 2>&1)"; fi
        sudo "$lib/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the pre-rename test"
        check_gone "pkg uninstall (after the pre-rename test)" "$link" "$otter_cmd" "$lib"
    fi
}

test_pkg_payload() {
    pkg=$1
    pkgutil --expand-full "$pkg" "$scratch/expanded"
    otter=$(find "$scratch/expanded" -path '*/Payload/*' -name otter-net -type f | head -1)
    [ -n "$otter" ] || abort "no otter-net in the payload of $pkg"
    say "payload: $(cd "$(dirname "$otter")" && ls -A | tr '\n' ' ')"
    for script in preinstall postinstall; do
        sh -n "$(find "$scratch/expanded" -path "*/Scripts/$script" | head -1)" && pass "$script parses" || fail "$script does not parse"
    done
    check_otter "$(dirname "$otter")" "pkg payload (expanded, not installed)"
}

mode=${1:-}
[ $# -ge 2 ] || { sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
file=$2
case "$file" in /*) ;; *) file="$PWD/$file" ;; esac
older=${3:-}
case "$older" in ""|/*) ;; *) older="$PWD/$older" ;; esac
newer=${4:-}
case "$newer" in ""|/*) ;; *) newer="$PWD/$newer" ;; esac
OTTER_PRE_RENAME=${OTTER_PRE_RENAME:-}
case "$OTTER_PRE_RENAME" in ""|/*) ;; *) OTTER_PRE_RENAME="$PWD/$OTTER_PRE_RENAME" ;; esac
case "$mode" in
    tarball) test_tarball "$file" ;;
    deb) test_deb "$file" "$older" "$newer" ;;
    pkg) test_pkg "$file" "$older" "$newer" ;;
    pkg-payload) test_pkg_payload "$file" ;;
    binary) mkdir -p "$scratch/bin"; ln -s "$file" "$scratch/bin/otter-net"; check_otter "$scratch/bin" "binary" ;;
    *) abort "unknown mode $mode" ;;
esac

say ""
say "Summary ($mode $(basename "$file")):$summary"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    printf '### %s %s\n%s\n\n' "$mode" "$(basename "$file")" "$summary" >> "$GITHUB_STEP_SUMMARY"
fi
if [ "$failures" -ne 0 ]; then say "$failures check(s) failed"; exit 1; fi
say "All checks passed"
