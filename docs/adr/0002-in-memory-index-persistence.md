# ADR-0002: In-memory index with a separate embeddings cache

## Status

Accepted (implemented since the project's first version; embedding cache format has iterated,
currently version 5).

## Context

MCP tools need fast keyword, tag, and backlink lookups, plus semantic similarity, over a vault
whose files are the source of truth (see [ADR-0001](0001-obsidian-markdown-storage.md)) and can
change outside of Kioku's control at any time — Obsidian saves, a `git checkout`, a sync client.
Any index Kioku keeps is therefore a derived cache that must recover cleanly from external
changes, not a structure it can treat as authoritative.

## Decision

`VaultIndexService` builds a full index in process memory at startup
(`InitializeAsync` → `IndexVaultAsync`): an inverted word index, a tag index, a backlink index,
and per-document lengths for BM25 normalization. It stays live via a `FileSystemWatcher` with a
500ms per-file debounce that coalesces rapid successive writes into one re-index, plus explicit
`SynchronizeFileMoveAsync` / `SynchronizeFileDelete` / `SynchronizeFileReindexAsync` entry points
that tools call directly after their own filesystem operations, avoiding a race with the watcher.
`RebuildIndexAsync` forces a full rebuild from disk on demand.

Embeddings are the one part of the index expensive to recompute — each chunk costs an Ollama
round trip — so they persist separately to `{vault}/.kioku/embeddings.bin` via
`EmbeddingPersistence`, keyed by content hash so an unchanged note skips re-embedding across
restarts. The binary format's header (magic, format version, text-scheme version, model name,
dimension) invalidates the whole cache automatically if any of those change.

## Alternatives rejected

An external search index or database — SQLite FTS5, Lucene.NET, or an external vector database —
in place of the in-process structure. The measured cost of the in-process approach doesn't
justify that operational overhead at the vault sizes Kioku targets: `docs/benchmarks.md` shows
the in-memory inverted index building a 10,000-note vault in ~1.7s and a 50,000-note vault in
~6.2s, scaling roughly linearly (~0.13ms/note) with no external process to install or keep in
sync. `docs/retrieval-eval.md` documents the same conclusion for the embedding side explicitly,
under "Design decisions": brute-force SIMD cosine search is sub-10ms at typical vault sizes, and
an ANN index is only worth revisiting "above ~100k vectors."

**Grounding note:** the keyword-index half of this decision (as opposed to the embedding-ANN half,
which `docs/retrieval-eval.md` states explicitly) is inferred from the measured numbers above and
from [ADR-0001](0001-obsidian-markdown-storage.md)'s "files are source of truth, index is derived"
framing — no comment or doc says "we considered SQLite FTS5 and rejected it."

## Consequences

- The keyword/tag/backlink index rebuilds from disk on every process restart (unlike embeddings,
  it isn't persisted); acceptable at current benchmark numbers but worth revisiting if vault sizes
  or startup-latency requirements grow past them.
- Correctness after external file changes depends on the watcher plus the explicit `Synchronize*`
  recovery paths, rather than a database's built-in durability and transactional guarantees.
- The embedding cache format is versioned and self-invalidating, so model or scheme changes cost a
  one-time re-embed rather than silently serving stale vectors.
