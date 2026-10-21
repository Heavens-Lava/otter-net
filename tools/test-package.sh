#!/bin/sh
# Tests an otter-net package the way a user gets it, on a machine without
# PowerShell: installs it, runs the installed `otter` (its version,
# tests/compat, and a new project), uninstalls it and checks that nothing is
# left behind. A POSIX shell script, so it needs nothing PowerShell; run it
# from the repository (it reads tests/compat and src/Otter.Cli/Program.cs).
#
#   tools/test-package.sh tarball <otter-net-*.tar.gz>          per-user, in a temporary HOME
#   tools/test-package.sh deb <otter-net_*.deb> [<older .deb> [<newer .deb>]]  as root (a disposable container)
#   tools/test-package.sh pkg <otter-net-*.pkg> [<older .pkg> [<newer .pkg>]]  with sudo (a disposable Mac)
#   tools/test-package.sh pkg-payload <otter-net-*.pkg>         no installation: the expanded payload
#   tools/test-package.sh binary <path to otter>                the checks alone
#
# Every `otter` runs with PATH=/usr/bin:/bin (and the folder of the otter
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

# A clean PATH: the system's own folders and the otter under test. $1 is the
# folder that holds the `otter` command.
clean_path() {
    printf '%s' "$1:/usr/bin:/bin:/usr/sbin:/sbin"
}

# Milliseconds, for timing: perl's Time::HiRes (macOS), or GNU date's %N.
now_ms() {
    perl -MTime::HiRes=time -e 'printf "%.0f", time * 1000' 2>/dev/null || echo $(( $(date +%s%N) / 1000000 ))
}

# check_otter <folder holding otter> <label>
check_otter() {
    bin_dir=$1
    label=$2
    path=$(clean_path "$bin_dir")
    say "== $label: otter from $bin_dir, PATH=$path"
    for shell_name in pwsh powershell powershell.exe pwsh.exe; do
        if env PATH="$path" sh -c "command -v $shell_name" >/dev/null 2>&1; then
            abort "$shell_name is on the test PATH; this is not a machine without PowerShell"
        fi
    done
    resolved=$(env PATH="$path" sh -c 'command -v otter' || true)
    [ "$resolved" = "$bin_dir/otter" ] || abort "otter resolves to '$resolved', not $bin_dir/otter"

    printed=$(env PATH="$path" otter --version 2>&1 || true)
    if [ "$printed" = "Otter $language_version" ]; then pass "otter --version: $printed"
    else fail "otter --version printed '$printed', not 'Otter $language_version'"; fi
    if env PATH="$path" otter --help >/dev/null 2>&1; then pass "otter --help"; else fail "otter --help failed"; fi

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
        (cd "$work" && env PATH="$path" perl -e 'alarm 60; exec @ARGV' otter run "$name" < "$input" > "$scratch/stdout" 2> "$scratch/stderr")
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
    if (cd "$project" && env PATH="$path" otter new console demo > "$scratch/new.txt" 2>&1) && [ -f "$project/demo/otter.json" ]; then
        pass "otter new console demo"
    else
        fail "otter new console demo: $(cat "$scratch/new.txt")"
    fi
    ran=$(cd "$project/demo" 2>/dev/null && env PATH="$path" otter run . 2>&1 || true)
    if [ "$ran" = "Hello from demo!" ]; then pass "otter run . printed: $ran"; else fail "otter run . printed '$ran'"; fi
    if (cd "$project/demo" && env PATH="$path" otter check . >/dev/null 2>&1); then pass "otter check ."; else fail "otter check ."; fi
    tested=$(cd "$project/demo" 2>/dev/null && env PATH="$path" otter test . 2>&1 || true)
    case "$tested" in *"1 passed"*) pass "otter test ." ;; *) fail "otter test . printed: $tested" ;; esac
    note "- $label: otter --version, new, run, check, test"

    # Startup: the median of 11 runs of `otter --version`, in milliseconds.
    times=""
    i=0
    while [ $i -lt 11 ]; do
        start=$(now_ms)
        env PATH="$path" otter --version >/dev/null
        end=$(now_ms)
        times="$times $((end - start))"
        i=$((i + 1))
    done
    median=$(printf '%s\n' $times | sort -n | sed -n '6p')
    note "- $label: startup (otter --version, median of 11): ${median} ms"
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

