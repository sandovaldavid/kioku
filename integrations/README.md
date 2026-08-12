# Client integrations

> Current tagged server release: **3.1.1** <!-- x-release-please-version -->

This directory packages native plugin bundles and MCP configurations for AI coding clients (such as Claude Code and Antigravity) that support native plugins or bundled skills alongside MCP configuration.

For native MCP setup across all supported clients, see [Installation Documentation](../docs/install.md).

## Release and versioning boundary

The server release is authoritative for the versioned Claude Code plugin manifest in
`claude-code-plugin/.claude-plugin/plugin.json`. Release Please updates that manifest together
with the server package and other release-facing metadata.

The marketplace entry intentionally does **not** duplicate a `version` field. Claude Code resolves
the explicit plugin version from the plugin manifest; keeping the version in one place avoids
cache/update drift between the marketplace catalog and the installed plugin.

The Antigravity bundle does not embed a separate Kioku server runtime. It packages MCP
configuration, skills, and rules from the repository and expects a compatible `kioku` executable
on `PATH`.

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

The canonical skills intentionally follow the branch/runtime MCP contract instead of pinning a
server version inside `SKILL.md`. `scripts/validate-portable-configs.mjs` checks their advertised
profile counts and key engineering workflow contract against generated public discovery metadata,
while `scripts/sync-skill.sh --check` prevents generated copies from drifting.

## `claude-code-plugin/`

A [Claude Code plugin](https://code.claude.com/docs/en/plugins-reference). Installed via:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

To refresh an existing installation after a Kioku release:

```bash
claude plugin marketplace update kioku
claude plugin update kioku@kioku
```

The marketplace entry lives in `.claude-plugin/marketplace.json` at the repository root and
points here. Before installing or launching the plugin, export `KIOKU_VAULT_PATH` in the
environment used to start Claude Code. The plugin passes this variable to the `kioku` process;
no vault path is stored in this repository. Optional local generation settings remain available
through the plugin's `userConfig`; no manual `.mcp.json` editing is required.

## `antigravity-plugin/`

An Antigravity plugin bundle. Installed globally for the user into `~/.gemini/antigravity-cli/plugins/kioku/` via `agy`:

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
