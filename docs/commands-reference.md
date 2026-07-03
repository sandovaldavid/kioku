# MCP Tools Reference

> Auto-generated documentation of all MCP tools. Do not edit manually.
> Regenerate with: `dotnet run --project scripts/GenerateCommandsRef`

**Generated:** 2026-07-03 01:10 UTC

## Summary

Total tool classes: **17**

## AssetTools

### `find_orphan_assets`

Find asset files (images, PDFs, Excalidraw) not referenced by any note. When dry_run=false, moves orphans to .trash/.kioku-orphans/.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dry_run` | Boolean | No | If true (default), lists orphans without moving them. |

### `get_asset_metadata`

Return metadata (name, path, size, last modified) for a non-Markdown asset file in the vault.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | String | Yes | Vault-relative path to the asset file (e.g. 'Attachments/diagram.png'). |

### `list_excalidraw_files`

List all Excalidraw files in the vault: standalone .excalidraw files and Markdown notes with 'excalidraw: true' in frontmatter.

### `move_attachments_to_folder`

Move scattered attachment files to a centralized folder and update all references in notes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `target_folder` | String | Yes | Target folder path where attachments will be moved (e.g., 'Attachments'). |
| `dry_run` | Boolean | No | If true, returns a preview without moving files. |

### `normalize_attachment_names`

Rename attachment files with a consistent pattern (note-slug-N.ext) and update all references in notes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `asset_folder` | String | Yes | Asset folder path containing attachments to normalize (e.g., 'Attachments', 'Assets'). |
| `dry_run` | Boolean | No | If true, returns a preview without renaming files. |

### `reorder_notes_in_folder`

Rename notes in a folder with numeric prefixes (01-, 02-, …) to define explicit ordering.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | Yes | Vault-relative folder path containing the notes to reorder. |
| `dry_run` | Boolean | No | If true, returns a preview without renaming files. |

## CssThemingTools

### `apply_css_snippet`

Creates or updates a CSS snippet file in the Obsidian vault's .obsidian/snippets/ folder. Use Obsidian CSS variables (--color-base-00, --text-normal, etc.) for best compatibility. After applying, call trigger_obsidian_command with 'app:reload-css-snippets' to activate it.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | String | Yes | Snippet filename without .css extension (e.g. 'sepia-editor', 'custom-tags'). |
| `css_content` | String | Yes | Valid CSS content. Use Obsidian CSS variables for theme compatibility. |
| `enable` | Boolean | No | If true, adds the snippet to Obsidian's enabled snippets list in app.json. Requires 'app:reload-css-snippets' plugin command to take effect. |

### `list_css_snippets`

Lists all CSS snippet files in the vault's .obsidian/snippets/ folder, showing their enabled/disabled status and a preview of their content.

### `remove_css_snippet`

Removes a CSS snippet file from the Obsidian vault's .obsidian/snippets/ folder. Also removes it from the enabledCssSnippets list in app.json.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | String | Yes | Snippet name without .css extension (e.g. 'sepia-editor'). |

## GitTools

### `commit_staged`

Commits all staged changes with the given message. Returns an informational message if there is nothing to commit.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `message` | String | Yes | Commit message describing the changes. |

### `fix_merge_conflicts`

Scans all Markdown notes in the vault for Git merge conflict markers (<<<<<<<, =======, >>>>>>>). Returns a list of affected notes with the conflicting sections. Does not modify any files — use resolve_merge_conflict to resolve conflicts. Does not require Obsidian to be running.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to scan (vault-relative). Leave empty to scan the entire vault. |

### `get_git_status`

Shows the current git status of the vault repository (modified, added, deleted files).

### `list_git_commits`

Lists the most recent git commits in the repository.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `max_count` | Int32 | No | Maximum number of commits to return (default: 10) |

### `resolve_merge_conflict`

Resolves a specific Git merge conflict in a note by choosing one version. Use 'ours' to keep the HEAD version, 'theirs' to keep the incoming version, or 'both' to concatenate both versions. Does not require Obsidian to be running. The FileSystemWatcher will automatically re-index the note after resolution.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or vault-relative path of the note with conflicts. |
| `conflict_index` | Int32 | No | Index of the conflict to resolve (0-based). Use -1 to resolve all conflicts at once. |
| `version` | String | No | Which version to keep: 'ours' (HEAD), 'theirs' (incoming), or 'both'. |

### `stage_all`

Stages all changes across the entire vault using git add -A. Includes modified, deleted, and new (untracked) files.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dry_run` | Boolean | No | If true, only reports what would be staged without running git add. |