test_tarball() {
    archive=$1
    home="$scratch/home"
    mkdir -p "$home" "$scratch/extract"
    tar -xzf "$archive" -C "$scratch/extract"
    package=$(find "$scratch/extract" -mindepth 1 -maxdepth 1 -type d | head -1)
    [ -x "$package/install.sh" ] || abort "no install.sh in $archive"
    data="$home/.local/share/otter-net"
    shim="$home/.local/bin/otter"
    run_in_home() { env -u XDG_DATA_HOME HOME="$home" PATH="/usr/bin:/bin" "$@"; }

    # An older version first, to test the upgrade: the same files, VERSION 0.8.0.
    cp -R "$package" "$scratch/older"
    printf '0.8.0\n' > "$scratch/older/VERSION"
    run_in_home "$scratch/older/install.sh" > "$scratch/install.txt" 2>&1 || abort "install.sh (older): $(cat "$scratch/install.txt")"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || abort "install.sh: $(cat "$scratch/install.txt")"
    cat "$scratch/install.txt"
    if [ ! -e "$data/0.8.0" ] && [ -x "$data/$package_version/otter" ] && grep -q "$data/$package_version/otter" "$shim"; then
        pass "tarball upgrade: 0.8.0 replaced by $package_version, launcher updated"
        note "- tarball: upgrade from an older version replaces it"
    else
        fail "tarball upgrade: $(ls "$data")"
    fi
    check_otter "$home/.local/bin" "tarball (~/.local/bin/otter)"
    run_in_home "$data/$package_version/uninstall.sh" || fail "uninstall.sh failed"
    check_gone "tarball uninstall" "$data" "$shim"

    # The PowerShell Otter's launcher is never replaced unasked, and comes back.
    mkdir -p "$home/.local/bin"
    printf '#!/bin/sh\n# Otter launcher - written by Install-Otter.ps1 (1.0.1)\nexec pwsh -NoProfile -File otter.ps1 "$@"\n' > "$shim"
    cp "$shim" "$scratch/otterps-launcher"
    if run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1; then fail "install.sh replaced otterPS's launcher unasked"
    elif [ -e "$data" ]; then fail "a refused install.sh left $data behind"
    else pass "install.sh refuses to replace otterPS's launcher unasked, and installs nothing"; fi
    run_in_home "$package/install.sh" --replace-otterps-launcher > "$scratch/install.txt" 2>&1 || fail "install.sh --replace-otterps-launcher: $(cat "$scratch/install.txt")"
    grep -q "otter-net launcher" "$shim" && pass "otter-net took the otter command" || fail "the launcher is not otter-net's"
    run_in_home "$package/uninstall.sh" > /dev/null || fail "uninstall.sh (from the package) failed"
    if cmp -s "$shim" "$scratch/otterps-launcher"; then pass "uninstall.sh put otterPS's launcher back"; note "- tarball: otterPS's launcher kept unless asked, and put back"
    else fail "otterPS's launcher was not put back"; fi
    rm -f "$shim"
    check_gone "tarball with otterPS's launcher" "$data"

    # otterPS uninstalled while otter-net held the command: otter-net's
    # uninstall.sh must not put back a launcher whose installation is gone.
    mkdir -p "$home/.local/share/otter/1.0.1"
    printf '#!/bin/sh\n# Otter launcher - written by Install-Otter.ps1 (1.0.1)\nexec '"'"'%s'"'"' "$@"\n' "$home/.local/share/otter/1.0.1/otter" > "$shim"
    run_in_home "$package/install.sh" --replace-otterps-launcher > /dev/null 2>&1 || fail "install.sh --replace-otterps-launcher (second time)"
    rm -rf "$home/.local/share/otter"
    run_in_home "$package/uninstall.sh" > "$scratch/uninstall.txt" 2>&1 || fail "uninstall.sh after otterPS was removed"
    if [ -e "$shim" ] || [ -L "$shim" ]; then fail "uninstall.sh put back the launcher of an otterPS that is no longer installed: $(cat "$shim")"
    else pass "uninstall.sh does not put back a removed otterPS's launcher"; note "- tarball: a removed otterPS's launcher is not put back (no dangling otter command)"; fi
    check_gone "tarball after otterPS was removed" "$data"

    # Downgrade: a newer version, then this one. install.sh installs what it
    # is given and removes the other marked versions: one version is left.
    cp -R "$package" "$scratch/newer"
    printf '99.0.0\n' > "$scratch/newer/VERSION"
    run_in_home "$scratch/newer/install.sh" > /dev/null 2>&1 || abort "install.sh (newer)"
    run_in_home "$package/install.sh" > "$scratch/install.txt" 2>&1 || fail "install.sh over a newer version: $(cat "$scratch/install.txt")"
    if [ "$(ls "$data")" = "$package_version" ] && grep -q "$data/$package_version/otter" "$shim"; then
        pass "tarball downgrade: allowed, leaves only $package_version"; note "- tarball: installing an older version over a newer one is allowed and leaves only that one (install.sh has no downgrade check)"
    else fail "tarball downgrade left: $(ls "$data")"; fi
    run_in_home "$data/$package_version/uninstall.sh" > /dev/null || fail "uninstall.sh after the downgrade"
    check_gone "tarball after the downgrade" "$data" "$shim"

    # Any other otter is never replaced.
    printf '#!/bin/sh\necho mine\n' > "$shim"
    if run_in_home "$package/install.sh" > /dev/null 2>&1; then fail "install.sh replaced another program's otter"; else pass "install.sh refuses another program's otter"; fi
    rm -f "$shim"
}

