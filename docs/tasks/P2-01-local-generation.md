# P2-01 — Local generation with Ollama (enabler)

| Field | Value |
|---|---|
| Priority | P2 (first in the block — unblocks improved P2-03 and P3-02) |
| Branch | `feat/local-generation` |
| Commit | `feat(server): add local text generation service with KIOKU_GEN_MODEL` |
| Size | M |
| Spec | [features/05-local-generation.md](../features/05-local-generation.md) |
| Dependencies | None |

## Objective

`Services/GenerationService.cs` (`EmbeddingService` pattern: ping-based init, graceful
degradation, `"ollama"` HttpClient, source-generated JSON) + new `KIOKU_GEN_MODEL` env var
(disabled by default) + first tool `summarize_note` in a new `GenerationTools` class
(new `generation` capability group).

## Acceptance criteria

- [ ] Without `KIOKU_GEN_MODEL`: the group is registered but `summarize_note` returns
  `[error] [DEPENDENCY_UNAVAILABLE]` with setup instructions; the rest of the server is
  unaffected.
- [ ] With Ollama + model: `summarize_note` returns a summary in all 3 styles
  (`bullets`/`paragraph`/`eli5`) with the note `[info] Generated locally with {model}`.
- [ ] Truncated input (~4k chars) and 120s timeout verified by test.
- [ ] `generation` group is gateable: `capabilities.disabled: [generation]` deregisters it.
- [ ] Tests with a mocked HttpClient (success, timeout, Ollama down, model not pulled).
- [ ] Docs in the same PR: env var in root README, server README, `install.md`,
  `.mcp/server.json`; new group in `vault-config.md` + `vault-config.example.yml`;
  `commands-reference.md` regenerated.

## Files

- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (new)
- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs` (new)
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs`, `Program.cs`,
  `Services/VaultConfigService.cs` (if gating needs the name registered)
- New tests + docs listed