### `stage_note`

Stages a note for commit using git add. Prepares the file to be included in the next commit.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to stage. |
| `dry_run` | Boolean | No | If true, only reports what would be staged without running git add. |

### `unstage_note`

Unstages a previously staged note using git restore --staged. Removes the file from the staging area without discarding changes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to unstage. |

## GraphAnalysisTools

### `find_graph_islands`

Finds connected components in the graph smaller than a threshold (graph islands). Small isolated clusters often indicate notes that should be linked to the main graph.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `threshold` | Int32 | No | Maximum size of a connected component to be considered an island (default: 3). |

### `find_unlinked_notes`

Finds all notes with no outgoing links and no backlinks (completely isolated from the graph).

### `measure_vault_density`

Computes vault graph density metrics: average links per note, percentage of notes with backlinks, connectivity profile.

## KnowledgeGraphTools

### `get_concept_map`

Returns a JSON graph centered on a specific note: nodes (notes) and edges (links). Edges include outgoing wikilinks, backlinks, and (optionally) semantic similarity. Use 'depth' to control traversal depth (1=direct links, 2=links of links). Use 'max_nodes' to limit graph size. The graph JSON can be visualized with tools like Obsidian Graph View, D3.js, or Cytoscape.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the center note for the concept map. |
| `depth` | Int32 | No | Traversal depth: 1 = direct links only, 2 = links of links (default: 2, max: 3). |
| `max_nodes` | Int32 | No | Maximum number of nodes to include (default: 50, max: 150). |

### `get_knowledge_timeline`