test_deb() {
    deb=$1
    older=${2:-}
    newer=${3:-}
    [ "$(id -u)" -eq 0 ] || abort "the deb test installs packages: run it as root in a disposable container"
    command -v apt-get >/dev/null || abort "apt-get is needed"
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
    [ "$(readlink -f /usr/bin/otter)" = /usr/lib/otter-net/otter ] && pass "/usr/bin/otter -> /usr/lib/otter-net/otter" || fail "/usr/bin/otter is $(readlink -f /usr/bin/otter)"
    check_otter /usr/bin "deb (/usr/bin/otter)"
    apt-get remove --purge -y otter-net > "$scratch/apt.txt" 2>&1 || fail "apt-get remove: $(tail -20 "$scratch/apt.txt")"
    if dpkg-query -W otter-net >/dev/null 2>&1; then fail "dpkg still lists otter-net"; fi
    check_gone "deb remove" /usr/bin/otter /usr/lib/otter-net /usr/share/doc/otter-net

    if [ -n "$newer" ]; then
        apt-get install -y "$newer" > "$scratch/apt.txt" 2>&1 || abort "installing $newer: $(tail -20 "$scratch/apt.txt")"
        newer_version=$(dpkg-query -W -f='${Version}' otter-net)
        if apt-get install -y "$deb" > "$scratch/apt.txt" 2>&1; then
            fail "apt-get installed $installed over the newer $newer_version without --allow-downgrades"
        elif [ "$(dpkg-query -W -f='${Version}' otter-net)" = "$newer_version" ] && [ -x /usr/lib/otter-net/otter ]; then
            pass "deb downgrade refused: $newer_version stays"; note "- deb: a downgrade ($newer_version to $installed) is refused by apt (needs --allow-downgrades); the newer one stays intact"
        else fail "after the refused downgrade: $(dpkg-query -W -f='${Version}' otter-net 2>&1)"; fi
        grep -i "downgrade" "$scratch/apt.txt" | head -3
        apt-get remove --purge -y otter-net > /dev/null 2>&1 || fail "apt-get remove (newer)"
        check_gone "deb remove (after the downgrade test)" /usr/bin/otter /usr/lib/otter-net /usr/share/doc/otter-net
    fi
}

