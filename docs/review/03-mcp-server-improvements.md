# 03 — MCP Server Improvements

Concrete, mostly-incremental improvements to `Kioku.Mcp.Server` beyond the bug fixes in
[01](./01-diagnosis-and-bugs.md). Grouped by theme, ordered roughly by value/effort.

> Tags: 🔒 security · 🧱 robustness · ⚡ performance · 🧩 capability · 🧹 hygiene

---

## Security & safety 🔒

1. **Vault containment helper (P0).** One `EnsureInsideVault` used by every write tool (see BUG-1).
   This is the single highest-value change in the whole server.
2. **Auth hardening for HTTP mode.** `ApiKeyMiddleware` does constant-time comparison ✅, but there's
   no rate-limiting on 401s and no lockout. Add a simple sliding-window limiter (or document that the
   `nginx` layer must do it). Make the `/health` endpoint the *only* unauthenticated route (it
   already is) and add a `/ready` that reports index status without leaking vault contents.
3. **Destructive-op guardrails.** `delete_note`, `delete_folder`, `bulk_tag_replace`,
   `rename_tag_globally` mutate at scale. Standardize a `dry_run` parameter (some tools have it) and
   return a diff/preview by default for bulk ops; require an explicit `confirm: true` to apply.
4. **Soft-delete / trash.** Route deletes to `.kioku/trash/` (or Obsidian's `.trash`) instead of hard
   delete, so an agent mistake is recoverable. `RestoreTools` already exists — wire deletes into it.

---

## Robustness 🧱

5. **Reindex error handling (P1).** Fix the fire-and-forget continuation (BUG-2) so failed reindexes
   are logged and retried with backoff.
6. **Embedding cache integrity (P1).** Stamp `{model, dim, schemaVersion}` into `embeddings.bin`;
   invalidate + re-embed on mismatch (BUG-3). Add a CRC/length check per record so a truncated file
   doesn't poison search.
7. **FileSystemWatcher resilience.** Document and optionally auto-enable
   `DOTNET_USE_POLLING_FILE_WATCHER=1` on Linux/network mounts; add a periodic "reconcile" sweep
   (every N minutes) that compares on-disk mtimes to the index to self-heal missed events.
8. **Rebuild safety.** Guard `RebuildIndexAsync` with a generation counter or lock so watcher events
   during a rebuild can't half-populate the index.
9. **Atomic writes.** For note writes, write to a temp file + `File.Move` (as `EmbeddingPersistence`
   already does) to avoid half-written notes if the process dies mid-write.
10. **Ollama failure UX.** On embedding request failure, currently returns `null` silently
    (`EmbeddingService`). Add bounded retry with jittered backoff, and surface a one-time
    `[info]` to the agent ("semantic search degraded: Ollama unreachable") rather than empty results.

---

## Performance ⚡

11. **SIMD cosine is good — guard it.** The vectorized `CosineSimilarity` (`:271`) is a nice win;
    add an `a.Length == b.Length` assert (ties into BUG-3) and a microbenchmark so regressions are
    caught.
12. **Top-K with a heap.** For large vaults, rank semantic results with a bounded min-heap instead of
    sorting all scores; cuts allocations and time on 5k+ note vaults.
13. **Embedding batching.** Ollama supports batched inputs; batch note embeddings on rebuild to cut
    HTTP round-trips dramatically (currently ~60ms/note serially).
14. **Tool-manifest token budget.** Expose a `get_capabilities` summary and (via config) let users
    disable tool groups so the per-session manifest is smaller (see [02](./02-architecture-review.md) §6).
    This *directly* advances Kioku's "save tokens" thesis.

---

## Capability 🧩

15. **`IHttpClientFactory` (P2).** Replace static/new `HttpClient` usage (BUG-4) with named clients;
    enables timeouts/retries/telemetry per dependency (Ollama, web fetch in `ResearchTools`).
16. **Structured tool results (optional).** Today every tool returns a plain string. Consider an
    *optional* structured (JSON) variant for tools an agent post-processes (search, metadata, graph),
    so the agent doesn't re-parse prose. Keep the string form as default for readability.
17. **Pagination & limits.** Ensure every list/search tool honors `KIOKU_MAX_RESULTS` and supports a
    cursor/offset so an agent can page without blowing context.
18. **Config-v2 polish.** Define and test `ExpandTemplateVariables` semantics; add built-in variables
    (`{{date}}`, `{{time}}`, `{{title}}`, `{{uid}}`); warn once on malformed `config.yml`
    (see [02](./02-architecture-review.md) §7).
19. **Embedding model registry.** Map known models → expected dim (`nomic-embed-text`→768,
    `mxbai-embed-large`→1024, `bge-m3`→1024) so the server can validate and pick sensible defaults.

---

## Observability & hygiene 🧹

20. **Bootstrap logger.** Replace the pre-DI `Console.Error.WriteLine` (`Program.cs:17`) with a tiny
    bootstrap `ILogger` so all logs go through one path (BUG-9).
21. **Operational counters.** Expose (via `get_index_status`/`ping`) counts for: notes indexed,
    embeddings cached, last reindex time, Ollama availability, tool-call count. These double as the
    basis for opt-in telemetry (see [07](./07-production-readiness.md)).
22. **Generated `commands-reference.md`.** Emit the tool inventory from attributes at build time and
    fail CI if it drifts — kills the doc-drift class of problems for good.
23. **Consistent error taxonomy.** A small helper for the `[error] …` strings so messages are
    uniform and (optionally) carry a stable code an agent can branch on.

---

## Suggested sequencing

| Wave | Items | Theme |
|------|-------|-------|
| **A (with P0 fixes)** | 1, 5, 20 | Make writes safe + errors visible |
| **B** | 6, 7, 8, 9, 10 | Index & embedding correctness/resilience |
| **C** | 14, 17, 18, 22 | Token efficiency, config, docs-as-code |
| **D** | 12, 13, 15, 16, 19, 21 | Performance, structured outputs, observability |

---

## Implementation Status

| # | Item | Branch | PR | Status |
|---|------|--------|----|--------|
| 1 | Vault containment helper (P0) | `fix/p0-path-traversal` | — | done |
| 5 | Reindex error handling (P1) | — | — | pending |
| 6 | Embedding cache integrity (P1) | — | — | pending |
| 15 | IHttpClientFactory (P2) | — | — | pending |
| 20 | Bootstrap logger (P2) | — | — | pending |
| 22 | Generated commands-reference.md | — | — | pending |
