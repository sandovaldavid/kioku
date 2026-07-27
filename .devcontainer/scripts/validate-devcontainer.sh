#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$repo_root"

echo "[info] Normalizing host-side Dev Container permissions..."
bash .devcontainer/scripts/initialize-host.sh

if ! command -v devcontainer >/dev/null 2>&1; then
  echo "[error] The Dev Container CLI is required." >&2
  echo "Install it from VS Code or with: npm install -g @devcontainers/cli" >&2
  exit 1
fi

echo "[info] Reading the resolved Dev Container configuration..."
devcontainer read-configuration --workspace-folder . >/dev/null

echo "[info] Building with the committed Feature lockfile..."
devcontainer build --workspace-folder . --frozen-lockfile

echo "[info] Starting the Dev Container..."
devcontainer up --workspace-folder . --frozen-lockfile >/dev/null

echo "[info] Running repository quality gates inside the Dev Container..."
devcontainer exec --workspace-folder . bash -lc '
  set -euo pipefail

  bash .devcontainer/scripts/verify-environment.sh

  dotnet restore Kioku.slnx
  dotnet build Kioku.slnx --configuration Release --no-restore
  dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj \
    --configuration Release \
    --no-restore \
    --verbosity minimal
  dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
  dotnet format Kioku.slnx style --verify-no-changes --no-restore
  dotnet list Kioku.slnx package --vulnerable --include-transitive

  pnpm install --frozen-lockfile
  pnpm audit --audit-level=high

  node scripts/lib/validate-skill-frontmatter.mjs
  ./scripts/sync-skill.sh --check
  shellcheck .devcontainer/scripts/*.sh \
    scripts/add-to-client.sh \
    scripts/sync-skill.sh \
    scripts/install.sh \
    scripts/sync-develop-to-main.sh
'

echo "[ok] Dev Container and repository validation completed successfully."
