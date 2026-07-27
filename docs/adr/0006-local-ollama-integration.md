# ADR-0006: Local Ollama for embeddings and generation

## Status

Accepted (implemented; documented in `docs/threat-and-privacy-model.md`'s "Ollama" section).

## Context

Semantic search and optional note generation need an embedding and LLM backend. Vault content is
often personal, private notes — `docs/threat-and-privacy-model.md` frames this explicitly
throughout ("note text used to build an embedding," "note text used as a generation prompt") — so
where that text is sent is a design decision, not an implementation detail.

## Decision

Kioku calls a local Ollama instance for both jobs. `EmbeddingService` posts note text (split into
heading-aware chunks by `NoteChunker`, built on top of `MarkdownTextExtractor`, one request per
chunk) to
`POST {KIOKU_OLLAMA_URL}/api/embeddings`, defaulting to `http://localhost:11434` (loopback), so
under default configuration no note text leaves the machine. This runs during vault indexing and
for `search_notes` in `semantic`/`hybrid` mode. `GenerationService` posts truncated
(about 4,000 characters) note-derived prompts to `POST {KIOKU_OLLAMA_URL}/api/generate`, but only
for tools in the `generation` group (disabled by default, see
[ADR-0005](0005-capability-gated-tool-groups.md)) and only when `KIOKU_GEN_MODEL` is explicitly
set. If Ollama is unreachable, both degrade gracefully: keyword search keeps working, and
semantic/generation calls report `[DEPENDENCY_UNAVAILABLE]` instead of falling back to a network
call.

## Alternatives rejected

A cloud embedding or LLM API (OpenAI, Cohere, or similar). Rejected for reasons the codebase and
threat model make explicit:

- **Privacy.** Vault notes are personal by default, and "no note text leaves the machine under
  default configuration" is the baseline `docs/threat-and-privacy-model.md` documents — a cloud
  API would break that by construction, on every embedding call, not as an opt-in.
- **No required account or per-token cost.** Ollama runs locally with no API key and no billing
  setup, so semantic search — a core feature, not an add-on — works without either.
- **Offline operation.** Indexing and keyword/semantic search work without internet connectivity;
  a cloud dependency would remove that.

This trade-off is deliberately not hard-enforced: `KIOKU_OLLAMA_URL` has no loopback or allowlist
restriction, so an operator can point it at a remote or cloud-hosted Ollama-compatible endpoint.
`docs/threat-and-privacy-model.md` calls this "the single most consequential misconfiguration for
privacy" and notes Kioku doesn't warn about it at startup — local-only is the default and intended
posture, not a constraint the server enforces.

## Consequences

- Embedding and generation quality is bounded by what runs well locally; model choice
  (`KIOKU_EMBEDDING_MODEL`, `KIOKU_GEN_MODEL`) is the operator's responsibility.
- No built-in fallback to a hosted API when Ollama is down — semantic search and generation are
  simply unavailable until it's reachable again.
- The unenforced `KIOKU_OLLAMA_URL` gap is tracked as a known future-work item in
  `docs/threat-and-privacy-model.md` rather than silently assumed safe.
