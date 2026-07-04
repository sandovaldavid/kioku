# P3-01 — BibTeX import/export

| Field | Value |
|---|---|
| Priority | P3 |
| Branch | `feat/zotero-bibtex` |
| Commit | `feat(server): add bibtex import and export for literature notes` |
| Size | M |
| Spec | [features/10-zotero-bibtex.md](../features/10-zotero-bibtex.md) |
| Dependencies | None |

## Objective

`import_bibtex(source, folder, update_existing, dry_run)` (custom BibTeX parser, literature
notes with `citekey`/`doi` in frontmatter, dedup by citekey) and `export_bibtex(folder)`
(the reverse, from notes with a citekey) in `ResearchTools`. Live integration with Zotero is
left for a phase 2 via Better BibTeX auto-export (document the flow).

## Acceptance criteria

- [ ] Tolerant parser: malformed entries are reported per entry without aborting the
  import; common LaTeX escapes (`{\'e}`, `--`) normalized; unrecognized fields preserved.
- [ ] Dedup: reimporting the same `.bib` doesn't create duplicates; `update_existing=true`
  refreshes only frontmatter.
- [ ] Round-trip: `import_bibtex` → `export_bibtex` preserves entries (test).
- [ ] Filename collisions resolved with a citekey suffix.
- [ ] `dry_run` lists create/skip/update without writing.
- [ ] Parser tests with real `.bib` fixtures (unicode, nested braces, comments).
- [ ] `commands-reference.md` regenerated + a short guide for the Zotero → Better BibTeX →
  Kioku flow in `docs/` (section in install.md or new doc).

## Files

- `src/Kioku.Mcp.Server/Services/BibtexParser.cs` (new)
- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs`
- `src/Kioku.Mcp.Server.Tests/BibtexParserTests.cs` (new) + fixtures
- Docs
