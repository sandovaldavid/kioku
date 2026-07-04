# 08 — Smart inbox

> Area: server · Task: [P2-04](../tasks/P2-04-smart-inbox.md) · Impact ★★ · Effort S

## Motivation

Processing the inbox (classifying captures: folder + tags + links) is the quintessential
repetitive work of a second brain. Kioku already has the pieces (`suggest_folder`,
`suggest_tags`, `find_similar_notes`, `FolderRanker`) but the agent has to orchestrate
them note by note, spending tokens. A tool that does this in batch locally is the
direct realization of the product's thesis.

## Design

### `process_inbox(inbox_folder = "", max_notes = 20, apply = false)`

In `VaultOrganizationTools` (group `organization`):

- `inbox_folder` default: `folders.inbox` from `.kioku/config.yml` (fallback `"Inbox"`).
- For each note in the inbox (up to `max_notes`):
  1. **Suggested folder** — `FolderRanker.RankFolders` (top-1 + score).
  2. **Suggested tags** — `suggest_tags` logic (inheritance from target folder + similar
     notes).
  3. **Suggested links** — top-3 from `HybridSearchService.FindSimilar` (if embeddings
     are available).
- `apply = false` (default): returns the **plan** per note, numbered:
  `1. "Capture X" → Research/Papers · tags: [paper, ml] · links: [[A]], [[B]]`.
- `apply = true`: executes the full plan — moves (`move_note` with wikilink updates if
  spec 02 is already merged), applies tags (`add_tag`), and adds the related-notes
  section (reusing the apply from spec 06). Reports what was done per note.

### `apply_inbox_plan(items)`

A more granular variant: takes the accepted subset of indices/notes from a previous
plan, for the "propose everything, apply only these" flow. (If this complicates v1, it
can be postponed: `apply=true` already covers the basic flow.)

## Affected files

- `src/Kioku.Mcp.Server/Tools/VaultOrganizationTools.cs` (+1-2 tools)
- Reuse: `FolderRanker`, `NoteHelpers.MergeTagsWithInheritance`, `HybridSearchService`
- Tests: correct plan with fixture (config folders/domains), idempotent apply
- `docs/commands-reference.md` (regenerate)

## Risks

- Moving notes in batch is the most destructive operation in the catalog → default
  `apply=false`, and remind in the output that `revert_all_uncommitted`/git exist
  (groups `restore`/`git`).
- Without Ollama: folder/tags still work (FolderRanker mixes in token overlap); links
  are skipped with a warning.
- Softly depends on specs 02 and 06 (better experience), but doesn't block.
