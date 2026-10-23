#!/bin/sh
# otter-net preview kit (macOS, Linux, Chromebooks): runs real Otter programs
# with the installed `otter-net` and compares them with what Otter 1.0.1
# (otterPS) printed for the same programs, then writes a report with this
# machine's details to attach to feedback.
#
#   sh check.sh [path to otter-net]
#
# Changes nothing outside a temporary folder; sends nothing anywhere.
set -u

kit=$(cd "$(dirname "$0")" && pwd)
otter_net=${1:-$(command -v otter-net || true)}
report="$kit/preview-kit-report.txt"
work=$(mktemp -d "${TMPDIR:-/tmp}/otter-net-kit.XXXXXX")
trap 'rm -rf "$work"' EXIT
passed=0; failed=0; results=""

line() { printf '%s\n' "$*"; results="$results
$*"; }

if [ -z "$otter_net" ] || [ ! -x "$otter_net" ]; then
    echo "otter-net was not found. Open a new terminal after installing it, or run: sh check.sh /path/to/otter-net" >&2
    exit 2
fi

# 60 seconds per program at most (perl is on every Mac and Debian).
limited() { perl -e 'alarm 60; exec @ARGV' "$@"; }

line "== Programs (output compared with Otter 1.0.1)"
for program in "$kit"/programs/*.ot; do
    name=$(basename "$program" .ot)
    run="$work/run-$name"
    mkdir -p "$run"
    cp "$kit"/programs/*.ot "$run/"
    (cd "$run" && limited "$otter_net" run "$name.ot" < /dev/null > "$run/out.txt" 2> "$run/err.txt")
    code=$?
    tr -d '\r' < "$run/out.txt" > "$run/out.lf"
    tr -d '\r' < "$kit/expected/$name.out" > "$run/expected.lf"
    if [ "$code" -eq 0 ] && cmp -s "$run/out.lf" "$run/expected.lf" && [ ! -s "$run/err.txt" ]; then
        line "PASS  $name"; passed=$((passed + 1))
    else
        line "FAIL  $name (exit $code)"; failed=$((failed + 1))
        line "$(diff "$run/expected.lf" "$run/out.lf" | sed 's/^/      /' | head -20)"
        [ -s "$run/err.txt" ] && line "$(sed 's/^/      stderr: /' "$run/err.txt" | head -10)"
    fi
done

line "== Web pages (compiled HTML compared with Otter 1.0.1's)"
for program in "$kit"/web/*.ot; do
    name=$(basename "$program" .ot)
    run="$work/web-$name"
    mkdir -p "$run"
    cp "$program" "$run/"
    (cd "$run" && limited "$otter_net" web "$name.ot" -NoOpen > "$run/out.txt" 2>&1)
    code=$?
    if [ "$code" -eq 0 ] && [ -f "$run/$name.html" ] && [ "$(tr -d '\r' < "$run/$name.html" | cksum)" = "$(tr -d '\r' < "$kit/expected/$name.html" | cksum)" ]; then
        line "PASS  $name.html"; passed=$((passed + 1))
    else
        line "FAIL  $name.html (exit $code): $(head -3 "$run/out.txt")"; failed=$((failed + 1))
    fi
done

line "== A new project"
(cd "$work" && limited "$otter_net" new console demo > "$work/new.txt" 2>&1)
ran=$(cd "$work/demo" 2>/dev/null && limited "$otter_net" run . 2>&1)
tested=$(cd "$work/demo" 2>/dev/null && limited "$otter_net" test . 2>&1)
if [ "$ran" = "Hello from demo!" ] && printf '%s' "$tested" | grep -q "1 passed"; then
    line "PASS  new console demo, run, test"; passed=$((passed + 1))
else
    line "FAIL  new console demo: run printed '$ran'; test printed '$tested'"; failed=$((failed + 1))
fi

line ""
line "$passed passed, $failed failed"

# The machine's details, for the report.
{
    echo "otter-net preview kit report - $(date -u '+%Y-%m-%d %H:%M UTC')"
    echo
    echo "== Machine"
    if [ "$(uname)" = Darwin ]; then
        echo "macOS $(sw_vers -productVersion) ($(sw_vers -buildVersion)), $(uname -m)"
    else
        [ -f /etc/os-release ] && . /etc/os-release && echo "${PRETTY_NAME:-Linux}, $(uname -m)"
        [ -d /opt/google/cros-containers ] && echo "Chromebook (Linux development environment)"
        echo "kernel $(uname -r)"
    fi
    echo
    echo "== otter-net"
    echo "path: $otter_net"
    real=$otter_net
    if [ -L "$otter_net" ]; then
        real=$(readlink "$otter_net")
        case "$real" in /*) ;; *) real="$(dirname "$otter_net")/$real" ;; esac
    elif LC_ALL=C grep -q "^# otter-net launcher" "$otter_net" 2>/dev/null; then
        real=$(sed -n "3s/^exec '\\(.*\\)' .*/\\1/p" "$otter_net")
    fi
    echo "runs: $real"
    echo "--version: $("$otter_net" --version 2>&1)"
    for folder in "$(dirname "$real")" "$(dirname "$real")/.." /usr/lib/otter-net; do
        if [ -f "$folder/VERSION" ]; then echo "package version: $(cat "$folder/VERSION")"; break; fi
    done
    LC_ALL=C grep -q "^# otter-net launcher" "$otter_net" 2>/dev/null && echo "installed by: install.sh (tar.gz)"
    case "$real" in /usr/local/lib/otter-net/*) echo "installed by: .pkg" ;; */lib/otter-net/*) echo "installed by: .deb" ;; esac
    echo
    echo "== otter (should be otterPS, or not found)"
    found=$(command -v otter || true)
    if [ -z "$found" ]; then echo "otter: not found"
    else
        echo "otter: $found"
        echo "second line: $(sed -n 2p "$found" 2>/dev/null)"
    fi
    echo
    echo "== Results$results"
} > "$report"
echo
echo "Report written to $report - attach it to your feedback."
[ "$failed" -eq 0 ]
