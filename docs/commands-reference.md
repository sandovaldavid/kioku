# MCP Tools Reference

> Auto-generated documentation of all MCP tools. Do not edit manually.
> Regenerate with: `dotnet run --project scripts/GenerateCommandsRef`

**Generated:** 2026-07-15 07:21 UTC

## Summary

Total tool classes: **16**

Total prompt classes: **1**

Total resource classes: **1**

## AssetTools

### `find_orphan_assets`

Find asset files (images, PDFs, Excalidraw) not referenced by any note. When dry_run=false, moves orphans to .trash/.kioku-orphans/.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dry_run` | Boolean | No | If true (default), lists orphans without moving them. |

### `tidy_attachments`

Move scattered attachments into a target folder, optionally normalize their names, and update note references.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `normalize_names` | Boolean | No | If true, rename all target-folder attachments as attachment-001.ext, attachment-002.ext, and so on. |
| `target_folder` | String | No | Vault-relative folder where attachments will be collected (for example, 'Attachments'). |
| `dry_run` | Boolean | No | If true, return the planned changes without modifying files or notes. |

## CssThemingTools

### `manage_css_snippets`

Manages CSS snippets in the Obsidian vault's .obsidian/snippets/ folder. action='list' lists snippets, action='apply' creates or updates one, and action='remove' deletes one. Use Obsidian CSS variables (--color-base-00, --text-normal, etc.) for best compatibility. After applying changes, call trigger_obsidian_command with 'app:reload-css-snippets' to activate them.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | String | No | Action to perform: 'list', 'apply', or 'remove'. |
| `name` | String | No | Snippet filename without .css extension. Required for 'apply' and 'remove'. |
| `css_content` | String | No | Valid CSS content. Required for 'apply'. Use Obsidian CSS variables for theme compatibility. |
| `enable` | Nullable`1 | No | For 'apply', if true (the default), adds the snippet to Obsidian's enabledCssSnippets list in app.json. Requires 'app:reload-css-snippets' plugin command to take effect. |

## EngineeringWorkflowTools

### `create_project_doc`

Creates an engineering document for a project. doc_type is adr, bug, plan, backlog, or knowledge; knowledge may omit project to create a general knowledge note.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `doc_type` | String | Yes | Document type: adr, bug, plan, backlog, or knowledge. |
| `project` | String | No | Project name; omit only for general knowledge. |
| `title` | String | No | Short document title. |
| `status` | String | No | Status. ADR: proposed/accepted/superseded; bug: open/fixed; plan: draft/active/done; backlog: proposed/adopted/discarded. |
| `tags` | String | No | Extra tags, comma-separated. |
| `context` | String | No | ADR context. |
| `decision` | String | No | ADR decision. |
| `consequences` | String | No | ADR consequences. |
| `alternatives` | String | No | ADR alternatives. |
| `symptom` | String | No | Bug symptom. |
| `root_cause` | String | No | Bug root cause. |
| `fix` | String | No | Bug fix. |
| `related_files` | String | No | Bug-related source files, comma-separated. |
| `objective` | String | No | Plan objective. |
| `steps` | String | No | Plan steps in markdown. |
| `ticket` | String | No | Optional plan ticket note name. |
| `content` | String | No | Knowledge content in markdown. |
| `description` | String | No | Backlog idea description. |

### `get_project_context`

Returns the current state of a project workspace: the project MOC note, summaries of recent work sessions, and per-type listings (decisions, bugs, plans, tickets, backlog, knowledge, daily). Reads fresh from disk. Call this before resuming work on a project.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). Use list_projects to discover names. |
| `include_content` | Boolean | No | Include the full content of every listed document (verbose). |
| `types` | String | No | Comma-separated type filter (adr, bug, plan, ticket, idea, knowledge, session, daily). Empty = all. |
| `limit` | Int32 | No | Maximum documents listed per type. |

### `list_projects`

Lists all project workspaces under the projects root with per-type document counts and the last modification date. Projects can be grouped in plain folders (e.g. 'Atena/api.core', 'Atena/api.common') — pass the full identifier shown here as the 'project' parameter to other engineering tools. Use to discover project names.

