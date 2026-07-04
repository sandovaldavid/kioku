# 10 — Zotero / BibTeX bridge

> Area: server · Task: [P3-01](../tasks/P3-01-zotero-bibtex.md) · Impact ★★★ · Effort M

## Motivation

For the researcher persona (thesis, papers), the library lives in Zotero/BibTeX. Today
`create_literature_note` is manual, note by note. Importing an entire library with
citation keys in frontmatter turns Kioku into the vault ↔ reference-manager bridge
(★★★ feature of the research roadmap).

## Design — phase 1: BibTeX (no network dependencies)

### `import_bibtex(source, folder = "", update_existing = false, dry_run = false)`

In `ResearchTools` (group `research`):

- `source`: path to a `.bib` file (inside or outside the vault) **or** inline BibTeX
  content.
- A custom, scoped BibTeX parser (`@article/@book/@inproceedings/...` entries, fields
  `author, title, year, journal, doi, url, abstract`): no new NuGet package if
  reasonable (~200 lines), `FrontmatterParser` style. Handles nested braces `{...}`
  and comments.
- Per entry: creates a literature note via the `create_literature_note` logic, with
  extra frontmatter `citekey: <key>`, `doi`, `source-url`.
- **Dedup by `citekey`**: if a note with that citekey already exists, skip it (or
  update the frontmatter if `update_existing=true`, without touching the body).
- `dry_run`: reports what would be created/skipped.

### `export_bibtex(folder = "")` (complement)

Reverse: rebuilds a `.bib` file from literature notes that have a `citekey`
(complements `export_citations`, which currently exports markdown).

## Phase 2 (later, optional): live Zotero

Integration via **Better BibTeX auto-export** (Zotero exports the `.bib` to disk and
Kioku re-imports it — zero network code) rather than Zotero's local HTTP API
(`:23119`), which adds coupling. Document the recommended flow in the guide.

## Affected files

- `src/Kioku.Mcp.Server/Services/BibtexParser.cs` (new)
- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs` (+2 tools)
- Reuse: `ZettelkastenTools.create_literature_note` internals (extract a helper if
  appropriate), `NoteHelpers.SanitizeFileName`
- Tests: parser (malformed entries, unicode, nested braces), dedup, import→export
  round-trip
- `docs/commands-reference.md` (regenerate)

## Risks

- Real-world BibTeX is messy (LaTeX escapes `{\'e}`, non-standard fields) → tolerant
  parser: keep the raw value of unrecognized fields in `ExtraFields`, never fail the
  whole import because of one bad entry (report per entry).
- Filename collisions between papers with the same title → suffix with the citekey.
