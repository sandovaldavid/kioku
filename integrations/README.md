# Client integrations

This directory packages native plugin bundles and MCP configurations for AI coding clients (such as Claude Code and Antigravity) that support native plugins or bundled skills alongside MCP configuration.

For native MCP setup across all supported clients, see [Installation Documentation](../docs/install.md).

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
points here. Installation prompts for the vault path and optional local generation settings through
the plugin's `userConfig`; no manual `.mcp.json` editing is required.

## `antigravity-plugin/`

An Antigravity plugin bundle. Installed into `~/.gemini/antigravity-cli/plugins/kioku/` (user scope) or `.agents/plugins/kioku/` (workspace scope) via `agy`:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
agy plugin install ./integrations/antigravity-plugin
```

Validate the bundle structure with:

```bash
agy plugin validate ./integrations/antigravity-plugin
```

- **`skills/*/SKILL.md` files are generated.** Update the canonical Claude Code copies and run
  `scripts/sync-skill.sh`; CI checks drift with `sync-skill.sh --check`.
- **`rules/kioku.md`** contains hard behavioral constraints, separate from usage guidance.

## Prerequisites

Both bundles assume the `kioku` binary is installed and on `PATH` (`dotnet tool install -g kioku-mcp-server` or prebuilt binary). They register the server but do not bundle a separate server runtime.