### `setup_agent_workflow`

Sets up the agent workflow structure in the vault: creates the projects and knowledge root folders, copies the default document templates (adr, bug, plan, knowledge, idea, session, daily, ticket, project-moc) into {templates}/kioku/ so the user can edit them in Obsidian, and documents the configuration in .kioku/config.yml. Fully idempotent: never overwrites existing files or human edits.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | No | Optional project to scaffold (creates its folder structure and MOC note). |
| `write_templates` | Boolean | No | Copy the default templates into the vault's templates folder (skips existing files). |
| `patch_config` | Boolean | No | Append a commented reference block to .kioku/config.yml if not present. |

## GenerationTools

### `generate_flashcards`

Generates flashcards from a note locally via Ollama (no cloud calls). Formats: 'spaced-repetition' (Q::A markdown, default), 'anki-csv' (front,back,tags CSV), or 'cloze' (==hidden text== cards). Requires KIOKU_GEN_MODEL and Ollama; review the cards before studying.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to generate flashcards from. |
| `count` | Int32 | No | Number of flashcards to generate (default: 10). |
| `format` | String | No | Output format: 'spaced-repetition' (default), 'anki-csv', or 'cloze'. |
| `output_note` | String | No | Path to write the flashcards to. Default: 'Flashcards/{note}.md' ('.csv' for anki-csv, in the assets folder). |
| `dry_run` | Boolean | No | Preview the generated flashcards without writing any file. |

### `summarize_note`

Summarizes a note locally via Ollama (no cloud calls). Styles: 'bullets' (default), 'paragraph', 'eli5'. Requires KIOKU_GEN_MODEL and Ollama; output quality depends on the local model.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to summarize. |
| `style` | String | No | Summary style: 'bullets' (default), 'paragraph', or 'eli5'. |
| `max_words` | Int32 | No | Approximate maximum word count for the summary (default: 150). |

## GraphAnalysisTools

### `suggest_links`

Suggests or adds wikilinks that don't exist yet. Provide 'targets' to explicitly choose targets; otherwise semantic candidates are generated for 'note', or for the whole vault when 'note' is empty. Suggestions are a dry run by default. Set apply=true to apply them. Explicit targets work without Ollama; semantic mode falls back to structural orphan/island analysis when Ollama is unavailable.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | No | Name or path of a note to suggest or add links for. Leave empty for vault-wide mode. |
| `targets` | String | No | Comma-separated target note names/paths. When provided, these explicit targets take precedence over semantic suggestions. |
| `section` | String | No | Heading for the links section (default: 'Related'). |
| `apply` | Boolean | No | If true, apply the suggestions. The default false only previews them. |
| `max_suggestions` | Int32 | No | Maximum number of semantic suggestions to return or apply (default: 10). |
| `min_similarity` | Single | No | Minimum semantic similarity score 0.0–1.0 (default: 0.7). |

## KnowledgeGraphTools

### `get_concept_map`

Returns a JSON graph centered on a specific note: nodes (notes) and edges (links). Edges include outgoing wikilinks, backlinks, and (optionally) semantic similarity. Use 'depth' to control traversal depth (1=direct links, 2=links of links). Use 'max_nodes' to limit graph size. The graph JSON can be visualized with tools like Obsidian Graph View, D3.js, or Cytoscape.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the center note for the concept map. |
| `depth` | Int32 | No | Traversal depth: 1 = direct links only, 2 = links of links (default: 2, max: 3). |
| `max_nodes` | Int32 | No | Maximum number of nodes to include (default: 50, max: 150). |

### `get_vault_snapshot`

Returns a comprehensive snapshot of the vault in a single call: folder tree with note counts, top tags by frequency, frontmatter coverage stats, recent activity summary, graph density, unlinked notes, and graph islands. Combines note listing, metadata coverage, and graph analysis in one report.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `island_threshold` | Int32 | No | Maximum connected-component size to report as a graph island (default: 3). |

## NoteCommandTools

### `create_note`

