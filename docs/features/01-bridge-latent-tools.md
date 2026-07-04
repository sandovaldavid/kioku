# 01 — Latent bridge tools

> Area: server · Task: [P1-01](../tasks/P1-01-bridge-latent-tools.md) · Impact ★★ · Effort S

## Motivation

The plugin implements 22 commands in `src/obsidian-kioku-mcp/src/handlers.ts`, but the server
only consumes 14. There are **8 commands already implemented, tested, and not exposed** as MCP
tools:

| Plugin command | What it does |
|---|---|
| `get-selection` | Returns the editor's current selection |
| `toggle-reading-mode` | Toggles between edit and reading mode |
| `fold-all-headings` / `unfold-all-headings` | Folds/unfolds all headings |
| `get-vault-path` | Path and name of the open vault |
| `is-obsidian-ready` | Plugin health check |
| `get-app-version` | Obsidian and plugin version |
| `reload-snippets` | Reloads CSS snippets |

Exposing them is minimal cost (the hard part already exists) and completes server↔plugin
parity.

## Design

New tools in `Tools/ObsidianBridgeTools.cs` (group `bridge`), via
`ObsidianBridgeService.SendRequestAsync(command, payload)`:

| MCP tool | Bridge command | Notes |
|---|---|---|
| `get_selection_in_obsidian()` | `get-selection` | Returns `{selection, hasSelection, length}` |
| `toggle_reading_mode()` | `toggle-reading-mode` | — |
| `fold_all_headings()` | `fold-all-headings` | — |
| `unfold_all_headings()` | `unfold-all-headings` | — |
| `get_obsidian_status()` | `is-obsidian-ready` + `get-app-version` + `get-vault-path` | **A single tool** that aggregates the 3 diagnostics: `{ready, obsidianVersion, kiokuVersion, vaultPath, vaultName}`. Avoids 3 trivial tools. |

And in `Tools/CssThemingTools.cs` (group `css`):

| MCP tool | Bridge command | Notes |
|---|---|---|
| `reload_css_snippets()` | `reload-snippets` | Complements `apply_css_snippet`, which currently doesn't force a reload |

Total: **6 new tools** (102 → 108). Error handling matches the existing bridge tools:
if the plugin isn't connected, return `KiokuError.DependencyUnavailable`.

## Affected files

- `src/Kioku.Mcp.Server/Tools/ObsidianBridgeTools.cs` (+5 tools)
- `src/Kioku.Mcp.Server/Tools/CssThemingTools.cs` (+1 tool)
- `docs/commands-reference.md` (regenerate)
- No plugin changes (the handlers already exist and are covered by
  `src/handlers.test.ts`)

## Risks

- Low. `get_obsidian_status` makes 3 sequential WebSocket round-trips (~ms on localhost);
  if that's a concern, the plugin could add an aggregate `get-status` command in a future
  iteration.
- `get-vault-path` uses an internal Obsidian API (`vault.adapter.basePath`) — already
  mitigated in the plugin with a `"unknown"` fallback.
