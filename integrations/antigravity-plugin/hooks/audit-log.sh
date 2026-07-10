#!/usr/bin/env bash
# EXPERIMENTAL — Antigravity CLI PreToolUse hook, off by default.
#
# Not verified against official Antigravity documentation; the hooks.json schema this
# script assumes (matcher/hooks/type/command, stdin JSON, {"allow_tool": ...} on stdout)
# comes from third-party write-ups, not the official Antigravity docs. Enable at your own
# risk with `scripts/add-to-client.sh antigravity --with-hooks`. See integrations/README.md.
#
# Appends every MCP tool call this hook sees to a local audit log, then always allows it.
set -euo pipefail

LOG_FILE="${KIOKU_AUDIT_LOG:-$HOME/.kioku/antigravity-audit.log}"
mkdir -p "$(dirname "$LOG_FILE")"

input=$(cat)
printf '%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$input" >>"$LOG_FILE"

echo '{"allow_tool": true}'
exit 0