Creates a note in the vault. kind='note' (default) creates a regular note; 'zettel', 'literature', 'moc', and 'folder-readme' preserve the corresponding structured creation conventions. Use template with kind='note' to render a vault template while keeping generated frontmatter.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | String | No | Note name or vault-relative path. For zettel/literature this is the title. |
| `content` | String | No | Markdown body. Required for kind='note' and kind='zettel'. |
| `kind` | String | No | 'note' (default), 'zettel', 'literature', 'moc', or 'folder-readme'. |
| `tags` | String | No | Comma-separated tags for note, zettel, or literature kinds. |
| `type` | String | No | Frontmatter type for a regular note. Empty uses configured note defaults. |
| `status` | String | No | Frontmatter status for a regular note. Empty uses configured note defaults. |
| `folder` | String | No | Target folder for structured kinds, or an optional folder for a regular note name. |
| `template` | String | No | Vault-relative template path, used for kind='note'. |
| `author` | String | No | Literature author(s), required for kind='literature'. |
| `year` | String | No | Literature publication year, required for kind='literature'. |
| `source` | String | No | Literature source or URL. |
| `summary` | String | No | Literature summary. |
| `link_related` | Boolean | No | For kind='zettel', automatically add related wikilinks. |
| `max_links` | Int32 | No | For kind='zettel', maximum related notes to link. |
| `output_name` | String | No | For kind='moc', optional output filename without extension. |
| `output_folder` | String | No | For kind='moc', optional output folder. |

### `delete_note`

Deletes a note from the vault by moving it to .trash folder (recoverable). Set permanent=true to delete immediately (irreversible). When dry_run is true, only reports what would be deleted without modifying the vault.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to delete. |
| `dry_run` | Boolean | No | If true, only reports what would be deleted without modifying the vault. |
| `permanent` | Boolean | No | If true, deletes permanently instead of moving to trash. Default: false (soft delete). |

### `edit_note`

Edits the body of an existing note, keeping its YAML frontmatter intact. mode='replace' (default) replaces the whole body, 'append' adds at the end, 'prepend' inserts just after the frontmatter.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `content` | String | Yes | The content to write (in Markdown). |
| `mode` | String | No | 'replace' (default), 'append', or 'prepend'. |
| `add_separator` | Boolean | No | Append mode only: adds a horizontal separator (---) before the new content. |

### `manage_trash`

Manages the vault trash. action='list' (default) shows deleted notes in '.trash' or '.obsidian/trash'; action='restore' moves a note out of the trash back into the vault (to the vault root, or the folder given in destination).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | String | No | 'list' (default) or 'restore'. |
| `note` | String | No | Restore only: name or path of the note in the trash. |
| `destination` | String | No | Restore only: target folder (vault-relative). Defaults to vault root. |
| `dry_run` | Boolean | No | Restore only: if true, reports what would be restored without moving the file. |

### `move_note`

Moves and/or renames a note. Provide destination_folder to move, new_name to rename (may include subfolders), or both. When the name changes, inbound wikilinks (bare name, full path, aliases, headings, block refs, embeds) are rewritten; bare-name links shared by another note are skipped and reported. When only the folder changes, just full-path links are rewritten. update_links=false skips rewriting; dry_run=true previews.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to move or rename. |
| `destination_folder` | String | No | Destination folder (relative to the vault). E.g. 'Archive/2024'. Empty = keep folder. |
| `new_name` | String | No | New name (without .md, may include subfolders). Empty = keep name. |
| `update_links` | Boolean | No | If true (default), rewrites inbound wikilinks to the note's new location. |
| `dry_run` | Boolean | No | If true, previews the change without modifying any file. |

### `update_frontmatter`

Updates or adds fields in the YAML frontmatter of an existing note. Only modifies specified fields, the rest remains intact. Use add_tags/remove_tags to change tags incrementally, or tags to replace them all.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `tags` | String | No | New tags (replaces existing ones, comma-separated). Leave empty to not modify. |
| `status` | String | No | New status (e.g. 'published', 'draft', 'archived'). Leave empty to not modify. |
| `type` | String | No | New note type. Leave empty to not modify. |
| `clear_tags` | Boolean | No | If true, removes all tags regardless of the 'tags' argument. |
| `add_tags` | String | No | Tag(s) to add to the existing set (comma-separated). |
| `remove_tags` | String | No | Tag(s) to remove from the existing set (comma-separated). |

