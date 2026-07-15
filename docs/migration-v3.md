---
layout: default
title: Tool Surface Migration Guide
sidebar: true
---

# Tool Surface Migration Guide

The current PR2 surface is **49 tools across 16 classes**. The generated
[`commands-reference.md`](commands-reference.md) is authoritative. This guide maps the previous
tool names to implemented tools and documents the intentional removals.

Arguments shown below are illustrative. Read the current tool schema before calling it.

## Query and note commands

| Previous tool | Current workflow |
|---|---|
| `search_notes` | Keep using `search_notes`; select `mode='keyword'`, `'semantic'`, or `'hybrid'`. |
| `search_notes_semantic` | `search_notes` with `mode='semantic'`. |
| `search_notes_hybrid` | `search_notes` with `mode='hybrid'`. |
| `filter_notes` | `list_notes` with `tag`, `status`, `type`, and date filters. |
| `get_note_metadata` | `read_note` with `metadata_only=true`. |
| `get_backlinks` | `get_links` with `direction='in'`. |
| `get_outgoing_links` | `get_links` with `direction='out'`. |
| `get_note_embedding` | No direct tool. Use `find_similar_notes` or `search_notes` with `mode='semantic'`. |
| `inspect_note_tags` | `read_note` with `metadata_only=true`, or `suggest_tags` for tag suggestions. |
| `get_vault_stats` | `get_server_status` for health/index status, or the `kioku://vault/stats` resource for vault statistics. |
| `update_note_content` | `edit_note` with `mode='replace'`. |
| `prepend_to_note` | `edit_note` with `mode='prepend'`. |
| `append_to_note` | `edit_note` with `mode='append'`. |
| `add_tag` | `update_frontmatter` with `add_tags`. |
| `remove_tag` | `update_frontmatter` with `remove_tags`. |
| `rename_note` | `move_note` with `new_name`; it can also update inbound wikilinks. |
| `create_note` | Keep using `create_note`; structured note conventions are selected with `kind`. |
| `delete_note` | Keep using `delete_note`; it soft-deletes by default. Preview with `dry_run=true`. |

## Tasks, structured notes, and templates

| Previous tool | Current workflow |
|---|---|
| `complete_task` | `set_task_state` with `completed=true`; call `list_tasks` first for the line number. |
| `reopen_task` | `set_task_state` with `completed=false`. |
| `list_tasks_by_tag` | `list_tasks` with `tag`. |
| `list_overdue_tasks` | `list_tasks` with `overdue_only=true`. |
| `create_zettel` | `create_note` with `kind='zettel'`. |
| `create_moc` | `create_note` with `kind='moc'`. |
| `create_literature_note` | `create_note` with `kind='literature'`. |
| `create_folder_readme` | `create_note` with `kind='folder-readme'`. |
| `link_related_notes` | `suggest_links`; preview by default and pass `apply=true` only after review. |
| `create_note_from_template` | `create_note` with its `template` argument, or `apply_template` when Templater evaluation is required. |
| `list_templates` | `manage_templates` with `action='list'`. |
| `create_template` | `manage_templates` with `action='set'`; it never overwrites an existing vault template. |
| `extract_action_items` | No dedicated replacement. Read the note, use `list_tasks` for existing checkboxes, and use `edit_note` to record confirmed tasks. |
| `generate_digest` | No dedicated replacement. Compose a review from `get_work_context`, `list_tasks`, `audit_vault`, and `suggest_links`; the `weekly_review` prompt provides this workflow. |

## Engineering, organization, and sessions

| Previous tool | Current workflow |
|---|---|
| `record_adr` | `create_project_doc` with `doc_type='adr'`. |
| `log_bug` | `create_project_doc` with `doc_type='bug'`. |
| `create_plan` | `create_project_doc` with `doc_type='plan'`. |
| `add_knowledge` | `create_project_doc` with `doc_type='knowledge'`. |
| `add_backlog_item` | `create_project_doc` with `doc_type='backlog'`. |
| `list_engineering_templates` | `manage_templates` with `scope='engineering'`, `action='list'`. |
| `get_engineering_template` | `manage_templates` with `scope='engineering'`, `action='get'`. |
| `set_engineering_template` | `manage_templates` with `scope='engineering'`, `action='set'`. |
| `normalize_tags` | `manage_tags` with `operation='normalize'`. |
| `rename_tag_globally` | `manage_tags` with `operation='rename'`; preview first. |
| `merge_tags` | `manage_tags` with `operation='merge'`; preview first. |
| `reclassify_note` | Combine `move_note` and `update_frontmatter`. |
| `find_broken_links` | `audit_vault`; its health report includes broken wikilinks. |
| `get_recent_activity` | `get_work_context` for current work state and recent notes. |
| `get_session_activity` | `list_work_sessions` with `include_activity=true`. |
| `get_knowledge_timeline` | No dedicated replacement. Use `get_vault_snapshot` for activity summary or `list_notes` with date filters. |
| `find_unlinked_notes` | `audit_vault` for the health report, or `get_vault_snapshot` for graph statistics. |
| `find_graph_islands` | `get_vault_snapshot` for graph islands, then `suggest_links` for candidate links. |
| `measure_vault_density` | `get_vault_snapshot`; it includes graph density. |
| `apply_link_suggestions` | `suggest_links` with `apply=true` after reviewing the preview. |

## Research, generation, CSS, assets, and integrations

