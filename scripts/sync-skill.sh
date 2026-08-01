#!/usr/bin/env bash
# Syncs the canonical Kioku skills (Claude Code plugin) into the Antigravity
# plugin bundle. The Antigravity copies are generated — never hand-edit them.
#
# Usage:
#   scripts/sync-skill.sh          # copy canonical -> antigravity, overwriting the target
#   scripts/sync-skill.sh --check  # exit 1 if the antigravity copy has drifted (for CI)
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SKILLS=(kioku-vault kioku-project-workflow)

mode="${1:-}"

if [ "$mode" = "--check" ]; then
    for skill in "${SKILLS[@]}"; do
        src="${ROOT_DIR}/integrations/claude-code-plugin/skills/${skill}/SKILL.md"
        dest="${ROOT_DIR}/integrations/antigravity-plugin/skills/${skill}/SKILL.md"
        if [ ! -f "$src" ]; then
            echo "Canonical skill not found: $src" >&2
            exit 1
        fi
        if [ ! -f "$dest" ]; then
            echo "Antigravity skill copy is missing: $dest" >&2
            echo "Run scripts/sync-skill.sh to generate it." >&2
            exit 1
        fi
        if ! diff -q "$src" "$dest" >/dev/null 2>&1; then
            echo "Antigravity skill copy has drifted from the canonical source: $skill" >&2
            echo "  canonical: $src" >&2
            echo "  copy:      $dest" >&2
            echo "Run scripts/sync-skill.sh to re-sync." >&2
            exit 1
        fi
    done
    echo "Kioku skill copies are in sync."
    exit 0
fi

for skill in "${SKILLS[@]}"; do
    src="${ROOT_DIR}/integrations/claude-code-plugin/skills/${skill}/SKILL.md"
    dest="${ROOT_DIR}/integrations/antigravity-plugin/skills/${skill}/SKILL.md"
    if [ ! -f "$src" ]; then
        echo "Canonical skill not found: $src" >&2
        exit 1
    fi
    mkdir -p "$(dirname "$dest")"
    cp "$src" "$dest"
    echo "Synced ${skill} skill: $src -> $dest"
done
