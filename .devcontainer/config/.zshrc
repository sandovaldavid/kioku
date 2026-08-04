export STARSHIP_CONFIG="${STARSHIP_CONFIG:-${ZDOTDIR:-$HOME}/starship.toml}"

if command -v starship >/dev/null 2>&1; then
  eval "$(starship init zsh)"
fi