## NoteQueryTools

### `find_similar_notes`

Finds notes conceptually similar to a given note using semantic embeddings. Unlike search_notes (which takes a text query), this takes a note and finds notes similar to it — useful for discovering hidden connections. Requires Ollama.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the source note. |
| `max_results` | Int32 | No | Maximum number of similar notes to return (default: 10). |
| `min_score` | Single | No | Minimum similarity score 0.0–1.0 (default: 0.5). |

### `get_links`

Returns the wikilink connections of a note. direction='in' lists notes linking TO it (backlinks), 'out' lists wikilinks FROM it, 'both' (default) lists both. Use format='json' for a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `direction` | String | No | 'both' (default), 'in' (backlinks), or 'out' (outgoing). |
| `format` | String | No | 'text' (default) or 'json'. |

### `list_notes`

Lists notes in the vault or a folder, optionally filtered by frontmatter metadata (tag, status, type, date range — combined with AND). Supports pagination via offset and limit. Use format='json' for a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to list (relative to the vault). Leave empty for the entire vault. |
| `tag` | String | No | Filter by tag (e.g. 'project'). |
| `status` | String | No | Filter by frontmatter status (e.g. 'draft'). |
| `type` | String | No | Filter by note type (e.g. 'zettel'). |
| `date_from` | String | No | Minimum frontmatter date (YYYY-MM-DD). |
| `date_to` | String | No | Maximum frontmatter date (YYYY-MM-DD). |
| `limit` | Int32 | No | Maximum notes to return (default: 50, capped by KIOKU_MAX_RESULTS). |
| `offset` | Int32 | No | Number of notes to skip for pagination. |
| `format` | String | No | 'text' (default) or 'json'. |

### `read_note`

Reads an Obsidian note. Accepts note name (without extension), vault-relative path, or absolute path. metadata_only=true returns just the YAML frontmatter metadata (tags, aliases, status, type, dates, outgoing link count) without the content. Use format='json' for a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. E.g. 'My Note', 'Projects/Kioku', '/home/user/vault/note.md' |
| `metadata_only` | Boolean | No | Return only frontmatter metadata, not the content. |
| `format` | String | No | 'text' (default) or 'json'. |

### `search_notes`

Searches notes. mode='hybrid' (default) combines keyword and semantic search via Reciprocal Rank Fusion and degrades to keyword-only without Ollama; mode='keyword' matches title/content/tags exactly; mode='semantic' matches by meaning (requires Ollama). Use format='json' for a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | String | Yes | Search query: keywords or natural language. |
| `mode` | String | No | 'hybrid' (default), 'keyword', or 'semantic'. |
| `max_results` | Int32 | No | Maximum number of results (default: 10). |
| `min_score` | Single | No | Minimum score 0.0–1.0 to include a result. Default: 0.4 for semantic, no filter otherwise. |
| `format` | String | No | 'text' (default) or 'json'. |

## ObsidianBridgeTools

### `edit_in_obsidian`

Edits the active Obsidian note. mode must be 'insert_at_cursor' or 'replace_selection'.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `text` | String | Yes | Text to insert or use as the replacement. |
| `mode` | String | Yes | 'insert_at_cursor' to insert at the cursor, or 'replace_selection' to replace the current selection. |

### `get_obsidian_state`

Returns a snapshot of Obsidian's bridge status, active note, open notes, and selection. Individual sections may report errors if a bridge request fails.

### `open_note_in_obsidian`

Opens and focuses a specific note within Obsidian. Set split=true to open it in a new split pane.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to open. |
| `split` | Boolean | No | Open the note in a new split pane instead of the current pane. |

### `trigger_obsidian_command`

Triggers an internal Obsidian command by its unique identifier (command ID).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `command_id` | String | Yes | Unique ID of the command (e.g., 'app:toggle-left-sidebar', 'workspace:close-others'). |

## PluginIntegrationTools

### `apply_template`

