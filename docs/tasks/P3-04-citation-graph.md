# P3-04 — Citation graph

| Field | Value |
|---|---|
| Priority | P3 |
| Branch | `feat/citation-graph` |
| Commit | `feat(server): add citation graph analysis for literature notes` |
| Size | M |
| Spec | — (derived from [review/05-feature-roadmap.md](../review/05-feature-roadmap.md); write a short spec in `docs/features/` before starting) |
| Dependencies | Best after [P3-01](P3-01-zotero-bibtex.md) (uses `citekey` in frontmatter) |

## Objective

Analyze which notes cite which sources (literature notes with `citekey`): most-cited
sources, orphan sources (imported but never cited), and working notes with no
bibliographic backing. Complements `get_literature_gap`.

## Proposed scope

- `get_citation_graph(folder = "")` in `ResearchTools` or `GraphAnalysisTools`: builds the
  note↔source bipartite graph from backlinks to literature notes; output with top cited,
  orphans and basic metrics.
- Reuse: `VaultIndexService`'s backlinks index, `citekey` frontmatter.
- Before implementing: write `docs/features/13-citation-graph.md` with the detailed design
  (same format as existing specs) and link it here.

## Acceptance criteria

- [ ] Spec 13 written and reviewed before the code.
- [ ] Correct cited/orphan sources with a fixture of literature notes + notes referencing
  them.
- [ ] No literature notes → clear message, not an error.
- [ ] `commands-reference.md` regenerated.

## Files

- `docs/features/13-citation-graph.md` (new, first)
- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs` or `GraphAnalysisTools.cs`
- Tests + docs
