# ADR-0001: Obsidian Markdown as durable storage

## Status

Accepted (implemented since the project's first version).

## Context

Kioku is memory for AI agents that operates directly on a user's existing Obsidian vault — a
folder of `.md` files with YAML frontmatter that Obsidian, the user, other Obsidian plugins
(Dataview, Templater), and tools like Git can all read and write independently of whether Kioku
is running. `Note.cs`, `NoteMetadata.cs`, and `FrontmatterParser.cs` show what that means for the
data model: `Note` carries the file's `RawContent` verbatim alongside derived fields, and
`NoteMetadata.ExtraFields` explicitly preserves any frontmatter key Kioku doesn't recognize as a
standard field, rather than dropping it. `FrontmatterParser` is documented as a "compatibility
facade" over `FrontmatterDocument`, and treats invalid or incomplete frontmatter as empty for
indexing rather than rejecting the file.

## Decision

Kioku stores nothing outside the vault's own Markdown files as the source of truth. Every read
tool works from an in-memory index that is rebuilt from files on disk (see
[ADR-0002](0002-in-memory-index-persistence.md)); every write tool (`create_note`, `edit_note`,
`update_frontmatter`, and so on) edits the `.md` file directly. Kioku never owns a private schema
that the vault's files are exported from or synced into.

## Alternatives rejected

A SQLite (or other embedded database) source of truth, with Markdown files generated as a
read-only export or view. This would require Kioku to reconcile the database against whatever
Obsidian, the user, or Git did to the files independently — a two-way sync problem that plain
files avoid by construction, since the file *is* the record. It would also break the core
promise that a vault stays fully usable in Obsidian, in a text editor, or under version control
with Kioku turned off.

**Grounding note:** no code comment or prior doc states this rejection explicitly — it's inferred
from the product's Obsidian-vault-first framing (README, `CLAUDE.md`) and from how `Note`,
`NoteMetadata`, and `FrontmatterParser` are built to tolerate and preserve arbitrary vault content
rather than normalize it into a Kioku-owned schema.

## Consequences

- Kioku doesn't control transactional consistency: two agents editing the same note's body can
  race at the content level (see `docs/threat-and-privacy-model.md`, "Concurrent agents").
- `FrontmatterParser.Parse` must degrade gracefully (return `NoteMetadata.Empty`) on invalid YAML
  instead of failing, since Kioku doesn't control what's in the files.
- Any index or cache Kioku builds is necessarily a derived, rebuildable structure, not a system of
  record — this is the premise [ADR-0002](0002-in-memory-index-persistence.md) builds on.
