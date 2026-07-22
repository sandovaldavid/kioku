# Kioku project shell customization.
# This file is sourced only from the Dev Container's interactive Zsh session.

if [[ -z "${KIOKU_WORKSPACE:-}" ]]; then
  export KIOKU_WORKSPACE="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
fi

export STARSHIP_CONFIG="${STARSHIP_CONFIG:-${KIOKU_WORKSPACE}/.devcontainer/shell/starship.toml}"

setopt auto_cd
setopt hist_ignore_all_dups
setopt share_history

if command -v eza >/dev/null 2>&1; then
  alias ls='eza --icons=auto --group-directories-first'
  alias ll='eza --icons=auto --group-directories-first --long --git --time-style=long-iso'
  alias la='eza --icons=auto --group-directories-first --all'
  alias tree='eza --icons=auto --tree'
  alias tree-d='eza --icons=auto --tree --only-dirs'
fi

if command -v bat >/dev/null 2>&1; then
  alias cat='bat --paging=never'
fi

function croot() {
  cd "${KIOKU_WORKSPACE}"
}

function krestore() {
  (cd "${KIOKU_WORKSPACE}" && dotnet restore Kioku.slnx)
}

function kbuild() {
  (cd "${KIOKU_WORKSPACE}" && dotnet build Kioku.slnx --configuration Release)
}

function ktest() {
  (cd "${KIOKU_WORKSPACE}" && dotnet test Kioku.slnx --configuration Release)
}

function kformat() {
  (
    cd "${KIOKU_WORKSPACE}" \
      && dotnet format Kioku.slnx whitespace \
      && dotnet format Kioku.slnx style
  )
}

function kplugin() {
  (
    cd "${KIOKU_WORKSPACE}" \
      && pnpm --filter obsidian-kioku-mcp run lint \
      && pnpm --filter obsidian-kioku-mcp run test \
      && pnpm --filter obsidian-kioku-mcp run build
  )
}

function kverify() {
  (cd "${KIOKU_WORKSPACE}" && bash .devcontainer/scripts/verify-environment.sh)
}

if [[ -f /usr/share/zsh-autosuggestions/zsh-autosuggestions.zsh ]]; then
  source /usr/share/zsh-autosuggestions/zsh-autosuggestions.zsh
fi

eval "$(starship init zsh)"

# zsh-syntax-highlighting must be sourced after the prompt and other plugins.
if [[ -f /usr/share/zsh-syntax-highlighting/zsh-syntax-highlighting.zsh ]]; then
  source /usr/share/zsh-syntax-highlighting/zsh-syntax-highlighting.zsh
fi
