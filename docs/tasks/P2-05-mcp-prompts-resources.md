# P2-05 — MCP Prompts & Resources

| Field | Value |
|---|---|
| Priority | P2 |
| Branch | `feat/mcp-prompts-resources` |
| Commit | `feat(server): expose mcp prompts and note resources` |
| Size | M |
| Spec | [features/09-mcp-prompts-resources.md](../features/09-mcp-prompts-resources.md) |
| Dependencies | None |

## Objective

Expose the other two MCP primitives with SDK 1.4.0: resources
(`kioku://note/{path}` via URI template + `kioku://vault/stats`; `resources/list` limited
to recent notes) and curated prompts (`research_digest`, `process_inbox`, `weekly_review`,
`literature_review`).

## Acceptance criteria

- [ ] `resources/list` returns a bounded top-N (~20 recent), not the whole vault.
- [ ] `resources/read` resolves any note by URI (and fails cleanly with NOT_FOUND).
- [ ] The 4 prompts appear in `prompts/list` with typed arguments and render correctly with
  sample arguments.
- [ ] End-to-end verification in Claude Code: the prompt appears as a slash command and the
  resources are mountable.
- [ ] Prior spike documented in the PR: exactly what `ModelContextProtocol
  1.4.0` supports (URI templates, subscribe) and what was left out.
- [ ] Shape tests + URI resolution tests with `VaultFixture`.
- [ ] Docs: new section in root README; evaluate extending
  `scripts/GenerateCommandsRef` to list prompts/resources.

## Files

- `src/Kioku.Mcp.Server/Prompts/KiokuPrompts.cs` (new)
- Resources per SDK convention + `Program.cs`
- Tests + docs
