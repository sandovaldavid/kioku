# 02 — Wikilink auto-update on move/rename

> Area: server · Task: [P1-02](../tasks/P1-02-wikilink-auto-update.md) · Impact ★★★ · Effort M

## Motivation

`move_note` and `rename_note` (`Tools/NoteCommandTools.cs`) currently **don't update incoming
wikilinks** — the tool's own description states this as a v1 limitation. Renaming a note with
20 backlinks breaks 20 links. It's the most-cited data integrity gap, and Obsidian does solve
it in its own UI, so users expect the same from the agent.

## Design

1. New parameters `update_links` (default `true`) and `dry_run` (default `false`) on
   `move_note` and `rename_note`.
2. Before moving/renaming, fetch the notes that link to the target using the backlink
   index from `VaultIndexService.GetBacklinks(note)`.
3. In each source note, rewrite the wikilink variants pointing to the old name:
   - `[[Name]]` → `[[NewName]]`
   - `[[Name|alias]]` → `[[NewName|alias]]` (the alias is preserved)
   - `[[Name#heading]]` / `[[Name#^block]]` → the fragment is preserved
   - `![[Name]]` (embeds) → same treatment
   - For `move_note`, short-name links don't change; only path-qualified links
     (`[[Folder/Name]]`) are rewritten.
4. The rewrite is done with a new `WikilinkRewriter` helper in `Services/` (reuses
   the patterns from `MarkdownTextExtractor.ExtractWikilinks` to locate the links, and
   replaces by span — never a global regex over code blocks).
5. With `dry_run=true` it returns the plan: `N links in M notes` with a per-note preview.
6. After rewriting, reindex the touched notes (`SynchronizeFileReindexAsync`).

Tool result: `[ok] Renamed X to Y — updated N wikilinks in M notes`.

## Edge cases

- **Duplicate names** across different folders: short-name links are ambiguous.
  Rule: if another note exists with the same name, only rewrite links with a full path
  and report the ambiguous ones in the response (leave them untouched).
- Links inside code blocks or frontmatter: don't rewrite (the extractor already
  excludes them).
- Standard markdown links `[text](Name.md)`: out of scope for the feature's v1
  (document this in the tool's response).

## Affected files

- `src/Kioku.Mcp.Server/Tools/NoteCommandTools.cs` (`move_note`, `rename_note`)
- `src/Kioku.Mcp.Server/Services/WikilinkRewriter.cs` (new)
- `src/Kioku.Mcp.Server/Services/MarkdownTextExtractor.cs` (expose wikilink positions
  if needed)
- `src/Kioku.Mcp.Server.Tests/` — rewrite tests (alias, heading, embed, ambiguous,
  code blocks) + round-trip integration test
- `docs/commands-reference.md` (regenerate)

## Risks

- Incorrect rewriting = note corruption → mitigate with a thorough test suite,
  `dry_run`, and the existing `revert_note` mechanism (group `restore`).
- Large vaults: bounded, backlinks are already indexed in memory.
