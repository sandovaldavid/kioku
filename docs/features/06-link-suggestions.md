# 06 — Link suggestions between notes

> Area: server · Task: [P2-02](../tasks/P2-02-link-suggestions.md) · Impact ★★★ · Effort M

## Motivation

The value of a vault is its **graph**. `link_related_notes` (writes a related-notes
section into a note) and `find_graph_islands`/`find_unlinked_notes` (diagnostics)
already exist, but there's no vault-level "propose links and apply them with a click"
flow. It's the ★★★ feature of the roadmap's cross-cutting block.

## Design

### `suggest_links(note = "", max_suggestions = 10, min_similarity = 0.7)`

In `GraphAnalysisTools` (group `graph-analysis`):

- With `note`: candidates by semantic similarity (`HybridSearchService.FindSimilar`)
  that **aren't linked yet** in either direction (filtered against backlinks +
  outgoing links from the index).
- Without `note` (vault mode): prioritizes orphan notes (`find_unlinked_notes`) and
  islands (`find_graph_islands`), returning `(source, target, score, reason)` pairs.
- Output: numbered list with score, context snippet, and the reason
  (`semantic-similarity` | `orphan-rescue` | `island-bridge`).

### `apply_link_suggestions(note, targets, section = "Related")`

In the same group:

- `targets`: list of names/paths (the suggestions accepted by the user/agent).
- Adds (or extends) a `## Related` section at the end of the note with
  `- [[target]] — reason` per entry. Doesn't touch the existing body; idempotent
  (doesn't duplicate links already present).
- `dry_run` for previewing.

Reuses the insertion logic from `link_related_notes` (extract a common helper if
appropriate) instead of duplicating it.

## Affected files

- `src/Kioku.Mcp.Server/Tools/GraphAnalysisTools.cs` (+2 tools)
- `src/Kioku.Mcp.Server/Services/HybridSearchService.cs` (reuse of `FindSimilar`)
- Possible shared helper with `ZettelkastenTools.link_related_notes`
- Tests: filtering of already-linked notes, apply idempotency, orphans/islands
- `docs/commands-reference.md` (regenerate)

## Risks

- Requires embeddings (Ollama) — degrade with a clear message if `EmbeddingService`
  isn't available (without Ollama, only the structural `island-bridge` mode works).
- Low-quality suggestions with a low `min_similarity` → conservative default (0.7).
