#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPOSITORY_ROOT"

printf '[info] Restoring .NET dependencies...\n'
dotnet restore Kioku.slnx

printf '\n[ok] Kioku development environment is ready.\n'
