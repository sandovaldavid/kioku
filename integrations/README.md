# Client integrations

This directory packages the Kioku MCP server for one-command installation into AI coding
CLIs, instead of the manual JSON/TOML editing documented in the main [README](../README.md).
See `scripts/add-to-client.sh` for the installer that drives these bundles, and the "Quick
install" section of the root README for the four one-liners.

## `claude-code-plugin/`

A [Claude Code plugin](https://code.claude.com/docs/en/plugins-reference). Installed via:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

(the marketplace entry lives in `.claude-plugin/marketplace.json` at the repo root, pointing
here). Installing prompts for the vault path (and optionally the Ollama URL / embedding model)
via the plugin's `userConfig` — no manual `.mcp.json` editing needed.

`skills/kioku-vault/SKILL.md` in this directory is the **canonical** copy of the skill. Edit it
here, then run `scripts/sync-skill.sh` to propagate the change into
`antigravity-plugin/skills/kioku-vault/SKILL.md`.

## `antigravity-plugin/`

An [Antigravity CLI](https://antigravity.google/docs/plugins) plugin bundle. Antigravity has no
CLI "add" command — plugins are discovered by scanning a directory — so
`scripts/add-to-client.sh antigravity` copies this whole folder to
`~/.gemini/config/plugins/kioku/` (global, default) or `.agents/plugins/kioku/` (workspace, with
`--workspace`), substituting the real vault path into `mcp_config.json` in the process.

- **`skills/kioku-vault/SKILL.md` is a generated file.** It's a copy of the canonical skill in
  `claude-code-plugin/`, produced by `scripts/sync-skill.sh`. Don't hand-edit it — edit the
  canonical copy and re-run the sync script (CI checks for drift via `sync-skill.sh --check`).
- **`rules/kioku.md`** holds behavioral constraints (hard "never do X without Y" rules),
  distinct from the skill's "how to use these tools" content.
- **`hooks/` is experimental and off by default.** The `hooks.json` schema it uses (matcher,
  `type: "command"`, stdin/stdout JSON shape) is based on third-party write-ups, not confirmed
  against official Antigravity documentation. It ships a single `PreToolUse` audit-log hook that
  always allows the call. Install it with `scripts/add-to-client.sh antigravity --with-hooks`;
  omit the flag (the default) to skip it entirely.

## Prerequisites

Both bundles assume the `kioku` binary is on `PATH` (`dotnet tool install -g
kioku-mcp-server`) — neither plugin installs the server itself, only registers it.
`scripts/add-to-client.sh` checks for this and offers to install it for you.
