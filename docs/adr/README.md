# Architecture decision records

Short records of Kioku's core architectural decisions: what was chosen, what was considered
instead, and why the alternative lost. Each follows the same template — Status, Context,
Decision, Alternatives rejected, Consequences — described in
[Michael Nygard's ADR format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).

| ADR | Decision |
| --- | --- |
| [0001](0001-obsidian-markdown-storage.md) | Store notes as plain Obsidian Markdown files, not a database. |
| [0002](0002-in-memory-index-persistence.md) | Build the vault index in process memory; persist only the embeddings cache. |
| [0003](0003-hybrid-search-bm25-embeddings-rrf.md) | Combine BM25 keyword search and embeddings via Reciprocal Rank Fusion. |
| [0004](0004-stdio-and-streamable-http-transports.md) | Support both the stdio and Streamable HTTP transports. |
| [0005](0005-capability-gated-tool-groups.md) | Gate optional tool groups behind vault-level capability configuration. |
| [0006](0006-local-ollama-integration.md) | Call a local Ollama instance for embeddings and generation, not a cloud API. |

These records describe decisions already implemented in the current codebase; they aren't a
proposal process. For the current state of the system these decisions produced, see
[`docs/architecture.md`](../architecture.md).
