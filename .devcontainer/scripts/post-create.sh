#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPOSITORY_ROOT"

export KIOKU_WORKSPACE="${KIOKU_WORKSPACE:-$REPOSITORY_ROOT}"

printf '[info] Configuring the shared Dev Container shell profile...\n'
bash .devcontainer/scripts/configure-shell.sh
bash .devcontainer/scripts/configure-git-ssh-signing.sh

printf '[info] Verifying the Kioku development toolchain...\n'
bash .devcontainer/scripts/verify-environment.sh

printf '[info] Installing plugin dependencies from pnpm-lock.yaml...\n'
CI=true pnpm install --frozen-lockfile

printf '[info] Restoring .NET dependencies...\n'
dotnet restore Kioku.slnx

printf '\n[ok] Kioku development environment is ready.\n'
printf '[info] Open a new terminal to load the managed shell profile and Kioku shortcuts.\n'
