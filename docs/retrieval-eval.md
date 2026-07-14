# Retrieval quality evaluation

How Kioku measures whether its search tools (`search_notes`, `search_notes_semantic`,
`search_notes_hybrid`) actually return the right notes, and how to compare configurations
(embedding model, thresholds, scoring changes) with numbers instead of gut feeling.

## Scope

Kioku is a retrieval-only MCP server: it returns notes, the client LLM generates answers.
Therefore only retrieval metrics apply here. Generation-side metrics (RAGAS faithfulness,
answer relevancy, groundedness) are out of scope by design — there is no generation step
to evaluate inside this server.

Metrics implemented in `src/Kioku.Mcp.Server/Domain/RetrievalMetrics.cs`:

| Metric | Question it answers |
|--------|---------------------|
| Precision@k | Of the k results returned, how many are relevant? |
| Recall@k | Of all relevant notes, how many made it into the top k? |
| MRR | How high does the first relevant note rank? |
| NDCG@k | Is the full ordering close to ideal, weighting by graded relevance? |

## The golden set

A golden set is a list of real queries annotated with the notes a good search should
return, with a graded relevance (1 = somewhat relevant, 2 = relevant, 3 = exactly what
the query asks for). Format (`src/Kioku.Mcp.Server.Tests/Fixtures/golden-set.json`):

```json
{
  "queries": [
    { "id": "q01", "query": "notas sobre burnout laboral",
      "relevant": [ { "path": "Salud/Burnout Laboral.md", "grade": 3 } ] },
    { "id": "q23-no-answer", "query": "quantum entanglement research papers",
      "relevant": [] }
  ]
}
```

Authoring guidance:

- Use queries you actually type, including typos and paraphrases that share no keywords
  with the target note (those exercise the semantic leg).
- Include queries answered by tags, by title, by aliases, and by content buried deep in a
  long note.
- Include 2-3 queries with an empty `relevant` list ("no-answer probes"): a good
  configuration returns few or no results for them, so they measure noise/threshold quality.
- Paths are vault-relative with `/` separators; grades 1-3.

The checked-in fixture vault (`src/Kioku.Mcp.Server.Tests/Fixtures/EvalVault/`, 27 mixed
Spanish/English notes) contains topic clusters, keyword distractors (same words, different
meaning), semantic twins (same meaning, different words), alias-only matches and one very
long note with a unique fact near the end (a truncation probe: whole-note embeddings get
cut at the model context window, so only keyword search finds it today).

## Running the evaluation

```bash
# Keyword only — works without Ollama
dotnet run --project scripts/Kioku.Eval -- --modes keyword --label baseline

# All modes — requires Ollama with the configured embedding model
dotnet run --project scripts/Kioku.Eval -- --label baseline-nomic

# Against your real vault with your own golden set
dotnet run --project scripts/Kioku.Eval -- \
  --vault ~/vault --golden ~/vault/.kioku/golden-set.json --min-score 0.4

# Compare embedding models (cache auto-invalidates on model change)
KIOKU_EMBEDDING_MODEL=qwen3-embedding:0.6b dotnet run --project scripts/Kioku.Eval -- --label qwen3
```

The runner boots the same `VaultIndexService` + `EmbeddingService` + `HybridSearchService`
stack the MCP server uses (no transport), waits for the embedding backlog to drain, and
prints one Markdown table per mode. Compare tables between runs with different `--label`s;
only keep a change if Recall@10 / NDCG@10 improve or hold.

## CI regression tests

`src/Kioku.Mcp.Server.Tests/RetrievalRankingTests.cs` runs the golden set through all
three search paths on every `dotnet test`, without Ollama, using a deterministic fake
embedder (`DeterministicEmbeddingHandler.cs`: hashed bag-of-words vectors, so cosine
similarity correlates with lexical overlap). Assertions are floors and invariants — never
exact orderings — so they catch ranking regressions without overfitting to the fake.
Real-model quality is measured only with the runner above.

## Baseline