test_pkg() {
    pkg=$1
    older=${2:-}
    newer=${3:-}
    identifier=com.otterprogramming.otter-net
    lib=/usr/local/lib/otter-net
    link=/usr/local/bin/otter
    sudo -n true 2>/dev/null || abort "the pkg test installs into /usr/local: run it on a disposable Mac with sudo"
    if [ -e "$link" ] || [ -L "$link" ]; then abort "$link already exists on this machine"; fi

    # Something else called otter is never replaced.
    sudo mkdir -p /usr/local/bin
    printf '#!/bin/sh\necho mine\n' | sudo tee "$link" > /dev/null
    if sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1; then fail "the .pkg replaced another program's $link"
    else pass "the .pkg refuses to replace another program's $link"; note "- pkg: another program's /usr/local/bin/otter is never replaced"; fi
    sudo rm -f "$link"
    sudo rm -rf "$lib"; sudo pkgutil --forget "$identifier" > /dev/null 2>&1 || true

    if [ -n "$older" ]; then
        sudo installer -pkg "$older" -target / || abort "installing $older"
    fi
    sudo installer -pkg "$pkg" -target / || abort "installing $pkg"
    say "installed: $(pkgutil --pkg-info "$identifier" | tr '\n' ' ')"
    versions=$(ls "$lib")
    if [ "$versions" = "$package_version" ]; then pass "$lib holds only $package_version"; else fail "$lib holds: $versions"; fi
    if [ -n "$older" ]; then note "- pkg: upgrade from an older .pkg leaves only $package_version"; fi
    [ "$(readlink "$link")" = "$lib/$package_version/otter" ] && pass "$link -> $lib/$package_version/otter" || fail "$link -> $(readlink "$link")"
    # The runner's /usr/local/bin has PowerShell in it: test through a folder
    # with only the installed link.
    mkdir -p "$scratch/bin"
    ln -s "$link" "$scratch/bin/otter"
    check_otter "$scratch/bin" "pkg (/usr/local/bin/otter)"
    sudo "$lib/$package_version/uninstall.sh" || fail "uninstall.sh failed"
    if pkgutil --pkgs | grep -qx "$identifier"; then fail "pkgutil still has $identifier"; fi
    check_gone "pkg uninstall" "$link" "$lib"

    if [ -n "$newer" ]; then
        sudo installer -pkg "$newer" -target / > /dev/null || abort "installing $newer"
        newer_version=$(pkgutil --pkg-info "$identifier" | sed -n 's/^version: //p')
        if sudo installer -pkg "$pkg" -target / > "$scratch/installer.txt" 2>&1; then
            got=$(pkgutil --pkg-info "$identifier" | sed -n 's/^version: //p')
            if [ "$(ls "$lib")" = "$package_version" ] && [ "$(readlink "$link")" = "$lib/$package_version/otter" ] && [ "$got" = "$package_version" ]; then
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
}

test_pkg_payload() {
    pkg=$1
    pkgutil --expand-full "$pkg" "$scratch/expanded"
    otter=$(find "$scratch/expanded" -path '*/Payload/*' -name otter -type f | head -1)
    [ -n "$otter" ] || abort "no otter in the payload of $pkg"
    say "payload: $(cd "$(dirname "$otter")" && ls -A | tr '\n' ' ')"
    for script in preinstall postinstall; do
        sh -n "$(find "$scratch/expanded" -path "*/Scripts/$script" | head -1)" && pass "$script parses" || fail "$script does not parse"
    done
    check_otter "$(dirname "$otter")" "pkg payload (expanded, not installed)"
}

mode=${1:-}
[ $# -ge 2 ] || { sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
file=$2
case "$file" in /*) ;; *) file="$PWD/$file" ;; esac
older=${3:-}
case "$older" in ""|/*) ;; *) older="$PWD/$older" ;; esac
newer=${4:-}
case "$newer" in ""|/*) ;; *) newer="$PWD/$newer" ;; esac
case "$mode" in
    tarball) test_tarball "$file" ;;
    deb) test_deb "$file" "$older" "$newer" ;;
    pkg) test_pkg "$file" "$older" "$newer" ;;
    pkg-payload) test_pkg_payload "$file" ;;
    binary) mkdir -p "$scratch/bin"; ln -s "$file" "$scratch/bin/otter"; check_otter "$scratch/bin" "binary" ;;
    *) abort "unknown mode $mode" ;;
esac

say ""
say "Summary ($mode $(basename "$file")):$summary"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    printf '### %s %s\n%s\n\n' "$mode" "$(basename "$file")" "$summary" >> "$GITHUB_STEP_SUMMARY"
fi
if [ "$failures" -ne 0 ]; then say "$failures check(s) failed"; exit 1; fi
say "All checks passed"
