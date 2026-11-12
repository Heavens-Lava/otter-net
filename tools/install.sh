#!/usr/bin/env bash
# tools/install.sh - One-command installer for Otter Language CLI & Standard Library
set -euo pipefail

OTTER_VERSION="1.0.1"
INSTALL_DIR="${OTTER_INSTALL_DIR:-$HOME/.otter}"
BIN_DIR="$INSTALL_DIR/bin"
LIB_DIR="$INSTALL_DIR/library"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" 2>/dev/null && pwd || echo "")"

echo "=========================================================="
echo " Installing Otter Programming Language v$OTTER_VERSION"
echo "=========================================================="
echo "Target directory: $INSTALL_DIR"

# 1. Verify .NET Runtime
echo -n "Checking for .NET 10.0+ runtime... "
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
    echo "FAILED"
    echo ""
    echo "Error: .NET runtime was not found." >&2
    echo "Please install .NET 10 SDK or Runtime first:" >&2
    echo "  macOS:  brew install dotnet" >&2
    echo "  Linux:  sudo apt install dotnet-sdk-10.0 (or equivalent)" >&2
    echo "  Web:    https://dotnet.microsoft.com/download" >&2
    exit 1
fi
DOTNET_VER="$("$DOTNET_BIN" --version 2>/dev/null || echo "unknown")"
echo "found ($DOTNET_VER at $DOTNET_BIN)"

# 2. Prepare Directories
mkdir -p "$BIN_DIR"
mkdir -p "$LIB_DIR"
mkdir -p "$INSTALL_DIR/packages"

# 3. Deploy Binaries and Library
if [ -n "$SCRIPT_DIR" ] && [ -d "$SCRIPT_DIR/../library" ]; then
    REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
    echo "Installing from local repository build..."
    "$SCRIPT_DIR/package-distribution.sh" "$INSTALL_DIR" > /dev/null
else
    # Packaged / standalone install fallback
    echo "Installing Otter binaries..."
    if [ -f "$INSTALL_DIR/dist/otter-1.0.1-portable.tar.gz" ]; then
        tar -xzf "$INSTALL_DIR/dist/otter-1.0.1-portable.tar.gz" -C "$INSTALL_DIR" --strip-components=1
    fi
fi

# Ensure launcher script is in place
cat << 'EOF' > "$BIN_DIR/otter"
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
    exit 1
fi

if [ -z "${OTTER_LIBRARY_PATH:-}" ]; then
    if [ -d "$OTTER_HOME/library" ]; then
        export OTTER_LIBRARY_PATH="$OTTER_HOME/library"
    elif [ -d "$BIN_DIR/library" ]; then
        export OTTER_LIBRARY_PATH="$BIN_DIR/library"
    fi
fi

exec "$DOTNET_BIN" "$BIN_DIR/otter.dll" "$@"
EOF
chmod +x "$BIN_DIR/otter"

# 4. PATH Configuration Check
echo "Configuring environment PATH..."
CURRENT_SHELL="$(basename "${SHELL:-bash}")"
RC_FILE=""
if [ "$CURRENT_SHELL" = "zsh" ]; then
    RC_FILE="$HOME/.zshrc"
elif [ "$CURRENT_SHELL" = "bash" ]; then
    if [ -f "$HOME/.bash_profile" ]; then
        RC_FILE="$HOME/.bash_profile"
    else
        RC_FILE="$HOME/.bashrc"
    fi
fi

# Link to user's standard binary directory if available on PATH
if [ "$INSTALL_DIR" = "$HOME/.otter" ] && [ -d "$HOME/.local/bin" ] && [ -w "$HOME/.local/bin" ]; then
    ln -sf "$BIN_DIR/otter" "$HOME/.local/bin/otter"
    echo "Linked $BIN_DIR/otter -> $HOME/.local/bin/otter"
fi

PATH_CMD="export PATH=\"$BIN_DIR:\$PATH\""
if [ "$INSTALL_DIR" = "$HOME/.otter" ] && [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
    if [ -n "$RC_FILE" ]; then
        touch "$RC_FILE"
        if ! grep -q "$BIN_DIR" "$RC_FILE"; then
            echo "" >> "$RC_FILE"
            echo "# Otter Programming Language" >> "$RC_FILE"
            echo "$PATH_CMD" >> "$RC_FILE"
            echo "Added $BIN_DIR to $RC_FILE"
        fi
    fi
fi

# 5. Run Verification
echo ""
echo "Verifying Otter installation..."
export PATH="$BIN_DIR:$PATH"
"$BIN_DIR/otter" --version
echo ""
"$BIN_DIR/otter" doctor

echo ""
echo "=========================================================="
echo " Otter v$OTTER_VERSION Installation Succeeded!"
echo " Binary: $BIN_DIR/otter"
echo ""
echo "To get started in your current terminal session, run:"
echo "  export PATH=\"$BIN_DIR:\$PATH\""
echo "  otter help"
echo "  otter doctor"
echo "=========================================================="
