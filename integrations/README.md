# Client integrations

This directory packages the Kioku MCP server for one-command installation into AI coding
CLIs, instead of the manual JSON/TOML editing documented in the main [README](../README.md).
See `scripts/add-to-client.sh` for the installer that drives these bundles, and the "Quick
install" section of the root README for the four one-liners.

## Skill source of truth

The skills under `claude-code-plugin/skills/` are the canonical copies for this repository.
Edit them there, then run:

```bash
scripts/sync-skill.sh
scripts/sync-skill.sh --check
```

The script propagates and verifies both generated destinations:

- `integrations/antigravity-plugin/skills/`;
- `.agents/skills/`, used by repository-scoped agent clients.

Do not hand-edit either generated destination. External catalogs such as
`sandovaldavid/dotfiles` must synchronize from these canonical files and run their own drift
check.

## `claude-code-plugin/`

A [Claude Code plugin](https://code.claude.com/docs/en/plugins-reference). Installed via:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

The marketplace entry lives in `.claude-plugin/marketplace.json` at the repository root and
points here. Installation asks for the vault path and optional local generation settings through
the plugin's `userConfig`; no manual `.mcp.json` editing is required.

## `antigravity-plugin/`

An [Antigravity CLI](https://antigravity.google/docs/plugins) plugin bundle. Antigravity has no
CLI add command, so `scripts/add-to-client.sh antigravity` copies this folder to
`~/.gemini/config/plugins/kioku/` by default or `.agents/plugins/kioku/` with `--workspace`, then
substitutes the configured vault path into `mcp_config.json`.

- **`skills/*/SKILL.md` files are generated.** Update the canonical Claude Code copies and run
  `scripts/sync-skill.sh`; CI checks drift with `sync-skill.sh --check`.
- **`rules/kioku.md`** contains hard behavioral constraints, separate from usage guidance.
## Prerequisites

Both bundles assume the `kioku` binary is on `PATH` (`dotnet tool install -g
kioku-mcp-server`). They register the server but do not bundle a separate server runtime.
`scripts/add-to-client.sh` checks the prerequisite and can offer installation.