| Previous tool | Current workflow |
|---|---|
| `get_citation_graph` | `audit_citations`, which combines citation graph and citation-gap reporting. |
| `get_literature_gap` | `audit_citations`, which includes citation gaps for the relevant folder. |
| `validate_research_notes` | `audit_citations`. |
| `import_bibtex` | Keep using `import_bibtex`; the `research` group is disabled by default. |
| `export_bibtex` | `export_citations` with `format='bibtex'`. |
| `export_citations` | Keep using `export_citations`; select `format='bibtex'` or `'markdown'`. |
| `export_note` | No dedicated export tool. Use `read_note` and write the desired output with `create_note` or `edit_note`. |
| `share_as_gist` | No direct replacement; use the host client's approved sharing workflow. |
| `summarize_note` | Keep using `summarize_note`; it requires the generation group and `KIOKU_GEN_MODEL`. |
| `generate_flashcards` | Keep using `generate_flashcards`; it requires the generation group and `KIOKU_GEN_MODEL`. |
| `apply_css_snippet` | `manage_css_snippets` with `action='apply'`. |
| `list_css_snippets` | `manage_css_snippets` with `action='list'`. |
| `remove_css_snippet` | `manage_css_snippets` with `action='remove'`. |
| `reload_css_snippets` | `trigger_obsidian_command` with `command_id='app:reload-css-snippets'` after applying a snippet. |
| `list_excalidraw_files` | No dedicated replacement. Use the host filesystem or `list_notes` for Markdown notes. |
| `get_asset_metadata` | No dedicated replacement. Use the host filesystem for asset metadata. |
| `find_orphan_assets` | Keep using `find_orphan_assets`; the `assets` group is disabled by default. |
| `normalize_attachment_names` | `tidy_attachments` with `normalize_names=true`. |
| `move_attachments_to_folder` | `tidy_attachments` with `target_folder`; preview with `dry_run=true`. |
| `reorder_notes_in_folder` | No direct replacement. Use native filesystem or Obsidian organization workflows. |
| `query_dataview` | Keep using `query_dataview`; the `plugin` group is disabled by default. |
| `apply_template` | Keep using `apply_template`; it requires the Obsidian plugin and Templater. |
| `lint_note` | `lint` with `scope='note'`. |
| `lint_vault` | `lint` with `scope='vault'`. |
| `get_installed_plugins` | Keep using `get_installed_plugins`. |

## Obsidian bridge and utilities

| Previous tool | Current workflow |
|---|---|
| `get_active_note_in_obsidian` | `get_obsidian_state`; it includes the active note. |
| `get_open_notes_in_obsidian` | `get_obsidian_state`; it includes open notes. |
| `get_obsidian_status` | `get_obsidian_state`; it includes bridge status. |
| `open_note_in_obsidian` | Keep using it; pass `split=true` for a split pane. |
| `insert_at_cursor` | `edit_in_obsidian` with `mode='insert_at_cursor'`. |
| `replace_selection` | `edit_in_obsidian` with `mode='replace_selection'`. |
| `create_note_ui` | Create the file with `create_note`, then open it with `open_note_in_obsidian`. |
| `scroll_to_block` | No dedicated replacement; use `open_note_in_obsidian` and the host UI. |
| `open_in_split` | `open_note_in_obsidian` with `split=true`. |
| `get_selection_in_obsidian` | `get_obsidian_state`; it includes the current selection. |
| `toggle_reading_mode` | `trigger_obsidian_command` with the relevant Obsidian command ID. |
| `fold_all_headings` | `trigger_obsidian_command` with the relevant Obsidian command ID. |
| `unfold_all_headings` | `trigger_obsidian_command` with the relevant Obsidian command ID. |
| `trigger_obsidian_command` | Keep using it. |
| `ping` | `get_server_status`. |
| `get_index_status` | `get_server_status`. |
| `rebuild_index` | Keep using `rebuild_index`. |

## Git and restore removal

The `git` and `restore` capability groups were removed. Per-tool mapping:

| Previous tool | Current workflow |
|---|---|
| `get_git_status` | `git status` in the vault directory. |
| `list_git_commits` | `git log --oneline`. |
| `stage_note` / `stage_all` / `unstage_note` | `git add <note>` / `git add -A` / `git restore --staged <note>`. |
| `commit_staged` | `git commit -m "..."`. |
| `revert_note` | `git restore -- <note>`. |
| `restore_note_version` | `git restore --source <rev> -- <note>`. |
| `revert_all_uncommitted` | No one-call replacement — inspect `git status`, then restore selected paths. |
| `fix_merge_conflicts` / `resolve_merge_conflict` | Grep for `<<<<<<<` markers and edit the note directly. |
| `list_deleted_notes` | `manage_trash` with `action='list'`. |
| `restore_note_from_trash` | `manage_trash` with `action='restore'`. |

Kioku does not emulate repository history; use native Git in a Git-backed vault:

```bash
git status
git diff -- path/to/note.md
git add path/to/note.md
git commit -m "Review vault changes"
git log -- path/to/note.md
git restore -- path/to/note.md
```

Review `git diff` and require confirmation before `git restore`, because it discards uncommitted
working-tree changes. For Kioku's own soft-deleted notes, use `manage_trash` with `action='list'`
or `action='restore'`. There is no safe one-call replacement for the previous all-files revert:
inspect `git status` and restore selected paths deliberately.

## Capability changes

The following groups are disabled by default in a new or unconfigured vault:
`research`, `generation`, `css`, `assets`, `bridge`, and `plugin`. Enable a group in
`.kioku/config.yml` when its tools are needed. `zettelkasten` is not a group anymore; structured
creation is part of `create_note`.
