# Resilient vault indexing pipeline

Kioku processes cold scans and `FileSystemWatcher` events through one bounded indexing pipeline.

```text
FileSystemWatcher / reconciliation scan
        -> bounded Channel<VaultFileChange> (capacity 2048)
        -> per-path coalescing and 500 ms debounce
        -> configurable worker pool
        -> VaultIndexService + bounded EmbeddingService
        -> readiness and operational metrics
```

## Configuration

| Setting | Default | Valid range | Purpose |
|---|---:|---:|---|
| `KIOKU_INDEX_CONCURRENCY` / `Kioku:IndexConcurrency` | `max(1, CPU/2)` | 1–128 | Maximum simultaneous parse/index operations. |
| `KIOKU_EMBEDDING_CONCURRENCY` / `Kioku:EmbeddingConcurrency` | 2 | 1–128 | Maximum simultaneous Ollama embedding requests and background backlog workers. |

The queue is intentionally bounded. When it cannot accept another watcher event, Kioku does not silently assume that the index is correct: it requests a full reconciliation.

## Correctness behavior

- Repeated writes to the same path are coalesced and applied once after the debounce window.
- Rename events preserve the old path so stale postings and cached embeddings are removed or re-keyed.
- Delete/recreate sequences are resolved from the final filesystem state.
- Watcher overflow or error schedules a reconciliation scan.
- Reconciliation removes indexed paths that no longer exist and reindexes every current Markdown file.
- Readiness reports `rebuilding` while a full scan is active, avoiding a false ready signal while readers may observe incremental replacement.
- A successful recovery scan returns readiness to `ready`; an unrecoverable scan failure reports `failed`.
- Transient `IOException` and `UnauthorizedAccessException` failures use three bounded retries with cancellation-aware backoff.
- Embedding backlog enumeration uses `Parallel.ForEachAsync`; it no longer creates one task for every stale note.
- Server startup remains non-blocking while embeddings are generated. Semantic workflows that require a complete initial corpus, such as link suggestions, wait for the tracked backlog with a bounded timeout.
- Shutdown disables the watcher, completes the channel, drains pending work, and only then allows the host lifecycle to persist the embedding cache.

## Observability

`get_server_status` reports:

- current queue depth;
- processed, failed, and coalesced changes;
- reconciliation count;
- last scan duration;
- maximum observed indexing concurrency;
- embedding initialization duration through the internal metrics snapshot.

No note names or contents are captured by these metrics.

## Reproducible load and concurrency checks

The `VaultIndexingPipelineTests` suite creates synthetic vaults and verifies:

- a 500-note cold start never exceeds configured concurrency;
- 100 rapid edits produce one effective reindex;
- simulated watcher failure discovers a missed file through reconciliation;
- cancellation stops scheduling a 1,000-note cold scan;
- delete and recreate sequences do not leave stale notes.

`EmbeddingConcurrencyTests` additionally verifies that the background embedding backlog never exceeds the configured Ollama concurrency.

Run the suites with:

```bash
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj \
  --filter "FullyQualifiedName~VaultIndexingPipelineTests|FullyQualifiedName~EmbeddingConcurrencyTests"
```

For machine-specific throughput and memory evidence, run the suite under the platform profiler (`dotnet-trace`, `dotnet-counters`, Windows Performance Recorder, or `/usr/bin/time -v`) and record:

1. cold-start files divided by the reported last-scan duration;
2. peak working set during the 1,000-note cancellation scenario or an expanded 10,000-note fixture;
3. p95 end-to-end update latency from watcher event creation until queue depth and active operations return to zero.

Fixed benchmark numbers are intentionally not committed because runner hardware varies. Release evidence should record the commit SHA, OS, CPU, .NET SDK, vault size, configured concurrency, throughput, peak memory, and p95 latency together.
