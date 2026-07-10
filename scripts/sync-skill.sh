#!/usr/bin/env bash
# Syncs the canonical kioku-vault skill (Claude Code plugin) into the Antigravity
# plugin bundle. The Antigravity copy is generated — never hand-edit it.
#
# Usage:
#   scripts/sync-skill.sh          # copy canonical -> antigravity, overwriting the target
#   scripts/sync-skill.sh --check  # exit 1 if the antigravity copy has drifted (for CI)
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${ROOT_DIR}/integrations/claude-code-plugin/skills/kioku-vault/SKILL.md"
DEST="${ROOT_DIR}/integrations/antigravity-plugin/skills/kioku-vault/SKILL.md"

if [ ! -f "$SRC" ]; then
    echo "Canonical skill not found: $SRC" >&2
    exit 1
fi

mode="${1:-}"

if [ "$mode" = "--check" ]; then
    if [ ! -f "$DEST" ]; then
        echo "Antigravity skill copy is missing: $DEST" >&2
        echo "Run scripts/sync-skill.sh to generate it." >&2
        exit 1
    fi
    if ! diff -q "$SRC" "$DEST" >/dev/null 2>&1; then
        echo "Antigravity skill copy has drifted from the canonical source." >&2
        echo "  canonical: $SRC" >&2
        echo "  copy:      $DEST" >&2
        echo "Run scripts/sync-skill.sh to re-sync." >&2
        exit 1
    fi
    echo "kioku-vault skill copy is in sync."
    exit 0
fi

mkdir -p "$(dirname "$DEST")"
cp "$SRC" "$DEST"
echo "Synced kioku-vault skill: $SRC -> $DEST"
