# Kioku MCP Server

> Version: **2.0.0-beta.10** <!-- x-release-please-version --> — [Release notes](https://github.com/sandovaldavid/kioku/releases)

MCP server in C# .NET 10 that exposes 117 tools across 18 classes for AI agents to read, write, and organize an Obsidian vault. The full inventory with parameters lives in [`docs/commands-reference.md`](../../docs/commands-reference.md) (auto-generated).

## Architecture

```
MCP Transport (stdio / HTTP-SSE)
        │
        ▼
  Program.cs — dual entry point (stdio | http)
        │
        ├── Tools/        ← 17 [McpServerTool] classes
        ├── Services/     ← VaultIndex, Embedding, HybridSearch, etc.
        ├── Middleware/   ← ApiKeyMiddleware
        └── Domain/       ← Note, NoteMetadata, SearchResult
```

Only `NoteQueryTools`, `NoteCommandTools`, and `UtilityTools` are always registered. The remaining 14 classes are enabled by **capability groups** defined in `{vault}/.kioku/config.yml` (all enabled by default) — see [`docs/vault-config.md`](../../docs/vault-config.md).

## Tool Classes

| Class | Group | Tools |
|---|---|---|
| `NoteQueryTools` | core | `read_note`, `list_notes`, `search_notes`, `search_notes_semantic`, `search_notes_hybrid`, `filter_notes`, `get_backlinks`, `get_outgoing_links`, `find_similar_notes`, `get_note_metadata`, `get_note_embedding`, `get_vault_stats`, `inspect_note_tags` |
| `NoteCommandTools` | core | `create_note`, `update_note_content`, `prepend_to_note`, `append_to_note`, `update_frontmatter`, `add_tag`, `remove_tag`, `move_note`, `rename_note`, `delete_note` |
| `UtilityTools` | core | `ping`, `get_index_status`, `rebuild_index` |
| `TaskManagementTools` | `tasks` | `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`, `list_overdue_tasks` |
| `ZettelkastenTools` | `zettelkasten` | `create_zettel`, `create_moc`, `create_literature_note`, `link_related_notes`, `create_folder_readme` |
| `VaultOrganizationTools` | `organization` | `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`, `suggest_folder`, `reclassify_note`, `find_duplicate_notes`, `audit_vault`, `find_broken_links`, `process_inbox` |
| `SessionContextTools` | `sessions` | `start_work_session`, `end_work_session`, `get_recent_activity`, `get_work_context`, `list_work_sessions`, `get_session_activity` |
| `WorkflowTools` | `workflows` | `create_note_from_template`, `list_templates`, `create_template`, `extract_action_items`, `generate_digest` |
| `CssThemingTools` | `css` | `apply_css_snippet`, `list_css_snippets`, `remove_css_snippet`, `reload_css_snippets` |
| `KnowledgeGraphTools` | `graph` | `get_concept_map`, `get_knowledge_timeline`, `get_vault_snapshot` |
| `GraphAnalysisTools` | `graph-analysis` | `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density`, `suggest_links`, `apply_link_suggestions` |
| `ResearchTools` | `research` | `export_citations`, `export_note`, `get_literature_gap`, `get_citation_graph`, `import_bibtex`, `export_bibtex`, `share_as_gist`, `validate_research_notes` |
| `PluginIntegrationTools` | `plugin` | `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins` |
| `GitTools` | `git` | `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`, `commit_staged`, `fix_merge_conflicts`, `resolve_merge_conflict` |
| `RestoreTools` | `restore` | `revert_note`, `list_deleted_notes`, `restore_note_from_trash`, `restore_note_version`, `revert_all_uncommitted` |
| `AssetTools` | `assets` | `reorder_notes_in_folder`, `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`, `normalize_attachment_names`, `move_attachments_to_folder` |
| `ObsidianBridgeTools` | `bridge` | `open_note_in_obsidian`, `get_active_note_in_obsidian`, `get_open_notes_in_obsidian`, `trigger_obsidian_command`, `insert_at_cursor`, `replace_selection`, `create_note_ui`, `scroll_to_block`, `open_in_split`, `get_selection_in_obsidian`, `toggle_reading_mode`, `fold_all_headings`, `unfold_all_headings`, `get_obsidian_status` |
| `GenerationTools` | `generation` | `summarize_note`, `generate_flashcards` |

## Services

| Service | Description |
|---|---|
| `VaultIndexService` | FileSystemWatcher with debounce (500ms). In-memory inverted index for full-text search. Excludes `.obsidian/`, `.trash/`, `.agents/`. |
| `EmbeddingService` | HTTP client to Ollama (`nomic-embed-text`, 768-dim). Graceful degradation if Ollama is unavailable. |
| `EmbeddingPersistence` | Binary cache at `{vault}/.kioku/embeddings.bin` (~15MB for 5000 notes). Loads in <100ms. |
| `HybridSearchService` | Combines keyword + semantic with Reciprocal Rank Fusion (k=60). |
| `TaskService` | Scans native Obsidian checkboxes (`[ ]`, `[x]`) with support for due dates. |
| `ObsidianBridgeService` | WebSocket client to the Obsidian plugin (`ws://localhost:{port}`). Automatic reconnection. |
| `VaultConfigService` | Loads `{vault}/.kioku/config.yml`: folders, domains, defaults, exclusions, tag inheritance, and capability group gating. |
| `MetricsService` | In-memory tool call counters (opt-in via `KIOKU_ENABLE_METRICS`). |

## Configuration

| Variable | Required | Default | Description |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | ✅ | — | Absolute path to the Obsidian vault |
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` or `http` |
| `KIOKU_HTTP_PORT` | no | `5173` | HTTP-SSE port |
| `KIOKU_API_KEY` | no | — | Bearer token for HTTP auth |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | Ollama URL |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Embedding model |
| `KIOKU_GEN_MODEL` | no | — (disabled) | Ollama model for local generation (`summarize_note`), e.g. `llama3.2` |
| `KIOKU_OBSIDIAN_PORT` | no | `7765` | Plugin WebSocket port |
| `KIOKU_BRIDGE_TOKEN` | no | — | Shared token for the WebSocket bridge; must match the plugin's "Auth token" setting |
| `KIOKU_MAX_RESULTS` | no | `20` | Maximum number of results |
| `KIOKU_GITHUB_TOKEN` | no | — | GitHub token for `share_as_gist` |
| `KIOKU_ENABLE_METRICS` | no | `false` | Tool usage counters (opt-in) |
| `KIOKU_SENTRY_DSN` | no | — | Sentry DSN for crash reporting (opt-in) |

## Development

```bash
# Build
dotnet build src/Kioku.Mcp.Server/

# Publish as self-contained
dotnet publish src/Kioku.Mcp.Server/ -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# Run in stdio mode (default)
KIOKU_VAULT_PATH=/path/to/vault dotnet run --project src/Kioku.Mcp.Server/

# Run in HTTP-SSE mode
KIOKU_VAULT_PATH=/path/to/vault dotnet run --project src/Kioku.Mcp.Server/ -- --http
```

## Logging

Logs are written to **stderr** (stdout is reserved for the MCP protocol). Use the `ILogger<T>` extensions:

```csharp
_logger.Info("Indexing vault at {Path}", vaultPath);
_logger.Warn("Ollama unavailable: {Message}", ex.Message);
_logger.Error(ex, "Failed to process note");
```

## Dependencies

- `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` — official MCP SDK (stdio and HTTP-SSE)
- `Markdig` — renders Markdown → HTML for `export_note`
- `YamlDotNet` — only for `{vault}/.kioku/config.yml`
- `Sentry.AspNetCore` — opt-in crash reporting (`KIOKU_SENTRY_DSN`)
- Frontmatter is parsed with a manual `Span<char>` parser with no reflection (`FrontmatterParser`); cosine similarity uses SIMD (`Vector<float>`)