Creates a new note from a Templater template via the Obsidian plugin bridge. Requires Obsidian to be open with the Kioku plugin and Templater plugin enabled. The template is instantiated by Templater — all template variables (tp.date, tp.file, etc.) are evaluated.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `template_path` | String | Yes | Vault-relative path to the Templater template file. Example: 'Templates/Daily Note.md' |
| `target_note` | String | No | Optional: vault-relative path of an existing note to apply the template to. |

### `get_installed_plugins`

Returns a list of all installed Obsidian plugins with their ID, name, version, author, and enabled status. Requires Obsidian to be open with the Kioku plugin. Use this to check if a required plugin (e.g. 'dataview', 'templater-obsidian') is available before calling plugin-dependent tools.

### `lint`

Runs the Obsidian Linter plugin with scope='note' or scope='vault'. For note scope, lints a specific note or the currently active note; vault scope lints all notes. Requires Obsidian to be open with the Kioku plugin and the 'obsidian-linter' plugin enabled. Linter fixes formatting issues according to the user's configured Linter rules.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `scope` | String | Yes | Lint scope: exactly 'note' or 'vault'. |
| `note` | String | No | For note scope, vault-relative path of the note to lint. Leave empty to lint the currently active note. |

### `query_dataview`

Executes a Dataview DQL query via the Obsidian plugin bridge and returns results as JSON. Requires Obsidian to be open with the Kioku plugin and Dataview plugin enabled. Supports TABLE, LIST, TASK queries and inline expressions.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | String | Yes | Dataview DQL query. Example: 'TABLE status, tags FROM "Projects" WHERE status = "active" SORT file.mtime DESC' |

## ResearchTools

### `audit_citations`

Audits citations in one combined report: citation graph and orphan sources, inline citation gaps, and required metadata on research/literature notes. The folder scopes source and audit notes; citation graph citers are still searched across the entire vault.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to scope source notes, inline-gap notes, and metadata validation (vault-relative). Leave empty for the entire vault. |

### `export_citations`

Exports citation keys found in note frontmatter as a full-fidelity BibTeX document or Markdown table. The BibTeX format preserves fields imported by import_bibtex for round-trip export. Accepted formats are exactly 'bibtex' and 'markdown'.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `format` | String | No | Export format: 'bibtex' for a round-trip BibTeX document or 'markdown' for a Markdown table (default: markdown). |
| `folder` | String | No | Folder to scan (vault-relative). Leave empty to scan the entire vault. |

### `import_bibtex`

Imports a BibTeX (.bib) file or raw BibTeX content as literature notes, one per entry. Parses tolerantly: malformed entries are reported individually rather than aborting the whole import. Deduplicates by 'citekey' — re-importing the same file never creates duplicates. All BibTeX fields are stored in frontmatter, so export_citations(format='bibtex') can reconstruct the original entries losslessly. Use dry_run=true to preview before writing.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `source` | String | Yes | Path to a .bib file (absolute, vault-relative, or CWD-relative), or raw BibTeX content. |
| `folder` | String | No | Folder to create literature notes in. Default: the configured 'literature' folder, or 'Literature'. |
| `update_existing` | Boolean | No | If a note with the same citekey already exists, refresh its frontmatter fields (body is left untouched). Default: skip existing entries. |
| `dry_run` | Boolean | No | Preview what would be created/updated/skipped without writing any files. |

## SessionContextTools

### `end_work_session`

Closes the current work session by appending a summary of notes modified since the session started. Updates the session note status to 'done'. For project sessions, the summary is also written into the '## Summary' section at the top of the note so the next agent reads it first via get_project_context.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `session_note` | String | No | Name or path of the session note to close. If empty, finds the most recent active session. |
| `summary` | String | No | Optional summary or outcome of the session. Strongly recommended for project sessions: it is the handoff for the next agent. |
| `project` | String | No | Project name: looks for the active session under {projects}/{project}/sessions/. |

### `get_work_context`

