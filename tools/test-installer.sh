#!/usr/bin/env bash
# tools/test-installer.sh - Test clean-machine installation workflow in isolated sandbox
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SANDBOX_DIR="/tmp/test-clean-otter-install-$$"

echo "=========================================================="
echo " Running Automated Clean-Machine Installer Test"
echo " Sandbox: $SANDBOX_DIR"
echo "=========================================================="

cleanup() {
    echo "Cleaning up sandbox: $SANDBOX_DIR..."
    rm -rf "$SANDBOX_DIR"
}
trap cleanup EXIT

# 1. Run Installer to isolated sandbox
echo "Step 1: Invoking tools/install.sh with custom OTTER_INSTALL_DIR..."
OTTER_INSTALL_DIR="$SANDBOX_DIR" "$ROOT_DIR/tools/install.sh"

# 2. Verify Binary & Version
echo "Step 2: Testing installed binary --version..."
if [ ! -x "$SANDBOX_DIR/bin/otter" ]; then
    echo "FAIL: Expected $SANDBOX_DIR/bin/otter to be executable" >&2
    exit 1
fi
VERSION_OUTPUT="$("$SANDBOX_DIR/bin/otter" --version)"
echo "Output: $VERSION_OUTPUT"
if [ "$VERSION_OUTPUT" != "Otter 1.0.1" ]; then
    echo "FAIL: Unexpected version output '$VERSION_OUTPUT'" >&2
    exit 1
fi

# 3. Test Otter Doctor
echo "Step 3: Testing otter doctor in sandbox..."
"$SANDBOX_DIR/bin/otter" doctor

# 4. Test Offline Library Discovery from Outside Repositories
echo "Step 4: Testing offline library commands from /tmp..."
(
    cd /tmp
    "$SANDBOX_DIR/bin/otter" snippet show math-clamp > /dev/null
    "$SANDBOX_DIR/bin/otter" recipe search "contact manager" > /dev/null
    echo "  [PASS] Offline snippet & recipe discovery verified"
)

# 5. Test Project Creation, Build, and Run in Sandbox
echo "Step 5: Testing project creation and run with isolated binary..."
APP_DIR="$SANDBOX_DIR/sample_app"
(
    cd "$SANDBOX_DIR"
    "$SANDBOX_DIR/bin/otter" new console sample_app > /dev/null
    cd "$APP_DIR"
    "$SANDBOX_DIR/bin/otter" check . > /dev/null
    "$SANDBOX_DIR/bin/otter" test . > /dev/null
    RUN_OUTPUT="$("$SANDBOX_DIR/bin/otter" run .)"
    echo "  App output: $RUN_OUTPUT"
    if [ "$RUN_OUTPUT" != "Hello from sample_app!" ]; then
        echo "FAIL: Unexpected app output '$RUN_OUTPUT'" >&2
        exit 1
    fi
    echo "  [PASS] Project creation, check, test, and run verified"
)

echo "=========================================================="
echo " Clean-Machine Installer Test PASSED (100%)"
echo "=========================================================="
