#!/usr/bin/env bash
# Syncs the canonical Kioku skills (Claude Code plugin) into every generated
# repository copy. Generated copies must never be hand-edited.
#
# Usage:
#   scripts/sync-skill.sh          # copy canonical skills into all targets
#   scripts/sync-skill.sh --check  # exit 1 when any generated copy has drifted
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SKILLS=(kioku-vault kioku-project-workflow)
TARGET_ROOTS=(
    "${ROOT_DIR}/integrations/antigravity-plugin/skills"
    "${ROOT_DIR}/.agents/skills"
)

mode="${1:-}"
if [[ "$mode" != "" && "$mode" != "--check" ]]; then
    echo "Usage: $0 [--check]" >&2
    exit 2
fi

for skill in "${SKILLS[@]}"; do
    src="${ROOT_DIR}/integrations/claude-code-plugin/skills/${skill}/SKILL.md"
    if [[ ! -f "$src" ]]; then
        echo "Canonical skill not found: $src" >&2
        exit 1
    fi

    for target_root in "${TARGET_ROOTS[@]}"; do
        dest="${target_root}/${skill}/SKILL.md"

        if [[ "$mode" == "--check" ]]; then
            if [[ ! -f "$dest" ]]; then
                echo "Generated skill copy is missing: $dest" >&2
                echo "Run scripts/sync-skill.sh to generate it." >&2
                exit 1
            fi
            if ! diff -q "$src" "$dest" >/dev/null 2>&1; then
                echo "Generated skill copy has drifted: $skill" >&2
                echo "  canonical: $src" >&2
                echo "  copy:      $dest" >&2
                echo "Run scripts/sync-skill.sh to re-sync." >&2
                exit 1
            fi
            continue
        fi

        mkdir -p "$(dirname "$dest")"
        cp "$src" "$dest"
        echo "Synced ${skill}: $src -> $dest"
    done
done

if [[ "$mode" == "--check" ]]; then
    echo "All generated Kioku skill copies match the canonical sources."
fi