Fixture vault (27 notes), 22 scored queries + 2 no-answer probes.

### keyword — naive TF scoring (pre-BM25), 2026-07-12

| k | Precision@k | Recall@k | MRR | NDCG@k |
|---|-------------|----------|-----|--------|
| 5 | 0.227 | 0.621 | 0.784 | 0.722 |
| 10 | 0.132 | 0.682 | 0.784 | 0.744 |

No-answer probes: avg 5.0 results returned.

### keyword — Okapi BM25 (k1=1.2, b=0.75, relative title/tag boosts), 2026-07-12

| k | Precision@k | Recall@k | MRR | NDCG@k |
|---|-------------|----------|-----|--------|
| 5 | 0.245 | 0.652 | 0.788 | 0.741 |
| 10 | 0.127 | 0.667 | 0.788 | 0.750 |

No-answer probes: avg 5.0 results returned.

Net effect of BM25: better early precision and ordering (P@5, R@5, MRR, NDCG@k all up);
Recall@10 dips marginally because IDF demotes one weakly-relevant match. Kept per the
"improve or hold" gate — rank quality is what the hybrid RRF fusion consumes.

### Semantic threshold default

`search_notes_semantic` now defaults to `min_score = 0.4` (explicit `0` disables the
filter). The value is a conservative starting point for nomic-embed-text with task
prefixes; validate it against your own golden set by sweeping `--min-score` with the
runner and watching Precision@k versus the no-answer probes.

### semantic / hybrid — nomic-embed-text (with query/document task prefixes), 2026-07-14

Fixture vault, 26/27 notes embedded — `Referencias/Historia de la Computacion.md` fails
every attempt because its content exceeds the model's context window (`n_ctx_slot = 2048`
tokens) and is excluded from semantic/hybrid results as a result (a known limitation of
whole-note embeddings; see "Design decisions" below). `min_score = 0` (the runner's
default; the server itself defaults to `0.4`, see above).

| k | Precision@k | Recall@k | MRR | NDCG@k |
|---|-------------|----------|-----|--------|
| 5 | 0.318 | 0.826 | 0.955 | 0.885 |
| 10 | 0.191 | 0.955 | 0.955 | 0.913 |

No-answer probes: avg 10.0 results returned.

### hybrid — nomic-embed-text (with query/document task prefixes), 2026-07-14

| k | Precision@k | Recall@k | MRR | NDCG@k |
|---|-------------|----------|-----|--------|
| 5 | 0.255 | 0.667 | 0.854 | 0.791 |
| 10 | 0.177 | 0.894 | 0.854 | 0.845 |

No-answer probes: avg 10.0 results returned.

Both modes clear the keyword baseline on Recall@k, MRR and NDCG@k by a wide margin, at
the cost of noisier no-answer probes (10.0 avg results vs. keyword's 5.0) — expected with
`min_score = 0`; the server's own `0.4` default trades some recall for that noise
reduction (sweep `--min-score` against your own golden set to tune it further).

```bash
dotnet run --project scripts/Kioku.Eval -- --label baseline-nomic
```

## Design decisions (what was deliberately not built)

- **LLM contextual enrichment (Anthropic contextual retrieval)**: adds an LLM call per
  chunk on every re-index. Obsidian vault notes change constantly, so the enrichment cost
  repeats forever; the planned deterministic breadcrumb prefix (note name + heading path)
  captures most of the benefit for free.
- **Late chunking**: needs token-level embeddings; Ollama's embeddings API returns pooled
  vectors only. Not implementable against Ollama.
- **Cross-encoder reranking**: no local cross-encoder runtime available. Revisit only if
  eval numbers show Precision@5 is the bottleneck after chunking lands.
- **ANN index**: brute-force SIMD cosine over a whole vault is sub-10ms at typical vault
  sizes. Revisit above ~100k vectors.
- **Chunking + parent-document retrieval**: planned as the next iteration (heading-aware
  chunks embedded individually, results aggregated back to note level). The golden set is
  annotated at note level so it survives that change unchanged.
