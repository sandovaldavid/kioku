# P1-01 — Expose the bridge's latent commands as MCP tools

| Field | Value |
|---|---|
| Priority | P1 |
| Branch | `feat/bridge-latent-tools` |
| Commit | `feat(server): expose latent bridge commands as MCP tools` |
| Size | S |
| Spec | [features/01-bridge-latent-tools.md](../features/01-bridge-latent-tools.md) |
| Dependencies | None (the plugin handlers already exist and are tested) |

## Objective

Add 6 MCP tools for the 8 commands that `handlers.ts` already implements but the server
doesn't use: `get_selection_in_obsidian`, `toggle_reading_mode`, `fold_all_headings`,
`unfold_all_headings`, `get_obsidian_status` (bundles `is-obsidian-ready` +
`get-app-version` + `get-vault-path`) and `reload_css_snippets`.

## Scope

- `ObsidianBridgeTools.cs`: +5 tools (`bridge` group), same pattern as the existing ones
  (`SendRequestAsync` + `KiokuError.DependencyUnavailable` if the plugin doesn't respond).
- `CssThemingTools.cs`: +1 tool `reload_css_snippets` (`css` group).
- No changes to the plugin.

## Acceptance criteria

- [ ] The 6 tools appear in `tools/list` with clear descriptions.
- [ ] With Obsidian closed they return `[error] [DEPENDENCY_UNAVAILABLE] ...` (not an
  exception).
- [ ] Manual end-to-end test with Obsidian open: `get_obsidian_status` returns
  `ready`, versions and vault; `get_selection_in_obsidian` reflects the real selection.
- [ ] `docs/commands-reference.md` regenerated (102 → 108 tools).
- [ ] Root README and server README tool tables updated.
- [ ] `dotnet build` + `dotnet format` + tests green.

## Files

- `src/Kioku.Mcp.Server/Tools/ObsidianBridgeTools.cs`
- `src/Kioku.Mcp.Server/Tools/CssThemingTools.cs`
- `docs/commands-reference.md`, `README.md`, `src/Kioku.Mcp.Server/README.md`
