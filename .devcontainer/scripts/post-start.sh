#!/usr/bin/env bash

set -euo pipefail

if [[ "${DEVCONTAINER:-}" != "true" ]]; then
  exit 0
fi

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$repo_root"

export KIOKU_WORKSPACE="${KIOKU_WORKSPACE:-$repo_root}"
export STARSHIP_CONFIG="${STARSHIP_CONFIG:-$repo_root/.devcontainer/shell/starship.toml}"

owner_uid="$(id -u)"
owner_gid="$(id -g)"
workspace_uid="$(stat -c '%u' "$repo_root")"
workspace_gid="$(stat -c '%g' "$repo_root")"

if [[ "$workspace_uid" != "$owner_uid" ]]; then
  cat >&2 <<EOF
[error] Dev Container identity mismatch.
- container UID:GID: ${owner_uid}:${owner_gid}
- workspace UID:GID: ${workspace_uid}:${workspace_gid}
Rebuild the container without cache so updateRemoteUserUID can synchronize ownership.
EOF
  exit 1
fi

if [[ ! -w "$repo_root" ]]; then
  echo "[error] Repository root is not writable by $(id -un): $repo_root" >&2
  exit 1
fi

bash .devcontainer/scripts/configure-shell.sh >/dev/null

generated_paths=(
  artifacts
  test-results
  src/obsidian-kioku-mcp/coverage
  src/obsidian-kioku-mcp/node_modules
)

for path in "${generated_paths[@]}"; do
  if [[ -e "$path" ]] && [[ ! -L "$path" ]] && [[ ! -w "$path" ]]; then
    sudo chown -R --no-dereference "${owner_uid}:${owner_gid}" -- "$path"
    sudo chmod -R u+rwX -- "$path"
  fi
done

printf '[ok] Kioku workspace is writable by %s (%s:%s).\n' \
  "$(id -un)" "$owner_uid" "$owner_gid"
