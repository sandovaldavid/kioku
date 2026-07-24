#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
export KIOKU_WORKSPACE="${KIOKU_WORKSPACE:-$REPOSITORY_ROOT}"
export STARSHIP_CONFIG="${STARSHIP_CONFIG:-$HOME/.config/starship.toml}"

for script_path in "$REPOSITORY_ROOT"/.devcontainer/scripts/*.sh; do
  if [[ ! -r "$script_path" ]] || [[ ! -x "$script_path" ]]; then
    echo "[error] Dev Container script is not readable and executable: $script_path" >&2
    echo "Reopen or rebuild the Dev Container so initializeCommand can normalize host-side permissions." >&2
    exit 1
  fi
done

required_commands=(
  bat
  dotnet
  eza
  fd
  gh
  git
  node
  pnpm
  python3
  rg
  shellcheck
  starship
  zsh
)

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
starship_version="$(starship --version | head -n 1 | awk '{print $2}')"
eza_version="$(eza --version | head -n 1 | awk '{print $2}')"
login_shell="$(getent passwd "$(id -un)" | cut -d: -f7)"

if [[ "$dotnet_version" != 10.* ]]; then
  echo "[error] Expected .NET 10, found $dotnet_version." >&2
  exit 1
fi

if [[ "${node_version%%.*}" != "24" ]]; then
  echo "[error] Expected Node.js 24, found $node_version." >&2
  exit 1
fi

if [[ "$pnpm_version" != "11.9.0" ]]; then
  echo "[error] Expected pnpm 11.9.0, found $pnpm_version." >&2
  exit 1
fi

if [[ "$starship_version" != "1.26.0" ]]; then
  echo "[error] Expected Starship 1.26.0, found $starship_version." >&2
  exit 1
fi

if [[ "$eza_version" != "v0.23.5" && "$eza_version" != "0.23.5" ]]; then
  echo "[error] Expected eza 0.23.5, found $eza_version." >&2
  exit 1
fi

if [[ "$login_shell" != */zsh ]]; then
  echo "[error] Expected Zsh as the login shell, found $login_shell." >&2
  exit 1
fi

required_shell_files=(
  "$HOME/.config/devcontainer/shell.bash"
  "$HOME/.config/devcontainer/shell.zsh"
  "$HOME/.config/starship.toml"
  "$HOME/.local/share/zsh/plugins/zsh-autosuggestions-0.7.1/zsh-autosuggestions.zsh"
  "$HOME/.local/share/zsh/plugins/zsh-completions-0.36.0/zsh-completions.plugin.zsh"
  "$HOME/.local/share/zsh/plugins/zsh-history-substring-search-1.1.0/zsh-history-substring-search.zsh"
  "$HOME/.local/share/zsh/plugins/zsh-syntax-highlighting-0.8.0/zsh-syntax-highlighting.zsh"
)

for required_file in "${required_shell_files[@]}"; do
  if [[ ! -f "$required_file" ]]; then
    echo "[error] Managed shell component is unavailable: $required_file" >&2
    exit 1
  fi
done

starship print-config >/dev/null

printf '[ok] user=%s shell=%s home=%s\n' "$(id -un)" "$login_shell" "$HOME"
printf '[ok] dotnet=%s node=%s pnpm=%s starship=%s eza=%s\n' \
  "$dotnet_version" "$node_version" "$pnpm_version" "$starship_version" "$eza_version"
printf '[ok] git=%s gh=%s\n' \
  "$(git --version | awk '{print $3}')" \
  "$(gh --version | head -n 1 | awk '{print $3}')"
printf '[ok] Dev Container scripts and managed shell components are available.\n'

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
