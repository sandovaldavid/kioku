# Kioku project shell customization.
# This file is sourced only from the Dev Container's interactive Zsh session.

if [[ -z "${KIOKU_WORKSPACE:-}" ]]; then
  export KIOKU_WORKSPACE="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
fi

export STARSHIP_CONFIG="${STARSHIP_CONFIG:-${KIOKU_WORKSPACE}/.devcontainer/shell/starship.toml}"
export PATH="$HOME/.local/bin:$PATH"

HISTFILE="${ZSH_HISTORY_FILE:-$HOME/.zsh_history}"
HISTSIZE=50000
SAVEHIST=50000

setopt auto_cd
setopt append_history
setopt share_history
setopt extended_history
setopt hist_ignore_dups
setopt hist_ignore_all_dups
setopt hist_find_no_dups
setopt hist_save_no_dups
setopt hist_reduce_blanks
setopt hist_verify
setopt hist_ignore_space

if command -v eza >/dev/null 2>&1; then
  alias ls='eza --icons=auto --group-directories-first'
  alias l='eza --icons=auto --group-directories-first'
  alias la='eza -a --icons=auto --group-directories-first'
  alias ll='eza -la --icons=auto --git --group-directories-first --time-style=long-iso'
  alias tree='eza --tree --icons=auto --group-directories-first'
  alias tree2='eza --tree --level=2 --icons=auto --group-directories-first'
  alias tree3='eza --tree --level=3 --icons=auto --group-directories-first'
  alias tree-d='eza --tree --only-dirs --icons=auto --group-directories-first'
  alias lsd='eza --only-dirs --icons=auto --group-directories-first'
  alias lsf='eza --only-files --icons=auto --group-directories-first'
  alias ls-native='command ls --color=auto'
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
