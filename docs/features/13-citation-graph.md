# 13 — Citation graph

> Area: server · Task: [P3-04](../tasks/P3-04-citation-graph.md) · Impact ★★ · Effort M

## Motivation

Kioku already knows *what* sources exist (literature notes with a `citekey`, see
[10 — Zotero/BibTeX](10-zotero-bibtex.md)) and *which citekeys are missing*
(`get_literature_gap`). What's missing is the reverse view: of the sources that **do**
have a note, which are actually being used elsewhere in the vault, and which were
imported and never cited? For the researcher persona, this is the difference between a
living library and a graveyard of references imported out of inertia.

## Design

### `get_citation_graph(folder = "")` in `ResearchTools`

Builds a bipartite **working-note → source** graph from two signals, merged without
duplication:

1. **Backlinks** (`VaultIndexService.GetBacklinks(note.Name)`): any note that links
   `[[Literature Note Name]]` counts as a citation.
2. **Inline citekeys** (the same `[@citekey]` / `@citekey` pattern already used by
   `get_literature_gap`, via `InlineCitePattern`): any note that mentions `@citekey`
   in its body, even without a wikilink to the literature note.

Both signals are merged per citing note (a note that does both only counts once).
This reuses exactly the two citation mechanisms the rest of the code already
recognizes — no third citation format is introduced.

**Sources** = notes with a `citekey` in frontmatter (same criterion as
`export_citations` / `export_bibtex`: `ExtraFields["citekey"]`, falling back to
`citation-key` / `key` so as not to break vaults that don't use `import_bibtex`).

**Output** (text, same style as the rest of `ResearchTools`):
- Top most-cited sources (citekey, title, count of citing notes), sorted descending.
- Orphan sources: have a `citekey` but zero citing notes (candidates for review or
  deletion).
- Working notes with no citation to a known source — *out of scope for this first
  version*: `get_literature_gap` already covers this from the opposite angle (cited
  citekeys with no note), and listing "every note without citations" would be noisy
  for notes that legitimately aren't research notes (zettels, tasks, etc.). Can be
  added later if explicitly requested.
- Vault with no literature notes (`citekey`): a clear `[ok]` message, not an error —
  same pattern as `export_bibtex`/`export_citations` when they find no citekeys.

### `folder` parameter

Same as the rest of `ResearchTools`: if passed, only sources within that folder are
considered (the *citing* notes can be anywhere in the vault — the filter applies to
which sources are reported, not to who can cite them).

## Affected files

- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs` (+1 tool, reuses the `InlineCitePattern`
  already in the file)
- Tests: fixture with literature notes + notes citing them (via wikilink and via
  `@citekey`), orphan source, vault with no citekeys
- `docs/commands-reference.md` (regenerate)

## Risks

- Double counting if a note cites the same source both by wikilink **and** by
  `@citekey` — mitigated by merging both signals into a `HashSet` per citing note
  before counting.
- Literature note names with special characters breaking the backlink lookup —
  already mitigated by `VaultIndexService`'s existing backlink index (used unchanged).
