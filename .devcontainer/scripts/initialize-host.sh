#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
devcontainer_dir="$repo_root/.devcontainer"
scripts_dir="$devcontainer_dir/scripts"

if [[ ! -d "$devcontainer_dir" ]]; then
  echo "[error] Dev Container directory was not found: $devcontainer_dir" >&2
  exit 1
fi

if ! find "$devcontainer_dir" -type d -exec chmod 0755 {} +; then
  echo "[error] Unable to normalize Dev Container directory permissions on the host." >&2
  exit 1
fi

if ! find "$devcontainer_dir" -type f -exec chmod 0644 {} +; then
  echo "[error] Unable to normalize Dev Container file permissions on the host." >&2
  exit 1
fi

if [[ -d "$scripts_dir" ]]; then
  if ! find "$scripts_dir" -type f -name '*.sh' -exec chmod 0755 {} +; then
    echo "[error] Unable to mark Dev Container scripts as executable on the host." >&2
    exit 1
  fi
fi

for script_path in "$scripts_dir"/*.sh; do
  if [[ ! -r "$script_path" ]] || [[ ! -x "$script_path" ]]; then
    echo "[error] Script permissions remain invalid after host initialization: $script_path" >&2
    exit 1
  fi
done

printf '[ok] Host-side Dev Container permissions are normalized.\n'
