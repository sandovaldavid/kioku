# Managed by .devcontainer/scripts/configure-shell.sh.

export PATH="$HOME/.local/bin:$PATH"
export STARSHIP_CONFIG="$HOME/.config/starship.toml"
export KIOKU_WORKSPACE="${KIOKU_WORKSPACE:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"

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

croot() {
  cd "$KIOKU_WORKSPACE"
}

krestore() {
  (cd "$KIOKU_WORKSPACE" && dotnet restore Kioku.slnx)
}

kbuild() {
  (cd "$KIOKU_WORKSPACE" && dotnet build Kioku.slnx --configuration Release)
}

ktest() {
  (cd "$KIOKU_WORKSPACE" && dotnet test Kioku.slnx --configuration Release)
}

kformat() {
  (
    cd "$KIOKU_WORKSPACE" \
      && dotnet format Kioku.slnx whitespace \
      && dotnet format Kioku.slnx style
  )
}

kverify() {
  (cd "$KIOKU_WORKSPACE" && bash .devcontainer/scripts/verify-environment.sh)
}

if command -v starship >/dev/null 2>&1; then
  eval "$(starship init bash)"
fi