Returns a snapshot of the vault's current work state: notes in inbox folders, notes with status 'draft', and the most recently modified notes. Call this at the start of a session to quickly understand where to resume work.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `inbox_folder` | String | No | Folder treated as the inbox (relative to vault root). Default: 'Inbox'. |
| `max_per_section` | Int32 | No | Maximum number of notes to show in the inbox, drafts, and recent sections unless recent_limit is set. |
| `recent_folder` | String | No | Scope the recently modified section to a subfolder (relative to vault root). Leave empty for the full vault. |
| `recent_limit` | Int32 | No | Maximum number of notes in the recently modified section. Defaults to max_per_section. |

### `list_work_sessions`

Lists all work session notes with their dates, status (active/done), and duration if closed. Optionally includes the notes modified during each session.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `sessions_folder` | String | No | Folder where session notes are stored (relative to vault root). Auto-detects if empty. |
| `project` | String | No | Project name: lists the sessions under {projects}/{project}/sessions/. |
| `include_activity` | Boolean | No | Include notes modified during each session. |

### `start_work_session`

Creates a new work session note with a timestamp header. Records the current date, time, and optional session goal. With a project, the session is stored in that project's sessions subfolder as {date-time}-{agent}.md so multiple agents can hand work off to each other; the agent name is auto-detected from the MCP client when not provided.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `session_name` | String | No | Optional name for the session (e.g. 'Thesis Chapter 3 Review'). Defaults to today's date. |
| `sessions_folder` | String | No | Folder where session notes are stored (relative to vault root). Ignored when project is set. |
| `goal` | String | No | Optional goal or focus for this session. |
| `project` | String | No | Project name: stores the session under {projects}/{project}/sessions/. |
| `agent` | String | No | Agent running this session (claude, codex, ...). Auto-detected from the MCP client if empty. |
| `server` | McpServer | No |  |

## TaskManagementTools

### `list_tasks`

Lists all tasks (open and completed) across the vault or within a specific note. Supports filtering by completion status, tag, and overdue date. Returns task text, note name, line number, due date, and inline tags.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | No | Name or path of a specific note to scan. Leave empty to scan the entire vault. |
| `status` | String | No | Filter by completion status: 'open' (default), 'done', or 'all'. |
| `folder` | String | No | Folder to restrict the search (relative to vault root). Only used when 'note' is empty. |
| `tag` | String | No | Optional tag to match in task text or note frontmatter, without the '#' prefix. |
| `overdue_only` | Boolean | No | Only return open tasks whose due date is in the past. |

### `set_task_state`

Sets a task's completion state at the specified line in a note. Use list_tasks first to find the note name and line number of the task.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note containing the task. |
| `line_number` | Int32 | Yes | 1-based line number of the task within the note. |
| `completed` | Boolean | Yes | True to mark the task complete ('- [x]'); false to reopen it ('- [ ]'). |

## UtilityTools

### `get_server_status`

Returns the current Kioku server health and status: vault path, indexed note count, cached embeddings, Ollama availability, last update time, index readiness, and — if a re-embedding backlog is being processed in the background — its progress (backlog, rate, ETA).

### `rebuild_index`

Forces a full re-indexing of the entire vault. Useful if the index got out of sync or massive changes were made outside of Obsidian.

## VaultOrganizationTools

### `audit_vault`

Generates a health report of the vault: notes without tags, without dates, without content, broken wikilinks, and notes not updated in a long time.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `stale_days` | Int32 | No | Flag notes not updated in this many days (default: 90). |

### `find_duplicate_notes`

Detects notes with very similar titles or content that may be duplicates. Always operates as a dry run — reports findings without modifying the vault.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `threshold` | Single | No | Similarity threshold (0.0–1.0). Higher = only very similar notes. Default: 0.8. |
| `max_results` | Int32 | No | Maximum number of duplicate pairs to report. |

### `manage_tags`

Manages tags across the entire vault. operation must be 'normalize', 'rename', or 'merge'. Rename uses old_tag/new_tag; merge uses source_tag/target_tag. Use dry_run=true to preview changes without modifying files.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `operation` | String | Yes | Operation to perform: normalize, rename, or merge. |
| `old_tag` | String | No | Tag to rename from when operation is 'rename'. |
| `new_tag` | String | No | Tag to rename to when operation is 'rename'. |
| `source_tag` | String | No | Tag to merge away when operation is 'merge'. |
| `target_tag` | String | No | Tag to keep when operation is 'merge'. |
| `dry_run` | Boolean | No | If true, returns a preview without modifying any files. |

