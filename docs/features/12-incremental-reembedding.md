# 12 — Incremental re-embedding

> Area: server · Task: [P3-03](../tasks/P3-03-incremental-reembedding.md) · Impact ★★★ · Effort M

## Motivation

After a startup with an invalid cache (model/dimension change) or on first indexing,
Kioku re-embeds the **entire** vault sequentially (~60ms/note locally, 2-5s/note on
CPU: for 5000 notes this can take anywhere from minutes to hours). The cache also
doesn't store a content hash, so it can't distinguish changed notes from untouched
ones across sessions if a file was touched without changing its content.

## Design

### 1. Content hash in the cache (format v4)

- `EmbeddingEntry` gains a `ContentHash` field (the MD5 already computed in
  `Note.ContentHash` — zero extra cost).
- `EmbeddingPersistence.FormatVersion` 3 → **4** (automatic invalidation of the old
  cache, a single re-embed during migration; document it in the PR's CHANGELOG).
- In `IndexNoteAsync`: if the hash matches the cached one, **skip** (today the
  freshness criterion only lives in per-session memory).

### 2. Batching and controlled parallelism

- Re-embedding queue with limited parallelism (e.g. `SemaphoreSlim(2)`) to avoid
  saturating Ollama, and cache flush every 50 entries (existing mechanism).
- If the model's Ollama API supports batch input (`/api/embed` with an array), use
  it; fall back to individual requests otherwise.

### 3. Observable progress

- `get_index_status` adds: `embedding_backlog` (pending notes), `embedded_count`,
  `embedding_rate` (notes/min), and `estimated_remaining`.
- The server starts serving keyword searches while the backlog is processed in the
  background (current behavior, now measurable).

## Affected files

- `src/Kioku.Mcp.Server/Services/EmbeddingPersistence.cs` (format v4 + hash)
- `src/Kioku.Mcp.Server/Services/EmbeddingService.cs` (skip by hash, queue, counters)
- `src/Kioku.Mcp.Server/Tools/UtilityTools.cs` (`get_index_status`)
- Tests: v4 round-trip, v3→v4 migration (clean invalidation), skip on identical hash
- Docs: `v2-http-sse-spec.md` (cache format), `troubleshooting.md` (slow indexing
  section)

## Risks

- Binary format change → the migration must be a clean invalidation, never a
  corrupted parse (the existing magic + version check already guarantees this).
- Parallelism against Ollama on CPU can degrade the machine → conservative limit,
  configurable only if proven necessary.
