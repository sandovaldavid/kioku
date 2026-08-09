# Resilient vault indexing pipeline

Kioku processes cold scans and `FileSystemWatcher` events through one bounded indexing pipeline.

```text
Generic Host / MCP transport starts
        -> background cold reconciliation
        -> bounded Channel<VaultFileChange> (capacity 2048)
        -> per-path coalescing and 500 ms debounce
        -> configurable worker pool
        -> VaultIndexService + bounded EmbeddingService
        -> cold-index readiness gate + operational metrics
```

The MCP transport is not blocked on a complete vault cold scan. Index-dependent operations still cannot observe a partial index: they wait on the explicit cold-index readiness gate until deterministic reconciliation completes.

## Configuration

| Setting | Default | Valid range | Purpose |
|---|---:|---:|---|
| `KIOKU_INDEX_CONCURRENCY` / `Kioku:IndexConcurrency` | `max(1, CPU/2)` | 1–128 | Maximum simultaneous parse/index operations. |
| `KIOKU_EMBEDDING_CONCURRENCY` / `Kioku:EmbeddingConcurrency` | 2 | 1–128 | Maximum simultaneous Ollama embedding requests and background backlog workers. |

The queue is intentionally bounded. When it cannot accept another watcher event, Kioku does not silently assume that the index is correct: it requests a full reconciliation.

## Startup and readiness

Runtime initialization runs in the background rather than blocking Generic Host startup.

The startup sequence is:

```text
process starts
  -> MCP transport can finish starting
  -> background runtime initialization begins
       -> deterministic cold reconciliation
       -> cold-index gate becomes ready
       -> embeddings initialize
       -> optional generation probe initializes
  -> full runtime readiness becomes ready after initialization succeeds
```

While cold reconciliation is still running, these operations are intentionally warm-up-safe because they do not depend on the shared note index:

- `get_server_capabilities`;
- `get_server_status`;
- `list_projects`;
- `get_project_context`.

Index-dependent tools, dynamic resource enumeration, mutations, sessions, search, note resolution, coordination/CAS/fencing, and graph operations wait on the cold-index gate rather than executing against a partial corpus.

A caller cancellation while waiting is propagated to that call. A requested host shutdown cancels in-progress initialization without reclassifying an orderly shutdown as an indexing failure. Genuine initialization/reconciliation failures remain observable and fail readiness.

## Correctness behavior

- Repeated writes to the same path are coalesced and applied once after the debounce window.
- Rename events preserve the old path so stale postings and cached embeddings are removed or re-keyed.
- Delete/recreate sequences are resolved from the final filesystem state.
- Watcher overflow or error schedules a reconciliation scan.
- Reconciliation removes indexed paths that no longer exist and reindexes every current Markdown file.
- Index readiness reports `rebuilding` while deterministic reconciliation is active; the cold-index gate does not become ready until that scan completes.
- A successful recovery scan returns index readiness to `ready`; an unrecoverable scan failure reports `failed`.
- Transient `IOException` and `UnauthorizedAccessException` failures use three bounded retries with cancellation-aware backoff.
- Embedding backlog enumeration uses `Parallel.ForEachAsync`; it no longer creates one task for every stale note.
- MCP startup remains non-blocking while both the cold scan and later embedding/generation initialization run in the background; correctness is preserved by the explicit cold-index gate.
- Semantic workflows that require a complete initial embedding corpus, such as link suggestions, wait for the tracked embedding backlog with a bounded timeout.
- Shutdown disables the watcher, completes the channel, drains pending work, and only then allows the host lifecycle to persist the embedding cache.

## Observability

`get_server_status` is warm-up-safe and reports runtime/index state while startup work is still in progress. Its indexing diagnostics include:

- current queue depth;
- processed, failed, and coalesced changes;
- reconciliation count;
- last scan duration;
- maximum observed indexing concurrency;
- embedding initialization duration through the internal metrics snapshot.

No note names or contents are captured by these metrics.

For HTTP deployments, liveness and readiness are intentionally different signals: the process/transport may be live while background runtime initialization is still preventing full readiness. See [Troubleshooting](troubleshooting.md) and [Streamable HTTP authentication](deploy/auth-options.md).

## Reproducible load and concurrency checks

The `VaultIndexingPipelineTests` and startup-readiness regressions verify, among other cases:

- Generic Host startup can return while runtime initialization is deliberately blocked;
- the cold-index readiness gate completes on success and propagates failure/cancellation correctly;
- warm-up-safe and index-dependent MCP operations remain explicitly classified;
- a 500-note cold start never exceeds configured concurrency;
- 100 rapid edits produce one effective reindex;
- simulated watcher failure discovers a missed file through reconciliation;
- cancellation stops scheduling a 1,000-note cold scan;
- delete and recreate sequences do not leave stale notes.

`EmbeddingConcurrencyTests` additionally verifies that the background embedding backlog never exceeds the configured Ollama concurrency.

Run the focused indexing suites with:

```bash
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj \
  --filter "FullyQualifiedName~VaultIndexingPipelineTests|FullyQualifiedName~EmbeddingConcurrencyTests|FullyQualifiedName~KiokuStartupReadinessGateTests"
```

For machine-specific throughput and memory evidence, run the suite under the platform profiler (`dotnet-trace`, `dotnet-counters`, Windows Performance Recorder, or `/usr/bin/time -v`) and record:

1. cold-start files divided by the reported last-scan duration;
2. peak working set during the 1,000-note cancellation scenario or an expanded 10,000-note fixture;
3. p95 end-to-end update latency from watcher event creation until queue depth and active operations return to zero.

Fixed performance numbers are intentionally not committed because runner hardware varies. Release evidence should record the commit SHA, OS, CPU, .NET SDK, vault size, configured concurrency, throughput, peak memory, and p95 latency together.
