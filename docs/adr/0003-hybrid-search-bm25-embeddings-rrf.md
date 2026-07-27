# ADR-0003: Hybrid search — BM25 keyword search, embeddings, and Reciprocal Rank Fusion

## Status

Accepted (BM25 replaced a naive TF scorer on 2026-07-12; heading-aware chunking landed
2026-07-14, per `docs/retrieval-eval.md`).

## Context

`search_notes` has to satisfy two different kinds of queries. Some share exact words with the
target note (including typos, where fuzzy/keyword matching helps); others are paraphrases that
share no keywords with the target note at all — `docs/retrieval-eval.md`'s golden-set authoring
guidance calls out both cases explicitly, and the fixture vault includes "semantic twins" (same
meaning, different words) specifically to exercise the gap. Pure keyword search misses the second
case; pure semantic search misses exact-term and typo cases and is more expensive per query
(`docs/benchmarks.md` measures semantic search at ~30ms p50 versus ~1.4ms for keyword, dominated
by the Ollama embedding round trip). The two scoring systems also live on incompatible scales —
BM25 is an unbounded IDF-weighted sum, cosine similarity is bounded in [-1, 1] — so combining raw
scores would need model- and vault-specific calibration.

## Decision

`VaultIndexService.Search` implements Okapi BM25 (`k1=1.2`, `b=0.75`) with relative title and tag
boosts for the keyword leg. `EmbeddingService` provides the semantic leg over Ollama-generated,
heading-aware-chunked embeddings (chunking keeps long notes from exceeding the model's context
window; results aggregate back to one score per note via max-pooling across its chunks).
`HybridSearchService.Search` fuses the two ranked lists with Reciprocal Rank Fusion —
`score(d) = Σ 1 / (k + rank)`, `k=60` — which its own docstring states is "parameter-free and
robust to different score scales": RRF only consumes each leg's rank position, never its raw
score, so the two systems never need score-scale calibration against each other.

## Alternatives rejected

A weighted linear combination of raw scores (`score = α·BM25 + β·cosine`). This would require
empirically calibrating `α`/`β` per vault and per embedding model, and re-validating that
calibration every time the embedding model changes — exactly the coupling RRF is built to avoid
by fusing on rank instead of score. `docs/retrieval-eval.md`'s measured baselines support keeping
both legs rather than picking a single "winning" scorer: BM25 alone improved precision and
ordering over the prior naive TF scorer (P@5 0.227→0.245, MRR 0.784→0.788), and hybrid mode is
kept even where pure semantic search scores higher on some metrics, because hybrid is what
actually catches both exact-term and semantic-twin queries in the same pass — the eval process's
explicit "improve or hold" gate governs every change here, not single-metric maximization.

## Consequences

- The only fusion tuning knob is `k=60` (a standard RRF constant); `keyword_weight` and
  `semantic_weight` scale each leg's contribution without touching the fusion math.
- Whole-note embeddings alone missed one note that exceeded the model's context window
  (`docs/retrieval-eval.md`'s truncation probe); chunking mitigates this but adds one Ollama
  request per chunk instead of per note.
- Retrieval quality is checked by CI (`RetrievalRankingTests.cs`, deterministic fake embedder) as
  floors and invariants, and separately by the real-model `Kioku.Eval` runner — the two are
  intentionally not the same test.
