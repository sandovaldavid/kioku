#!/usr/bin/env bash

set -euo pipefail

required_commands=(dotnet node pnpm git gh python3 shellcheck)
for command_name in "${required_commands[@]}"; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "[error] Required command is unavailable: $command_name" >&2
    exit 1
  fi
done

if [[ "$(id -u)" -eq 0 ]]; then
  echo "[error] The Dev Container must run as a non-root user." >&2
  exit 1
fi

dotnet_version="$(dotnet --version)"
node_version="$(node --version | sed 's/^v//')"
pnpm_version="$(pnpm --version)"

if [[ "$dotnet_version" != 10.* ]]; then
  echo "[error] Expected .NET 10, found $dotnet_version." >&2
  exit 1
fi

if (( ${node_version%%.*} < 24 )); then
  echo "[error] Expected Node.js 24 or newer, found $node_version." >&2
  exit 1
fi

if (( ${pnpm_version%%.*} < 11 )); then
  echo "[error] Expected pnpm 11 or newer, found $pnpm_version." >&2
  exit 1
fi

printf '[ok] user=%s home=%s\n' "$(id -un)" "$HOME"
printf '[ok] dotnet=%s node=%s pnpm=%s\n' "$dotnet_version" "$node_version" "$pnpm_version"
printf '[ok] git=%s gh=%s\n' "$(git --version | awk '{print $3}')" "$(gh --version | head -n 1 | awk '{print $3}')"

if ! git config --global --get user.name >/dev/null 2>&1 || \
   ! git config --global --get user.email >/dev/null 2>&1; then
  echo "[warn] Git identity was not detected. Configure user.name and user.email on the host, then rebuild or reopen the Dev Container."
fi

if [[ -z "${SSH_AUTH_SOCK:-}" ]]; then
  echo "[warn] SSH agent forwarding is unavailable. HTTPS Git credentials can still be reused through the host credential helper."
elif ! ssh-add -l >/dev/null 2>&1; then
  echo "[warn] SSH_AUTH_SOCK is present, but no SSH identity is currently loaded in the forwarded agent."
else
  echo "[ok] SSH agent forwarding is available."
fi
