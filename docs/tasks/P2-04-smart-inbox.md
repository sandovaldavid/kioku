# P2-04 — Smart inbox

| Field | Value |
|---|---|
| Priority | P2 |
| Branch | `feat/smart-inbox` |
| Commit | `feat(server): add process_inbox batch triage tool` |
| Size | S |
| Spec | [features/08-smart-inbox.md](../features/08-smart-inbox.md) |
| Dependencies | No hard dependency; better UX with P1-02 (wikilinks) and P2-02 (applying links) |

## Objective

`process_inbox(inbox_folder, max_notes, apply = false)` in `VaultOrganizationTools`: for
each note in the inbox, proposes a folder (`FolderRanker`), tags (inheritance + similar
notes) and links (top-3 semantic); with `apply=true` executes the full plan (move + tags +
related notes).

## Acceptance criteria

- [ ] `apply=false` (default) doesn't modify anything and returns the numbered per-note
  plan.
- [ ] `apply=true` executes and reports per note what it did; moved notes keep their
  frontmatter and content; with P1-02 merged, inbound wikilinks get updated.
- [ ] Without Ollama: folder/tags still work (token overlap), links are skipped with a
  warning.
- [ ] Empty inbox / nonexistent folder → clear messages, not errors.
- [ ] The output reminds the user of the revert mechanisms (`revert_all_uncommitted`, git).
- [ ] Tests with `VaultFixture` (plan, apply, degradation) + `commands-reference.md`
  regenerated.

## Files

- `src/Kioku.Mcp.Server/Tools/VaultOrganizationTools.cs`
- Reuse: `FolderRanker`, `NoteHelpers`, `HybridSearchService`
- Tests + docs
