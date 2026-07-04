# P0-03 — Regroup merge-conflict tools out of `plugin`

| Field | Value |
|---|---|
| Priority | P0 |
| Branch | `fix/merge-tools-group` |
| Commit | `fix(server): move merge-conflict tools out of the plugin capability group` |
| Size | S |
| Dependencies | None |

## Context

`fix_merge_conflicts` and `resolve_merge_conflict` live in `Tools/PluginIntegrationTools.cs`
(`plugin` group), but **they don't use the Obsidian bridge**: they scan and edit local files
with git conflict markers (`<<<<<<<`). If the user disables the `plugin` group (e.g. because
they don't use the Obsidian plugin), they lose two tools that don't need it.

## Scope

1. Move both tools to `Tools/GitTools.cs` (`git` group) — conceptually they're git conflict
   tooling. Alternative if we'd rather not touch `git`: a new small class; we prefer `git`
   so as not to create yet another group.
2. Keep the tool names (no breaking change for agents).
3. Document in the PR that their capability group changed `plugin` → `git` (users with
   `capabilities.require_explicit` or `disabled` might be affected).

## Acceptance criteria

- [ ] With `capabilities.disabled: [plugin]`, `fix_merge_conflicts` and
  `resolve_merge_conflict` remain available (`git` group enabled).
- [ ] With `capabilities.disabled: [git]`, they stop being registered.
- [ ] Build + tests green; `dotnet format` with no changes.
- [ ] `docs/commands-reference.md` regenerated (the tools appear under `GitTools`).
- [ ] Root README / server README tables updated (Git row and Plugin Bridge row).

## Files

- `src/Kioku.Mcp.Server/Tools/PluginIntegrationTools.cs` (remove)
- `src/Kioku.Mcp.Server/Tools/GitTools.cs` (add)
- `docs/commands-reference.md`, `README.md`, `src/Kioku.Mcp.Server/README.md`,
  `docs/vault-config.md` (if it mentions group examples)