### `process_inbox`

Batch-triages notes in an inbox folder: for each note, suggests a destination folder (same scoring as suggest_folder), tags (keyword overlap + destination folder inheritance), and up to 3 related notes (semantic similarity, when Ollama embeddings are available). apply=false (default) returns a numbered plan without touching any file. apply=true executes it: moves each note (updating inbound full-path wikilinks), adds the suggested tags, and appends a Related section. Review the plan before applying; git can undo an apply.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `inbox_folder` | String | No | Inbox folder (relative to vault root). Leave empty to use folders.inbox from .kioku/config.yml, falling back to 'Inbox'. |
| `max_notes` | Int32 | No | Maximum number of notes to process in one call (default: 20). |
| `apply` | Boolean | No | If true, executes the plan (move + tag + link). Default false only previews it. |

### `suggest_folder`

Suggest the most appropriate vault folder(s) for a note based on content similarity to existing notes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to suggest a folder for. |
| `max_suggestions` | Int32 | No | Maximum number of folder suggestions to return. |

### `suggest_tags`

Reports a note's existing, folder-inherited, and excluded tag state, then suggests relevant existing vault tags using keyword overlap.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to inspect and suggest tags for. |
| `max_suggestions` | Int32 | No | Maximum number of tag suggestions to return. |

## WorkflowTools

### `manage_templates`

Manages note templates. scope='vault' handles templates in the vault's configured templates folder; scope='engineering' handles the engineering document templates and their vault overrides. action is list, get, or set. Vault set never overwrites an existing file.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `scope` | String | No | Template scope: 'vault' or 'engineering'. |
| `action` | String | No | Action: 'list', 'get', or 'set'. |
| `name` | String | No | Vault template name without .md. Required for vault get/set. |
| `type_key` | String | No | Engineering template type: adr, bug, plan, knowledge, idea, session, daily, ticket, or project-moc. Required for engineering get/set. |
| `content` | String | No | Template body. Required for engineering set unless reset_to_default=true; optional for vault set. |
| `templates_folder` | String | No | Vault templates folder relative to the vault. Leave empty to auto-detect. |
| `reset_to_default` | Boolean | No | For engineering set, delete the vault override and use the embedded default. |

## Prompts

### `literature_review`

Collects existing evidence on a topic from the vault and synthesizes it with citations.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `topic` | String | Yes | Topic or research question to review. |

### `log_bugfix`

Logs a bug and its fix for a project so future agents do not re-debug it.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). |

### `plan_feature`

Drafts an implementation plan for a feature after checking prior art.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). |
| `feature` | String | Yes | Feature to plan. |

### `process_inbox`

Guides the smart-inbox triage workflow: propose a plan, confirm it, then apply it.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `inbox` | String | No | Inbox folder to process (relative to vault root). Leave empty to use the configured default. |

### `record_decision`

Records an architecture decision for a project.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). |
| `topic` | String | Yes | Topic of the decision, e.g. 'database choice'. |

### `research_digest`

Summarizes recent reading/research activity in the vault and lists open questions.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to scope the review to (relative to vault root). Leave empty for the whole vault. |

### `resume_project`

Loads a project's engineering context before resuming work.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). |

### `weekly_review`

Runs a weekly vault review: activity, overdue tasks, vault health, and link suggestions.

### `work_on_ticket`

Reads a human-written ticket, structures it, and creates a linked implementation plan.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). |
| `ticket` | String | Yes | Ticket note name or path (under the project's tickets folder). |

### `write_daily`

Drafts today's daily note for a project from recent sessions and the previous daily.

**Arguments:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `project` | String | Yes | Project name (folder under the projects root). |

## Resources

### `kioku://note/{path}`

Full content (including frontmatter) of a note, resolved by vault-relative path or name.

MIME type: `text/markdown`

### `kioku://vault/stats`

Snapshot of vault statistics: note count, tag count, folder count, index status.

MIME type: `application/json`

---

**Total tools:** 49

**Total prompts:** 10

**Total resources:** 2
