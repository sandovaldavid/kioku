#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$repo_root"

echo "[info] Verifying the Kioku development toolchain..."
bash .devcontainer/scripts/verify-environment.sh

echo "[info] Restoring .NET dependencies..."
dotnet restore Kioku.slnx

echo "[info] Installing plugin dependencies from pnpm-lock.yaml..."
pnpm install --frozen-lockfile

echo "[ok] Kioku development environment is ready."
