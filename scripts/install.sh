#!/usr/bin/env bash
# One-line installer for the Kioku MCP server.
# Usage: curl -fsSL https://raw.githubusercontent.com/sandovaldavid/kioku/main/scripts/install.sh | bash
set -euo pipefail

REPO="sandovaldavid/kioku"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"
BIN_NAME="kioku"

# Detect OS/ARCH for release artifact naming.
detect_target() {
    local os arch
    os=$(uname -s)
    arch=$(uname -m)

    case "$os" in
        Linux*)     os="linux" ;;
        Darwin*)    os="osx" ;;
        MINGW*|MSYS*|CYGWIN*) os="win"; INSTALL_DIR="${INSTALL_DIR:-$HOME/.bin}" ;;
        *)          echo "Unsupported OS: $os"; exit 1 ;;
    esac

    case "$arch" in
        x86_64|amd64) arch="x64" ;;
        arm64|aarch64) arch="arm64" ;;
        *)            echo "Unsupported architecture: $arch"; exit 1 ;;
    esac

    if [ "$os" = "win" ]; then
        echo "kioku-server-${os}-${arch}.exe"
    else
        echo "kioku-server-${os}-${arch}"
    fi
}

fetch_latest_release() {
    curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" |
        grep '"tag_name":' |
        sed -E 's/.*"tag_name": "([^"]+)".*/\1/'
}

main() {
    local asset_name version asset_url tmpdir
    asset_name=$(detect_target)
    version=$(fetch_latest_release)

    if [ -z "$version" ]; then
        echo "Could not determine the latest release." >&2
        exit 1
    fi

    asset_url="https://github.com/${REPO}/releases/download/${version}/${asset_name}"

    echo "Installing Kioku ${version} (${asset_name})..."
    tmpdir=$(mktemp -d)
    trap 'rm -rf "$tmpdir"' EXIT

    curl -fsSL -o "${tmpdir}/${BIN_NAME}" "$asset_url"

    mkdir -p "$INSTALL_DIR"
    cp "${tmpdir}/${BIN_NAME}" "${INSTALL_DIR}/${BIN_NAME}"
    chmod +x "${INSTALL_DIR}/${BIN_NAME}"

    echo "Kioku ${version} installed to ${INSTALL_DIR}/${BIN_NAME}"
    echo "Make sure ${INSTALL_DIR} is on your PATH."
    echo "Set KIOKU_VAULT_PATH and run: kioku"
}

main "$@"