Returns notes ordered chronologically by their frontmatter 'date' field. Useful for reviewing the evolution of ideas over time. Optionally filter by tag, folder, or date range. Notes without a 'date' frontmatter field are excluded.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `tag` | String | No | Filter by tag (without #). Leave empty for all notes. |
| `folder` | String | No | Folder path relative to vault root. Leave empty for all folders. |
| `date_from` | String | No | Start date (inclusive) in YYYY-MM-DD format. Leave empty for no lower bound. |
| `date_to` | String | No | End date (inclusive) in YYYY-MM-DD format. Leave empty for no upper bound. |
| `max_results` | Int32 | No | Maximum number of notes to return (default: 50, max: 200). |

### `get_vault_snapshot`

Returns a comprehensive snapshot of the vault in a single call: folder tree with note counts, top tags by frequency, frontmatter coverage stats, and recent activity summary. Replaces the need to call list_notes + get_vault_stats + multiple get_note_metadata.

## NoteCommandTools

### `add_tag`

Adds one or more tags to an existing note.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `tags` | String | Yes | Tag(s) to add (comma-separated). |

### `append_to_note`

Appends text to the end of an existing note. Ideal for log notes or journals where the agent records entries.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `content` | String | Yes | Text to append to the end of the note (in Markdown). |
| `add_separator` | Boolean | No | If true, appends a horizontal separator (---) before the new content. |

### `create_note`

Creates a new note in the Obsidian vault with frontmatter and content. If the note already exists, returns an error — use update_note_content to modify it.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | String | Yes | Name of the note (without .md extension). Can include subfolders: 'Projects/My Note'. |
| `content` | String | Yes | Content of the note body (in Markdown). |
| `tags` | String | No | Tags to add in the frontmatter (comma-separated). E.g. 'ai, project, draft'. |
| `type` | String | No | Note type for frontmatter (e.g. 'note', 'project', 'area', 'resource'). |
| `status` | String | No | Status of the note (e.g. 'draft', 'published'). |

### `delete_note`

Deletes a note from the vault by moving it to .trash folder (recoverable). Set permanent=true to delete immediately (irreversible). When dry_run is true, only reports what would be deleted without modifying the vault.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to delete. |
| `dry_run` | Boolean | No | If true, only reports what would be deleted without modifying the vault. |
| `permanent` | Boolean | No | If true, deletes permanently instead of moving to trash. Default: false (soft delete). |

### `move_note`

Moves a note to another folder in the vault. Note: wikilinks pointing to this note are not updated automatically in v1.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to move. |
| `destination_folder` | String | Yes | Destination folder (relative to the vault). E.g. 'Archive/2024' |

### `prepend_to_note`

Prepends text to the beginning of a note body (just after the YAML frontmatter).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `content` | String | Yes | Text to prepend (in Markdown). |

### `remove_tag`

Removes one or more tags from an existing note.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `tags` | String | Yes | Tag(s) to remove (comma-separated). |

### `rename_note`

Renames a note in the vault. The new name can include subfolders.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to rename. |
| `new_name` | String | Yes | New name of the note (without .md extension, e.g. 'New Folder/New Name'). |

### `update_frontmatter`

Updates or adds fields in the YAML frontmatter of an existing note. Only modifies specified fields, the rest remains intact.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `tags` | String | No | New tags (replaces existing ones, comma-separated). Leave empty to not modify. |
| `status` | String | No | New status (e.g. 'published', 'draft', 'archived'). Leave empty to not modify. |
| `type` | String | No | New note type. Leave empty to not modify. |

### `update_note_content`

Replaces the body of an existing note keeping its YAML frontmatter intact.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `content` | String | Yes | New content of the body of the note. |

## NoteQueryTools

### `filter_notes`

Filters notes by YAML frontmatter metadata. All parameters are optional — combined with AND. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `tag` | String | No | Filter by tag (e.g. 'project', 'ai', 'reference'). |
| `status` | String | No | Filter by frontmatter status (e.g. 'draft', 'published', 'archived'). |
| `type` | String | No | Filter by note type (e.g. 'note', 'project', 'area'). |
| `date_from` | String | No | Minimum date in frontmatter (format: YYYY-MM-DD). |
| `date_to` | String | No | Maximum date in frontmatter (format: YYYY-MM-DD). |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `find_similar_notes`

Finds notes conceptually similar to a given note using semantic embeddings. Unlike search_notes_semantic (which takes a text query), this tool takes a note name and finds notes similar to it — useful for discovering hidden connections in the vault. Requires Ollama.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the source note. |
| `max_results` | Int32 | No | Maximum number of similar notes to return (default: 10). |
| `min_score` | Single | No | Minimum similarity score 0.0–1.0 (default: 0.5). |

### `get_backlinks`

Returns all notes linking to the specified note via [[wikilinks]]. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note_name` | String | Yes | Name of the target note (without .md extension). |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `get_note_embedding`

Returns diagnostic information about the embedding vector of a note. Shows the vector dimensions and a preview of the first values. Use to verify that a note has been indexed for semantic search.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |

### `get_note_metadata`

Reads only the YAML frontmatter metadata of a note, without loading its full content. More efficient than read_note when only metadata is needed. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `get_outgoing_links`

Returns all wikilinks outgoing from the specified note. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `get_vault_stats`

Returns general statistics of the vault: total notes, unique tags, folders, and index status. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `inspect_note_tags`

Returns the current tag state of a note to help an AI agent decide which new tags to add. Reports existing tags, folder-inherited tags from config.yml auto_tags, and frontmatter fields that must not be duplicated as tags. After reading this, the AI agent should call add_tag with any missing semantic tags.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or vault-relative path of the note. |

### `list_notes`

Lists notes in the vault or a specific folder. Supports pagination via offset and limit. Returns name, relative path, tags, and modified date. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to list (relative to the vault). Leave empty to list the entire vault. |
| `limit` | Int32 | No | Maximum number of notes to return (default: 50, capped by KIOKU_MAX_RESULTS). |
| `offset` | Int32 | No | Number of notes to skip for pagination. |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `read_note`

Reads the full content of an Obsidian note. Accepts note name (without extension), vault-relative path, or absolute path. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. E.g. 'My Note', 'Projects/Kioku', '/home/user/vault/note.md' |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `search_notes`

Searches notes in the vault by text in title, content, and tags. Returns results ordered by relevance with a snippet of context. Use format='json' to receive a structured response.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | String | Yes | Text to search. Can include multiple keywords. |
| `max_results` | Int32 | No | Maximum number of results to return (default: 10). |
| `format` | String | No | Output format: 'text' (default) or 'json'. |

### `search_notes_hybrid`

Searches notes combining keyword and semantic search using Reciprocal Rank Fusion (RRF). Finds notes that match by exact terms AND by conceptual meaning. Best general-purpose search when you are unsure whether to use keyword or semantic search. Requires Ollama for the semantic leg — degrades to keyword-only if Ollama is unavailable.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | String | Yes | Search query in natural language or keywords. |
| `max_results` | Int32 | No | Maximum number of results to return (default: 10). |
| `min_score` | Single | No | Minimum RRF score 0.0–1.0 to include a result (default: 0.0 = no filter). |
| `keyword_weight` | Single | No | Weight for keyword search leg (default: 1.0). |
| `semantic_weight` | Single | No | Weight for semantic search leg (default: 1.0). Set to 0 to disable semantic. |

### `search_notes_semantic`

Searches notes by semantic meaning using Ollama embeddings. Finds notes conceptually related to the query even without exact keyword matches. Frontmatter fields (tags, status, type, date, extra fields) are included in the index. Requires Ollama running with the configured embedding model.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | String | Yes | Natural language query. E.g. 'notes about stress and burnout'. |
| `max_results` | Int32 | No | Maximum number of results to return (default: 10). |
| `min_score` | Single | No | Minimum similarity score 0.0–1.0 to include a result (default: 0.0 = no filter). Use 0.7 to keep only high-confidence matches. |

## ObsidianBridgeTools

### `create_note_ui`

Create a note and open it in Obsidian. Creates the file if it does not exist.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | String | Yes | Vault-relative path of the note to create and open (e.g. 'Projects/NewNote.md'). |

### `get_active_note_in_obsidian`

Returns metadata of the note currently open in Obsidian.

### `get_open_notes_in_obsidian`

Returns the list of all notes currently open in Obsidian tabs.

### `insert_at_cursor`

Insert text at the cursor position in the active Obsidian note.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `text` | String | Yes | Text to insert at the current cursor position. |

### `open_in_split`

Open a note in a new split pane in Obsidian.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to open in a split pane. |

### `open_note_in_obsidian`

Opens and focuses a specific note within the Obsidian application.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to open. |

### `replace_selection`

Replace the current text selection in the active Obsidian note.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `text` | String | Yes | Text to replace the selection with. |

### `scroll_to_block`

Scroll the active Obsidian note to a specific block ID (e.g. '^blockid').

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `block_id` | String | Yes | Block ID to scroll to (without the ^ prefix, e.g. 'abc123'). |

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

### `lint_note`

Runs the Obsidian Linter plugin on a specific note or the currently active note. Requires Obsidian to be open with the Kioku plugin and the 'obsidian-linter' plugin enabled. Linter fixes formatting issues according to the user's configured Linter rules.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | No | Vault-relative path of the note to lint. Leave empty to lint the currently active note. |

### `lint_vault`

Runs the Obsidian Linter plugin on all notes in the vault. Requires Obsidian to be open with the Kioku plugin and the 'obsidian-linter' plugin enabled. This is a long-running operation for large vaults.

### `query_dataview`

Executes a Dataview DQL query via the Obsidian plugin bridge and returns results as JSON. Requires Obsidian to be open with the Kioku plugin and Dataview plugin enabled. Supports TABLE, LIST, TASK queries and inline expressions.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | String | Yes | Dataview DQL query. Example: 'TABLE status, tags FROM "Projects" WHERE status = "active" SORT file.mtime DESC' |

## ResearchTools

### `export_citations`

Exports citation keys found in note frontmatter ('citekey' field) as a BibTeX stub list or Markdown table. Useful for building a bibliography from your literature notes. Each note with a 'citekey' in its extra frontmatter fields is included.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `format` | String | No | Export format: 'bib' for BibTeX stubs, 'markdown' for a Markdown table (default: markdown). |
| `folder` | String | No | Folder to scan (vault-relative). Leave empty to scan the entire vault. |

### `export_note`

Exports a note as HTML (rendered from Markdown using Markdig). Returns a self-contained HTML document with inline styles. Useful for sharing notes without requiring Obsidian.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to export. |
| `format` | String | No | Output format: only 'html' is supported. |

### `get_literature_gap`

Identifies citekeys referenced inline in notes (as [@citekey] or @citekey) that do not have a corresponding literature note in the vault. Helps find gaps in your literature review — citations you have referenced but not yet synthesized.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to scan for literature notes (vault-relative). Leave empty to scan the entire vault. |

### `share_as_gist`

Publishes a note as a GitHub Gist and returns the URL. Requires the KIOKU_GITHUB_TOKEN environment variable to be set with a GitHub personal access token that has the 'gist' scope. Gists are public by default; set 'public' to false for a secret Gist.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to share. |
| `description` | String | No | Gist description shown on GitHub. |
| `public` | Boolean | No | Whether the Gist should be public (default: true). |

### `validate_research_notes`

Validates that research and literature notes have required metadata fields (citekey, year, authors, status, updated). Returns a report of notes with missing fields for quality assurance.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to scan for research notes (vault-relative). Leave empty for entire vault. |

## RestoreTools

### `list_deleted_notes`

Lists notes in the trash folder that can be restored. Shows file paths and how long ago they were deleted.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `trash_folder` | String | No | Trash folder (vault-relative). Default: '.trash'. |

### `restore_note_from_trash`

Restores a deleted note from the trash folder back to the vault. Moves the file from .trash to the vault root or a specified destination folder.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note in the trash to restore. |
| `destination` | String | No | Target folder to restore into (vault-relative). Defaults to vault root. |
| `dry_run` | Boolean | No | If true, only reports what would be restored without moving the file. |

### `restore_note_version`

Restores a note to a specific git revision using git restore --source. Requires the vault to be a git repository.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note. |
| `revision` | String | Yes | Git revision (commit hash, branch name, or ref like HEAD~2). |

### `revert_all_uncommitted`

Reverts all uncommitted changes across the entire vault using git restore. Discards staged and unstaged changes to tracked files. Untracked files are not affected.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dry_run` | Boolean | No | If true, lists files that would be reverted without modifying them. |

### `revert_note`

Reverts a note to its last committed version using git restore. Discards all uncommitted changes. Requires the vault to be a git repository.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to revert. |

## SessionContextTools

### `end_work_session`

Closes the current work session by appending a summary of notes modified since the session started. Updates the session note status to 'done'.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `session_note` | String | No | Name or path of the session note to close. If empty, finds the most recent active session. |
| `summary` | String | No | Optional summary or outcome of the session. |

### `get_recent_activity`

Returns the N most recently modified notes in the vault, ordered by last modification time. Useful for the agent to quickly understand what the user has been working on.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `n` | Int32 | No | Maximum number of notes to return. |
| `folder` | String | No | Scope to a subfolder (relative to vault root). Leave empty for the full vault. |

### `get_session_activity`

Returns all notes created or modified during a specific work session.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `session_note` | String | Yes | Name or path of the session note (e.g., '2025-06-25' or 'Sessions/2025-06-25'). |

### `get_work_context`

Returns a snapshot of the vault's current work state: notes in inbox folders, notes with status 'draft', and the most recently modified notes. Call this at the start of a session to quickly understand where to resume work.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `inbox_folder` | String | No | Folder treated as the inbox (relative to vault root). Default: 'Inbox'. |
| `max_per_section` | Int32 | No | Maximum number of recent notes to show in each section. |

### `list_work_sessions`

Lists all work session notes with their dates, status (active/done), and duration if closed.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `sessions_folder` | String | No | Folder where session notes are stored (relative to vault root). Auto-detects if empty. |

### `start_work_session`

Creates a new work session note with a timestamp header. Records the current date, time, and optional session goal. The note is saved in the sessions folder for later reference.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `session_name` | String | No | Optional name for the session (e.g. 'Thesis Chapter 3 Review'). Defaults to today's date. |
| `sessions_folder` | String | No | Folder where session notes are stored (relative to vault root). |
| `goal` | String | No | Optional goal or focus for this session. |

## TaskManagementTools

### `complete_task`

Marks a task as completed ('- [x]') at the specified line in a note. Use list_tasks first to find the note name and line number of the task.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note containing the task. |
| `line_number` | Int32 | Yes | 1-based line number of the task within the note. |

### `list_overdue_tasks`

Lists all open tasks whose due date (📅 YYYY-MM-DD in Obsidian Tasks format) is in the past. Only scans open tasks — completed tasks are never overdue.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to restrict the search (relative to vault root). Leave empty to scan the entire vault. |

### `list_tasks`

Lists all tasks (open and completed) across the vault or within a specific note. Supports filtering by completion status. Returns task text, note name, line number, due date, and inline tags.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | No | Name or path of a specific note to scan. Leave empty to scan the entire vault. |
| `status` | String | No | Filter by completion status: 'open' (default), 'done', or 'all'. |
| `folder` | String | No | Folder to restrict the search (relative to vault root). Only used when 'note' is empty. |

### `list_tasks_by_tag`

Lists all open tasks that match a given tag. Matches both frontmatter tags of the note and inline '#tag' annotations in the task text. Returns task text, note, line number, and due date.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `tag` | String | Yes | Tag to filter by (without the '#' prefix). E.g. 'project', 'urgent'. |
| `include_done` | Boolean | No | Include completed tasks too (default: false — open tasks only). |

### `reopen_task`

Reopens a completed task by changing '- [x]' back to '- [ ]'. Use list_tasks with status='done' first to find the note and line number.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note containing the task. |
| `line_number` | Int32 | Yes | 1-based line number of the task within the note. |

## UtilityTools

### `get_index_status`

Returns the current status of the in-memory index: number of notes, embeddings cached, Ollama availability, last update time, and whether the index is ready.

### `ping`

Verifies that the Kioku MCP server is active and responding.

### `rebuild_index`

Forces a full re-indexing of the entire vault. Useful if the index got out of sync or massive changes were made outside of Obsidian.

## VaultOrganizationTools

### `audit_vault`

Generates a health report of the vault: notes without tags, without dates, without content, broken wikilinks, and notes not updated in a long time.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `stale_days` | Int32 | No | Flag notes not updated in this many days (default: 90). |

### `find_broken_links`

Scans the entire vault for broken wikilinks — links that point to notes that do not exist.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | No | Folder to scope the scan (relative to vault root). Leave empty for full vault. |

### `find_duplicate_notes`

Detects notes with very similar titles or content that may be duplicates. Always operates as a dry run — reports findings without modifying the vault.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `threshold` | Single | No | Similarity threshold (0.0–1.0). Higher = only very similar notes. Default: 0.8. |
| `max_results` | Int32 | No | Maximum number of duplicate pairs to report. |

### `merge_tags`

Merges two tags into one across the entire vault. All notes containing source_tag will have it replaced with target_tag. Use dry_run=true to preview changes without modifying files.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `source_tag` | String | Yes | The tag to merge away (will be replaced). |
| `target_tag` | String | Yes | The tag to merge into (will remain). |
| `dry_run` | Boolean | No | If true, returns a preview without modifying any files. |

### `normalize_tags`

Normalizes tag formatting across all notes in the vault. Converts tags to lowercase and replaces spaces/underscores with hyphens. Use dry_run=true to preview changes without modifying files.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dry_run` | Boolean | No | If true, returns a preview of what would change without modifying any files. |

### `reclassify_note`

Move a note to the most appropriate folder based on its content. Uses the same scoring as suggest_folder.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to reclassify. |
| `dry_run` | Boolean | No | If true, returns the suggested destination without moving the file. |

### `rename_tag_globally`

Renames a tag in every note across the entire vault. Use dry_run=true to preview which notes would be affected without modifying files.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `old_tag` | String | Yes | The tag to rename (without the # prefix). |
| `new_tag` | String | Yes | The new tag name (without the # prefix). |
| `dry_run` | Boolean | No | If true, returns a preview of which notes would change without modifying any files. |

### `suggest_folder`

Suggest the most appropriate vault folder(s) for a note based on content similarity to existing notes.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to suggest a folder for. |
| `max_suggestions` | Int32 | No | Maximum number of folder suggestions to return. |

### `suggest_tags`

Suggests existing tags from the vault that are relevant to the given note's content. Uses keyword overlap between the note's text and existing tags.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to suggest tags for. |
| `max_suggestions` | Int32 | No | Maximum number of tag suggestions to return. |

## WorkflowTools

### `create_note_from_template`

Creates a new note by applying a template with variable substitution. Replaces {{ variable }} placeholders with the provided values. Built-in variables: {{date}} (today), {{time}} (now), {{title}} (note name).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `template_name` | String | Yes | Name of the template (without .md extension). Use list_templates to see available templates. |
| `target_path` | String | Yes | Path for the new note (without .md extension). Can include subfolders: 'Projects/My Note'. |
| `variables` | Dictionary`2 | No | Variables to inject into the template as key-value pairs. Example: {"title": "My Note", "status": "draft", "author": "David"}. Built-in variables (date, time, title) are auto-populated if not provided. |
| `templates_folder` | String | No | Templates folder relative to vault root. Leave empty to auto-detect. |

### `create_template`

Creates a new template file in the vault's templates folder. Use {{ variable }} syntax for placeholders that will be filled when the template is used.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | String | Yes | Template name (without .md extension). |
| `content` | String | Yes | Template content in Markdown. Use {{ variable }} for placeholders. |
| `templates_folder` | String | No | Templates folder relative to vault root. Leave empty to auto-detect (defaults to 'Templates'). |

### `extract_action_items`

Extracts all unchecked task checkboxes from a note and optionally consolidates them into a new action items note. Returns the found tasks even in dry-run mode.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to scan for action items. |
| `output_note` | String | No | If provided, creates a new note at this path containing the extracted action items. |
| `dry_run` | Boolean | No | If true, only reports found action items without creating the output note. |

### `list_templates`

Lists all available note templates in the vault's templates folder. Returns template names and the variables they accept ({{ variable }} syntax).

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `templates_folder` | String | No | Templates folder relative to vault root. Leave empty to auto-detect. |

## ZettelkastenTools

### `create_folder_readme`

Creates a folder note (named after the folder, e.g. Projects.md inside Projects/) listing all its notes. Compatible with the Obsidian Folder Notes plugin. Acts as a lightweight, non-Zettelkasten alternative to create_moc. Overwrites any existing note with the same name in that folder. Only supports folders up to level 2 depth (e.g. 'Projects' or 'Areas/Work').

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | Yes | Vault-relative folder path (max level 2, e.g. 'Projects' or 'Areas/Work'). |

### `create_literature_note`

Creates a structured literature note for a book, article, or paper using the standard Zettelkasten literature note template. The note includes fields for author, year, title, source, and a summary section.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `title` | String | Yes | Title of the work (book, article, paper, etc.). |
| `author` | String | Yes | Author(s) of the work. |
| `year` | String | Yes | Publication year (e.g. '2023'). |
| `source` | String | No | Source or URL (e.g. 'https://...', 'ISBN 978-...'). |
| `summary` | String | No | Brief summary or key insight from the work. |
| `tags` | String | No | Tags to add in frontmatter (comma-separated). 'literature' is always included. |
| `folder` | String | No | Folder to save the note in. Default: 'Literature'. |

### `create_moc`

Generates a Map of Content (MOC) note for a given vault folder. The MOC lists all notes in the folder with their tags and a brief description, organized hierarchically by subfolder. Overwrites any existing MOC for the same folder.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `folder` | String | Yes | Vault-relative folder path to generate the MOC for (e.g. 'Projects', 'Areas/Work'). |
| `output_name` | String | No | Name of the output MOC note (without extension). Default: '<folder>-MOC'. |
| `output_folder` | String | No | Folder to save the MOC note. Defaults to the same folder being mapped. |

### `create_zettel`

Creates a Zettelkasten note with a unique timestamp ID (YYYYMMDDHHMMSS) as the filename. Optionally finds semantically related notes and adds wikilinks to them. Returns the created note's path and ID.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `title` | String | Yes | Title of the note (used as the H1 heading inside the note). |
| `content` | String | Yes | Main content of the note in Markdown. |
| `tags` | String | No | Tags to add in the frontmatter (comma-separated). E.g. 'idea, philosophy'. |
| `folder` | String | No | Folder inside the vault to create the note in. Leave empty to use the configured default, or auto-detect via content similarity if no default is set. |
| `link_related` | Boolean | No | If true, automatically finds up to 5 semantically related notes and adds [[wikilinks]] to them. |
| `max_links` | Int32 | No | Maximum number of related notes to link (default 5). Only used when link_related=true. |

### `link_related_notes`

Finds notes that are semantically related to a given note and appends wikilinks to them in a 'Related' section at the end of the note. Requires Ollama to be running (semantic search). Does not add links that already exist in the note.

**Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `note` | String | Yes | Name or path of the note to find related notes for and link. |
| `max_links` | Int32 | No | Maximum number of related notes to link (default 5). |
| `min_similarity` | Double | No | Minimum similarity score (0.0–1.0). Notes below this threshold are excluded. Default: 0.65. |

---

**Total tools:** 102
