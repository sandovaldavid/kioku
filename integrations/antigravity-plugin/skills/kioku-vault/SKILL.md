---
name: kioku-vault
description: Use when working with an Obsidian vault via the Kioku MCP server — searching notes, managing tasks, zettelkasten workflows, tags, links, git-backed history, and vault organization. Covers when to use each category of Kioku's 147+ tools and safety notes for destructive operations.
---

# Kioku vault skill

Kioku is an MCP server that reads, searches, and writes an Obsidian vault directly on disk. It
works even with Obsidian closed — the optional WebSocket bridge to the Obsidian plugin only
matters for a handful of UI tools (opening notes in the editor, running Obsidian commands, seeing
what's currently open).

This skill summarizes *what exists* and *when to reach for it*. For exact parameters, read the
tool's own MCP description or `docs/commands-reference.md` in the Kioku repo — don't guess
parameter names.

## Tool categories at a glance

- **Query** — `search_notes`, `search_notes_semantic`, `search_notes_hybrid`, `filter_notes`,
  `read_note`, `get_note_metadata`, `get_backlinks`, `get_outgoing_links`, `find_similar_notes`,
  `list_notes`.
- **Write** — `create_note`, `update_note_content`, `prepend_to_note`, `append_to_note`,
  `update_frontmatter`, `add_tag`, `remove_tag`, `move_note`, `rename_note`, `delete_note`.
- **Tasks** — `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`,
  `list_overdue_tasks`.
- **Zettelkasten** — `create_zettel`, `create_moc`, `create_literature_note`,
  `link_related_notes`, `create_folder_readme`.
- **Templates/workflows** — `create_note_from_template`, `list_templates`, `create_template`,
  `extract_action_items`.
- **Engineering** (per-project ADRs, bugs, plans, knowledge, backlog) — `record_adr`, `log_bug`,
  `create_plan`, `add_knowledge`, `add_backlog_item`, `get_project_context`, `list_projects`,
  `setup_agent_workflow`, template management (`list_engineering_templates`,
  `get_engineering_template`, `set_engineering_template`). Projects can be grouped in plain
  folders (e.g. `Atena/api.core`) — use `list_projects` to discover the exact identifier to pass.
  Call `get_project_context` before resuming work on a project; it's the handoff point between
  agent sessions. Documents nested in subfolders of `decisions/`, `plans/`, `knowledge/`, etc.
  (e.g. `knowledge/employee-debt/*.md`) are listed too — no need to flatten them first.
- **Organization** — `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`,
  `suggest_folder`, `reclassify_note`, `find_duplicate_notes`, `find_broken_links`,
  `audit_vault`.
- **Sessions** — `start_work_session`, `end_work_session`, `get_recent_activity`,
  `get_work_context`, `list_work_sessions`, `get_session_activity`.
- **Knowledge graph / graph analysis** — `get_concept_map`, `get_knowledge_timeline`,
  `get_vault_snapshot`, `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density`.
- **Research** — `export_citations`, `export_note`, `get_literature_gap`, `get_citation_graph`,
  `import_bibtex`, `export_bibtex`, `share_as_gist`, `validate_research_notes`.
- **Local generation** (requires Ollama) — `summarize_note`, `generate_flashcards`.
- **Restore** — `revert_note`, `list_deleted_notes`, `restore_note_from_trash`,
  `restore_note_version`, `revert_all_uncommitted`.
- **CSS theming** — `apply_css_snippet`, `list_css_snippets`, `remove_css_snippet`,
  `reload_css_snippets`.
- **Assets** — `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`,
  `normalize_attachment_names`, `move_attachments_to_folder`, `reorder_notes_in_folder`.
- **Git** — `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`,
  `commit_staged`, `fix_merge_conflicts`, `resolve_merge_conflict`.
- **Plugin bridge** (Dataview/Templater/Linter) — `query_dataview`, `apply_template`,
  `lint_note`, `lint_vault`, `get_installed_plugins`.
- **Obsidian UI** (requires the plugin + Obsidian open) — `open_note_in_obsidian`,
  `get_active_note_in_obsidian`, `trigger_obsidian_command`, `insert_at_cursor`,
  `replace_selection`, and similar.
- **Utilities** — `ping`, `get_vault_stats`, `get_index_status`, `rebuild_index`.

Any group can be disabled per-vault via `.kioku/config.yml` (`capabilities.disabled`) — if a
tool call fails as "unknown tool," check `get_vault_stats` or the vault's config before assuming
a bug.

## Reading a specific note

`read_note` takes exactly one note-reference parameter: `note` (plus an optional `format`).
It accepts a short name, a vault-relative path with or without `.md`, or an absolute path — but
not `path`, `folder`, or `note_name`, which don't exist on its schema and will fail with a
generic binding error before any Kioku code runs. If you're unsure a note exists, use
`search_notes` or `list_notes` first rather than guessing at `read_note`'s parameters.

## Search strategy

Pick the right search tool instead of defaulting to one:

- **`search_notes`** — keyword/full-text. Fast, no external dependency. Use for exact terms,
  tags, or filenames you already know.
- **`search_notes_semantic`** — meaning-based via Ollama embeddings. Use when the concept likely
  exists under different wording than your query, or keyword search returned nothing.
- **`search_notes_hybrid`** — RRF-combined keyword + semantic. **Default choice when unsure** —
  degrades gracefully to keyword-only if Ollama is unavailable.
- **`filter_notes`** — structured frontmatter queries (tag, status, type, date ranges) instead
  of free text.

## Zettelkasten conventions

- `create_zettel` — atomic, single-idea notes (the core Zettelkasten unit).
- `create_note` — general-purpose note, no Zettelkasten conventions applied.
- `create_literature_note` — notes tied to a source (book, paper, article).
- `create_note_from_template` — apply an existing template (see `list_templates`).
- `create_moc` — a Map of Content linking related notes together.
- `link_related_notes` / `suggest_links` — connect notes instead of leaving them orphaned.

Folder conventions (inbox, zettel, literature, etc.) come from the vault's own
`.kioku/config.yml` — don't hardcode folder names; check the vault's config or ask the user if
it's ambiguous.

## Task management

Tasks are native Markdown checkboxes (`- [ ]`) living inside notes, not a separate task store.
Use `list_tasks` / `list_overdue_tasks` / `list_tasks_by_tag` to find them and
`complete_task` / `reopen_task` to change their state.

## Writes reindex immediately

`update_frontmatter`, `update_note_content`, `append_to_note`, and `prepend_to_note` reindex the
note right after writing — a `get_note_metadata`/`search_notes` call immediately afterward
already reflects the change. No need to wait or retry on a stale read.

## Sessions and plan status don't auto-sync

`end_work_session` does not inspect or update any plan's `status`, even if every step in a plan
touched during the session is checked off. If you completed a plan's work in this session, call
`update_frontmatter` on that plan note yourself to set `status: done` — otherwise it keeps
showing as active/draft in `get_project_context` indefinitely.

## Reduce the number of loaded tools

Kioku ships 147+ tools across 19 groups; every connected client loads the full enabled-tool list
into context at session start. If a vault's `.kioku/config.yml` disables groups you don't use,
fewer tool schemas get loaded — same functionality for what's left, lower fixed cost per session.
Check `get_vault_stats` or the vault's config before assuming a missing tool is a bug (see
"Any group can be disabled" above). Example, disabling groups a note-taking-only workflow won't
need:

```yaml
capabilities:
  disabled: [git, css, generation, research]
```

## Safety notes for destructive or vault-wide tools

General rule: **any tool that accepts a `dry_run` parameter should be called with
`dry_run=true` first**, and the result shown to the user, before re-calling with
`dry_run=false` — unless the user has explicitly asked to skip confirmation for this specific
operation in the current turn.

This applies especially to:

- **`delete_note`** — soft-deletes to `.trash` by default, which is recoverable via
  `restore_note_from_trash`. Never pass `permanent=true` without explicit confirmation in the
  current turn — it is not recoverable the same way.
- **`revert_all_uncommitted`** — discards *all* uncommitted vault changes via git. Always
  `dry_run=true` first and show the affected file list.
- **`merge_tags`** / **`rename_tag_globally`** — vault-wide rewrites. Always `dry_run=true`
  first and show the affected-note count before committing to the change.

## Git-backed safety net

`GitTools` (`stage_note`, `stage_all`, `commit_staged`, `get_git_status`, `revert_note`,
`list_git_commits`) exists precisely so risky bulk edits can be committed and reviewed, or
reverted if something goes wrong. Before a bulk operation across many notes, it's good practice
to check `get_git_status` and suggest committing first.

## Finding the full tool list

This skill summarizes categories, not parameters. For the complete inventory with exact
parameters, see `docs/commands-reference.md` in the Kioku repo, or call the MCP `tools/list`
method directly if the repo isn't available.
