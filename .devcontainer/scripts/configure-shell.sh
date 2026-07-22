#!/usr/bin/env bash

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
zshrc="${HOME}/.zshrc"
start_marker="# >>> kioku-devcontainer-shell >>>"
end_marker="# <<< kioku-devcontainer-shell <<<"

touch "$zshrc"

if ! grep -qF "$start_marker" "$zshrc"; then
  cat >> "$zshrc" <<'ZSH_EOF'

# >>> kioku-devcontainer-shell >>>
_kioku_workspace="${KIOKU_WORKSPACE:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
if [[ -f "${_kioku_workspace}/.devcontainer/shell/init.zsh" ]]; then
  export KIOKU_WORKSPACE="${_kioku_workspace}"
  source "${_kioku_workspace}/.devcontainer/shell/init.zsh"
fi
unset _kioku_workspace
# <<< kioku-devcontainer-shell <<<
ZSH_EOF
fi

if ! grep -qF "$end_marker" "$zshrc"; then
  echo "[error] Kioku shell customization block is incomplete in ${zshrc}." >&2
  exit 1
fi

printf '[ok] Kioku Zsh configuration is linked from %s.\n' "$repo_root"
