#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$repo_root"

export KIOKU_WORKSPACE="${KIOKU_WORKSPACE:-$repo_root}"
export STARSHIP_CONFIG="${STARSHIP_CONFIG:-$repo_root/.devcontainer/shell/starship.toml}"

echo "[info] Configuring the Kioku Zsh and Starship experience..."
bash .devcontainer/scripts/configure-shell.sh

echo "[info] Verifying the Kioku development toolchain..."
bash .devcontainer/scripts/verify-environment.sh

echo "[info] Restoring .NET dependencies..."
dotnet restore Kioku.slnx

echo "[info] Installing plugin dependencies from pnpm-lock.yaml..."
pnpm install --frozen-lockfile

echo "[ok] Kioku development environment is ready."
echo "[info] Open a new terminal to load the project prompt and Kioku command shortcuts."
