#!/bin/sh
# Checks the audit's sample programs with otter-net: every program must pass
# `otter-net check`, except those in EXPECTED_REJECTED.txt (forms the docs
# show but Otter 1.0.1 rejects), which must still be rejected.
#
#   sh docs/web/samples/check.sh [path to otter-net]
set -u
here=$(cd "$(dirname "$0")" && pwd)
otter_net=${1:-$(command -v otter-net || true)}
[ -n "$otter_net" ] || { echo "otter-net not found; pass its path" >&2; exit 2; }
valid=0; rejected=0; wrong=""
for f in $(cd "$here" && find . -name '*.ot' | sed 's|^\./||' | sort); do
    if (cd "$here/$(dirname "$f")" && perl -e 'alarm 20; exec @ARGV' "$otter_net" check "$(basename "$f")" > /dev/null 2>&1); then ok=yes; else ok=no; fi
    if grep -qxF "$f" "$here/EXPECTED_REJECTED.txt"; then
        if [ "$ok" = no ]; then rejected=$((rejected + 1)); else wrong="$wrong $f(now-valid)"; fi
    else
        if [ "$ok" = yes ]; then valid=$((valid + 1)); else wrong="$wrong $f(rejected)"; fi
    fi
done
echo "valid: $valid, rejected as expected: $rejected"
[ -z "$wrong" ] || { echo "UNEXPECTED:$wrong"; exit 1; }
