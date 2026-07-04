# P0-01 — Resolve `suggest_tags` name collision

| Field | Value |
|---|---|
| Priority | P0 |
| Branch | `fix/suggest-tags-collision` |
| Commit | `fix(server): rename duplicate suggest_tags query tool to inspect_note_tags` |
| Size | S |
| Dependencies | None |

## Context

`suggest_tags` is defined **twice**:

- `Tools/NoteQueryTools.cs` (core, always registered) — read-only diagnostic: reports a
  note's current/inherited/excluded tags.
- `Tools/VaultOrganizationTools.cs` (`organization` group) — suggests new tags
  (`max_suggestions`).

When the `organization` group is enabled (default), two MCP tools get registered with the
same name. Depending on the client/SDK, one shadows the other or the listing becomes
ambiguous.

## Scope

1. Rename the one in `NoteQueryTools` to **`inspect_note_tags`** (better describes its
   read-only/diagnostic nature). The one in `VaultOrganizationTools` keeps `suggest_tags`
   (it's the one the name promises).
2. Check the SDK/registration code to see what behavior the collision actually caused and
   note it in the PR description.
3. Update references in docs (root README Query table, server README).

## Acceptance criteria

- [ ] `grep -rn '"suggest_tags"\|suggest_tags' src/Kioku.Mcp.Server/Tools/` shows a single
  MCP tool with that name.
- [ ] With the `organization` group enabled, `tools/list` contains no duplicates.
- [ ] `NoteQueryToolsTests` tests updated and green.
- [ ] `docs/commands-reference.md` regenerated (`dotnet run --project scripts/GenerateCommandsRef`).
- [ ] Root README and `src/Kioku.Mcp.Server/README.md` updated.

## Files

- `src/Kioku.Mcp.Server/Tools/NoteQueryTools.cs`
- `src/Kioku.Mcp.Server.Tests/NoteQueryToolsTests.cs`
- `docs/commands-reference.md`, `README.md`, `src/Kioku.Mcp.Server/README.md`

## Breaking change note

This is a tool rename visible to agents: mention it in the PR body so release-please's
CHANGELOG picks it up (`fix(server)!:` if it should be marked as breaking).
