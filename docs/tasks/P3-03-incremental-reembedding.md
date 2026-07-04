# P3-03 — Incremental re-embedding

| Field | Value |
|---|---|
| Priority | P3 |
| Branch | `feat/incremental-reembedding` |
| Commit | `feat(server): incremental re-embedding with content hashes and progress` |
| Size | M |
| Spec | [features/12-incremental-reembedding.md](../features/12-incremental-reembedding.md) |
| Dependencies | None |

## Objective

Embeddings cache in **v4 format** with a `ContentHash` per entry (skipping unchanged notes
between sessions), a re-embedding queue with limited parallelism and observable progress in
`get_index_status` (`embedding_backlog`, `embedding_rate`, `estimated_remaining`).

## Acceptance criteria

- [ ] v4 round-trip (save/load) with hashes; existing v3 cache is invalidated cleanly
  (re-embedded once, no parse errors) — migration test.
- [ ] A note with no content changes across restarts is **not** re-embedded (test with
  fixture).
- [ ] A large backlog doesn't block startup: keyword search is available immediately,
  embeddings complete in the background (current behavior, now with metrics).
- [ ] `get_index_status` reflects backlog and rate; once finished, backlog = 0.
- [ ] Limited parallelism verified (no more than N concurrent requests to Ollama).
- [ ] Docs: `v2-http-sse-spec.md` (v4 format), `troubleshooting.md` (slow indexing),
  the PR's CHANGELOG mentions the one-time cache invalidation.

## Files

- `src/Kioku.Mcp.Server/Services/EmbeddingPersistence.cs`
- `src/Kioku.Mcp.Server/Services/EmbeddingService.cs`
- `src/Kioku.Mcp.Server/Tools/UtilityTools.cs`
- Tests + docs
