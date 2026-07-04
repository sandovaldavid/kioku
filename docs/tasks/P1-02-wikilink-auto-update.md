# P1-02 — Auto-update wikilinks on move/rename

| Field | Value |
|---|---|
| Priority | P1 |
| Branch | `feat/wikilink-auto-update` |
| Commit | `feat(server): update inbound wikilinks on move_note and rename_note` |
| Size | M |
| Spec | [features/02-wikilink-auto-update.md](../features/02-wikilink-auto-update.md) |
| Dependencies | None |

## Objective

Make `move_note` and `rename_note` rewrite inbound wikilinks (`[[x]]`, `[[x|alias]]`,
`[[x#h]]`, `![[x]]`) using the backlinks index, with `update_links=true` by default and a
`dry_run` preview. Removes the limitation documented in v1.

## Scope

- New `Services/WikilinkRewriter.cs` (link location reusing `MarkdownTextExtractor`'s
  logic, span-based replacement, excluding code blocks/frontmatter).
- `NoteCommandTools.move_note` / `rename_note`: new params, report
  `updated N wikilinks in M notes`, reindexing of touched notes.
- Ambiguity rule: if another note with the same name exists, don't touch short-name links
  and report them.

## Acceptance criteria

- [ ] Rewrite tests: simple name, alias, heading, block-ref, embed, links inside
  code blocks (don't touch), notes sharing a name (don't touch + report), full paths on move.
- [ ] Round-trip integration test with `VaultFixture`: rename → backlinks still resolve.
- [ ] `dry_run=true` doesn't modify any file and lists the full plan.
- [ ] `update_links=false` reproduces current behavior.
- [ ] `docs/commands-reference.md` regenerated; both tools' descriptions no longer state
  the limitation.
- [ ] End-to-end verification in a real vault: rename a note with several backlinks and
  confirm in Obsidian that the links still resolve.

## Files

- `src/Kioku.Mcp.Server/Services/WikilinkRewriter.cs` (new)
- `src/Kioku.Mcp.Server/Tools/NoteCommandTools.cs`
- `src/Kioku.Mcp.Server/Services/MarkdownTextExtractor.cs` (if positions need to be exposed)
- `src/Kioku.Mcp.Server.Tests/WikilinkRewriterTests.cs` (new) + integration
- `docs/commands-reference.md`
