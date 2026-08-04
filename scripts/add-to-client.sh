#!/usr/bin/env bash
# One-command MCP registration for Kioku across Claude Code, Codex CLI, OpenCode, and
# Antigravity CLI/IDE. Wraps each client's native mechanism where one exists, and
# writes/copies config directly where it doesn't. Run from inside a checkout of the
# kioku repo (integrations/ bundles are read from there).
#
# Usage:
#   scripts/add-to-client.sh <claude-code|codex|opencode|antigravity> --vault <path> [options]
#
# Options:
#   --vault <path>     Absolute path to your Obsidian vault (required)
#   --scope <scope>    claude-code only: "user" (default) or "project"
#   --simple           claude-code only: use `claude mcp add` instead of the plugin/marketplace
#   --workspace        antigravity only: install to ./.agents/plugins/ instead of the global dir
#   --dry-run          print what would happen without changing anything
#   --yes              don't prompt before running `dotnet tool install -g kioku-mcp-server`
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

usage() {
    sed -n '2,17p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
    usage
    exit 0
fi

CLIENT="${1:-}"
[[ $# -gt 0 ]] && shift

VAULT=""
SCOPE=""
WORKSPACE=false
DRY_RUN=false
YES=false
SIMPLE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
    --vault)
        VAULT="${2:-}"
        shift 2
        ;;
    --scope)
        SCOPE="${2:-}"
        shift 2
        ;;
    --workspace)
        WORKSPACE=true
        shift
        ;;
    --dry-run)
        DRY_RUN=true
        shift
        ;;
    --yes)
        YES=true
        shift
        ;;
    --simple)
        SIMPLE=true
        shift
        ;;
    -h | --help)
        usage
        exit 0
        ;;
    *)
        echo "Unknown option: $1" >&2
        usage
        exit 1
        ;;
    esac
done

case "$CLIENT" in
claude-code | codex | opencode | antigravity) ;;
*)
    echo "Error: unknown or missing client '${CLIENT}'." >&2
    usage
    exit 1
    ;;
esac

if [[ -z "$VAULT" ]]; then
    echo "Error: --vault <path> is required." >&2
    exit 1
fi

RESOLVED_VAULT="$(cd "$VAULT" 2>/dev/null && pwd)" || RESOLVED_VAULT=""
if [[ -z "$RESOLVED_VAULT" ]]; then
    echo "Error: --vault path '$VAULT' does not exist or is not a directory." >&2
    exit 1
fi
VAULT="$RESOLVED_VAULT"

if [[ ! -d "$ROOT_DIR/integrations" ]]; then
    echo "Error: integrations/ not found next to this script — run from inside a kioku checkout." >&2
    exit 1
fi

# Escapes a replacement string for sed's `s#pattern#replacement#` form (delimiter `#`).
sed_escape_repl() {
    printf '%s' "$1" | sed -e 's/[\&#]/\\&/g'
}

ensure_kioku_binary() {
    if command -v kioku >/dev/null 2>&1; then
        return 0
    fi
    echo "The 'kioku' command was not found on PATH." >&2
    if [[ "$DRY_RUN" == true ]]; then
        echo "[dry-run] would offer to run: dotnet tool install -g kioku-mcp-server"
        return 0
    fi
    if [[ "$YES" != true ]]; then
        read -r -p "Install it now via 'dotnet tool install -g kioku-mcp-server'? [y/N] " reply
        case "$reply" in
        [yY]*) ;;
        *)
            echo "Skipping install. The MCP server may fail to start until 'kioku' is on PATH." >&2
            return 1
            ;;
        esac
    fi
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "dotnet SDK not found. Install the .NET 10 SDK, then run:" >&2
        echo "  dotnet tool install -g kioku-mcp-server" >&2
        return 1
    fi
    dotnet tool install -g kioku-mcp-server
    if ! command -v kioku >/dev/null 2>&1; then
        echo "Installed, but 'kioku' still isn't on PATH. Add this to your shell profile:" >&2
        # shellcheck disable=SC2016 # intentional: printed literally, not expanded here
        echo '  export PATH="$PATH:$HOME/.dotnet/tools"' >&2
        return 1
    fi
}

# Runs a command, or prints it if --dry-run or the binary isn't available.
run_cmd() {
    if [[ "$DRY_RUN" == true ]]; then
        printf '[dry-run] would run:'
        printf ' %q' "$@"
        printf '\n'
        return 0
    fi
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "'$1' not found on PATH. Run this manually:" >&2
        printf ' %q' "$@" >&2
        printf '\n' >&2
        return 1
    fi
    "$@"
}

ensure_kioku_binary || true

case "$CLIENT" in
claude-code)
    if [[ "$SIMPLE" == true ]]; then
        run_cmd claude mcp add kioku --scope "${SCOPE:-project}" --env "KIOKU_VAULT_PATH=${VAULT}" -- kioku
    else
        echo "Registering the Kioku marketplace and installing the kioku plugin"
        echo "(bundles the MCP server config with the kioku-vault skill)..."
        run_cmd claude plugin marketplace add sandovaldavid/kioku
        run_cmd claude plugin install kioku@kioku
        echo "When prompted for the vault path, enter: $VAULT"
    fi
    ;;

codex)
    run_cmd codex mcp add kioku --env "KIOKU_VAULT_PATH=${VAULT}" -- kioku
    if [[ "${SCOPE:-}" == "project" ]]; then
        cat >&2 <<EOF

Note: 'codex mcp add' has no confirmed project-scope flag; it writes to the global
~/.codex/config.toml. For project scope, add this to .codex/config.toml manually:

[mcp_servers.kioku]
command = "kioku"
args = []
env = { KIOKU_VAULT_PATH = "$VAULT" }
EOF
    fi
    ;;

opencode)
    if [[ "${SCOPE:-}" == "project" ]]; then
        echo "Error: native 'opencode mcp add' only supports user configuration." >&2
        exit 1
    fi

    skill_dest="$HOME/.claude/skills/kioku-vault"

    run_cmd opencode mcp add kioku --env "KIOKU_VAULT_PATH=${VAULT}" -- kioku
    if [[ "$DRY_RUN" == true ]]; then
        echo "[dry-run] would copy skill to: $skill_dest/SKILL.md"
    else
        mkdir -p "$skill_dest"
        cp "$ROOT_DIR/integrations/claude-code-plugin/skills/kioku-vault/SKILL.md" "$skill_dest/SKILL.md"
        echo "Copied kioku-vault skill to $skill_dest (also picked up by Claude Code if installed)."
    fi
    ;;

antigravity)
    if [[ "$WORKSPACE" == true ]]; then
        if [[ -d "$(pwd)/_agents" ]]; then
            dest="$(pwd)/_agents/plugins/kioku"
        else
            dest="$(pwd)/.agents/plugins/kioku"
        fi
    else
        dest="$HOME/.gemini/config/plugins/kioku"
    fi

    if [[ "$DRY_RUN" == true ]]; then
        echo "[dry-run] would copy $ROOT_DIR/integrations/antigravity-plugin -> $dest"
        echo "[dry-run] would set KIOKU_VAULT_PATH=$VAULT in $dest/mcp_config.json"
    else
        mkdir -p "$dest"
        cp -R "$ROOT_DIR/integrations/antigravity-plugin/." "$dest/"

        vault_escaped="$(sed_escape_repl "$VAULT")"
        sed -i.bak "s#__KIOKU_VAULT_PATH__#${vault_escaped}#" "$dest/mcp_config.json"
        rm -f "$dest/mcp_config.json.bak"

        echo "Installed the Antigravity plugin to $dest"
    fi
    ;;
esac
