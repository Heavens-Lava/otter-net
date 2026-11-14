#!/usr/bin/env bash
# tools/package-distribution.sh - Build and package reproducible Otter distribution
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$ROOT_DIR/dist"
OUTPUT_DIR="${1:-$DIST_DIR/otter-1.0.1}"

echo "=========================================================="
echo " Packaging Otter 1.0.1 Distributable"
echo "=========================================================="
echo "Source: $ROOT_DIR"
echo "Output: $OUTPUT_DIR"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR/bin"
mkdir -p "$OUTPUT_DIR/library"

# 1. Build & Publish Release Binaries
echo "Publishing Otter.Cli Release binaries..."
dotnet publish "$ROOT_DIR/src/Otter.Cli/Otter.Cli.csproj" \
    -c Release \
    -p:PublishAot=false \
    -p:UseAppHost=true \
    -p:TreatWarningsAsErrors=false \
    -p:NoWarn=NU1903 \
    -o "$OUTPUT_DIR/bin" > /dev/null

# 2. Deploy Standard Library from Published Build Output
echo "Bundling standard library from build outputs..."
cp -R "$OUTPUT_DIR/bin/library/." "$OUTPUT_DIR/library/"

# 2b. Automated Distribution Bundle Integrity Verification
echo "Verifying distribution bundle integrity..."
python3 "$ROOT_DIR/tools/verify-bundle-integrity.py" "$OUTPUT_DIR/library" "$OUTPUT_DIR/bin/library"

# 3. Create Universal Portable Launchers
echo "Generating universal launchers..."

# POSIX Launcher (macOS / Linux)
cat << 'EOF' > "$OUTPUT_DIR/bin/otter"
#!/usr/bin/env bash
set -e
SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do
    DIR="$(cd -P "$(dirname "$SOURCE")" >/dev/null 2>&1 && pwd)"
    SOURCE="$(readlink "$SOURCE")"
    [[ $SOURCE != /* ]] && SOURCE="$DIR/$SOURCE"
done
BIN_DIR="$(cd -P "$(dirname "$SOURCE")" >/dev/null 2>&1 && pwd)"
OTTER_HOME="$(cd "$BIN_DIR/.." && pwd)"

# Resolve .NET runtime
DOTNET_BIN="$(command -v dotnet 2>/dev/null || true)"
if [ -z "$DOTNET_BIN" ]; then
    if [ -x "/opt/homebrew/bin/dotnet" ]; then
        DOTNET_BIN="/opt/homebrew/bin/dotnet"
    elif [ -x "/usr/local/share/dotnet/dotnet" ]; then
        DOTNET_BIN="/usr/local/share/dotnet/dotnet"
    elif [ -x "/usr/share/dotnet/dotnet" ]; then
        DOTNET_BIN="/usr/share/dotnet/dotnet"
    fi
fi

if [ -z "$DOTNET_BIN" ]; then
    echo "Error: .NET runtime not found on PATH." >&2
    echo "Please install .NET 10.0 or higher from https://dotnet.microsoft.com" >&2
    exit 1
fi

exec "$DOTNET_BIN" "$BIN_DIR/otter.dll" "$@"
EOF
chmod +x "$OUTPUT_DIR/bin/otter"

# Windows CMD Launcher
cat << 'EOF' > "$OUTPUT_DIR/bin/otter.cmd"
@echo off
setlocal
set "BIN_DIR=%~dp0"

dotnet "%BIN_DIR%otter.dll" %*
EOF

# Windows PowerShell Launcher
cat << 'EOF' > "$OUTPUT_DIR/bin/otter.ps1"
$BinDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& dotnet (Join-Path $BinDir "otter.dll") $args
EOF

# 4. Create Archive
ARCHIVE_NAME="otter-1.0.1-portable.tar.gz"
echo "Creating distributable archive: $DIST_DIR/$ARCHIVE_NAME..."
mkdir -p "$DIST_DIR"
OUTPUT_PARENT="$(dirname "$OUTPUT_DIR")"
OUTPUT_BASE="$(basename "$OUTPUT_DIR")"
tar -czf "$DIST_DIR/$ARCHIVE_NAME" -C "$OUTPUT_PARENT" "$OUTPUT_BASE"

echo "=========================================================="
echo " Distribution Build Complete!"
echo " Directory: $OUTPUT_DIR"
echo " Archive:   $DIST_DIR/$ARCHIVE_NAME"
echo "=========================================================="
