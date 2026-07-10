#!/usr/bin/env bash
# Promotes develop's work into main via an intermediate branch (never pushes
# directly to develop or main). Version-tracked files are always resolved to
# main's current content — Release Please (which runs only on main) is the
# sole writer of version numbers, CHANGELOG.md, and related metadata.
# Usage: scripts/sync-develop-to-main.sh
set -euo pipefail

REMOTE="${REMOTE:-origin}"
BRANCH="chore/sync-develop-to-main-$(date +%Y%m%d%H%M%S)"

# Files Release Please owns on main — always keep main's content on conflict.
VERSION_FILES=(
    "README.md"
    "CHANGELOG.md"
    "src/Kioku.Mcp.Server/README.md"
    "src/Kioku.Mcp.Server/.mcp/server.json"
    "src/obsidian-kioku-mcp/manifest.json"
    "src/obsidian-kioku-mcp/package.json"
)

git fetch "$REMOTE" main develop

echo "Creating $BRANCH from $REMOTE/develop..."
git checkout -b "$BRANCH" "$REMOTE/develop"

echo "Merging $REMOTE/main..."
if ! git merge "$REMOTE/main" --no-edit; then
    echo "Auto-resolving known version-tracked files to main's content..."
    for f in "${VERSION_FILES[@]}"; do
        if git status --short -- "$f" | grep -q '^\(UU\|AA\|DU\|UD\)'; then
            git checkout --theirs -- "$f"
            git add -- "$f"
        fi
    done

    remaining=$(git status --short | grep -E '^(UU|AA|DU|UD)' || true)
    if [ -n "$remaining" ]; then
        echo
        echo "Unresolved conflicts remain (not in the known version-file list):"
        echo "$remaining"
        echo
        echo "Resolve them manually, then run: git commit --no-edit"
        echo "For PackageVersion / dependency-version lines inside .csproj files,"
        echo "keep main's <PackageVersion> but take develop's dependency bumps."
        exit 1
    fi

    git commit --no-edit
fi

git push -u "$REMOTE" "$BRANCH"

echo
echo "Pushed $BRANCH. Open the PR with:"
echo "  gh pr create --base main --head $BRANCH --title \"chore(release): sync develop into main\""
