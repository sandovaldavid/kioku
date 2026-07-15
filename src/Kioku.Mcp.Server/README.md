# Kioku MCP Server

> Version: **2.3.0** <!-- x-release-please-version --> — [Release notes](https://github.com/sandovaldavid/kioku/releases)

MCP server in C# .NET 10 that exposes **49 tools across 16 classes** for AI agents to read,
write, search, and organize an Obsidian vault. The authoritative inventory with parameters is
[`docs/commands-reference.md`](../../docs/commands-reference.md) (auto-generated).

## Architecture

```
MCP Transport (stdio / HTTP-SSE)
        |
        v
  Program.cs - dual entry point
        |
        +-- Tools/        <- 16 MCP tool classes
        +-- Services/     <- VaultIndex, Embedding, HybridSearch, and workflow services
        +-- Middleware/   <- ApiKeyMiddleware
        +-- Domain/       <- Note, NoteMetadata, SearchResult
```

The core classes `NoteQueryTools`, `NoteCommandTools`, and `UtilityTools` are always registered.
The other classes are controlled by capability groups in `{vault}/.kioku/config.yml`. With no
configuration file, `research`, `generation`, `css`, `assets`, `bridge`, and `plugin` are
disabled by default; the other optional groups are enabled. Git, restore, and zettelkasten are
not capability groups in this surface. See [`docs/vault-config.md`](../../docs/vault-config.md).

## Tool Classes

| Class | Group | Tools |
|---|---|---|
| `NoteQueryTools` | core | `find_similar_notes`, `get_links`, `list_notes`, `read_note`, `search_notes` |
| `NoteCommandTools` | core | `create_note`, `delete_note`, `edit_note`, `manage_trash`, `move_note`, `update_frontmatter` |
| `UtilityTools` | core | `get_server_status`, `rebuild_index` |
| `TaskManagementTools` | `tasks` | `list_tasks`, `set_task_state` |
| `VaultOrganizationTools` | `organization` | `audit_vault`, `find_duplicate_notes`, `manage_tags`, `process_inbox`, `suggest_folder`, `suggest_tags` |
| `SessionContextTools` | `sessions` | `end_work_session`, `get_work_context`, `list_work_sessions`, `start_work_session` |
| `WorkflowTools` | `workflows` | `manage_templates` |
| `KnowledgeGraphTools` | `graph` | `get_concept_map`, `get_vault_snapshot` |
| `GraphAnalysisTools` | `graph` | `suggest_links` |
| `ResearchTools` | `research` | `audit_citations`, `export_citations`, `import_bibtex` |
| `ObsidianBridgeTools` | `bridge` | `edit_in_obsidian`, `get_obsidian_state`, `open_note_in_obsidian`, `trigger_obsidian_command` |
| `PluginIntegrationTools` | `plugin` | `apply_template`, `get_installed_plugins`, `lint`, `query_dataview` |
| `AssetTools` | `assets` | `find_orphan_assets`, `tidy_attachments` |
| `GenerationTools` | `generation` | `generate_flashcards`, `summarize_note` |
| `CssThemingTools` | `css` | `manage_css_snippets` |
| `EngineeringWorkflowTools` | `engineering` | `create_project_doc`, `get_project_context`, `list_projects`, `setup_agent_workflow` |

## Services

| Service | Description |
|---|---|
| `VaultIndexService` | FileSystemWatcher with debounce and an in-memory index for full-text search. |
| `EmbeddingService` | HTTP client to Ollama with graceful degradation when unavailable. |
| `EmbeddingPersistence` | Binary embedding cache at `{vault}/.kioku/embeddings.bin`. |
| `HybridSearchService` | Combines keyword and semantic results with Reciprocal Rank Fusion. |
| `TaskService` | Scans native Obsidian checkboxes and due dates. |
| `ObsidianBridgeService` | WebSocket client to the optional Obsidian plugin. |
| `VaultConfigService` | Loads folder, domain, default, exclusion, tag, template, and capability settings. |
| `MetricsService` | In-memory tool call counters, opt-in via `KIOKU_ENABLE_METRICS`. |

## Configuration

| Variable | Required | Default | Description |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | yes | - | Absolute path to the Obsidian vault |
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` or `http` |
| `KIOKU_HTTP_PORT` | no | `5173` | HTTP-SSE port |
| `KIOKU_API_KEY` | no | - | Bearer token for HTTP auth |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | Ollama URL |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Embedding model |
| `KIOKU_GEN_MODEL` | no | - (disabled) | Local generation model for the generation group |
| `KIOKU_OBSIDIAN_PORT` | no | `7765` | Plugin WebSocket port |
| `KIOKU_BRIDGE_TOKEN` | no | - | Shared token for the WebSocket bridge |
| `KIOKU_MAX_RESULTS` | no | `20` | Maximum number of results |
| `KIOKU_ENABLE_METRICS` | no | `false` | Tool usage counters (opt-in) |
| `KIOKU_SENTRY_DSN` | no | - | Sentry DSN for crash reporting (opt-in) |

## Development

```bash
dotnet build src/Kioku.Mcp.Server/
dotnet publish src/Kioku.Mcp.Server/ -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
KIOKU_VAULT_PATH=/path/to/vault dotnet run --project src/Kioku.Mcp.Server/
KIOKU_VAULT_PATH=/path/to/vault dotnet run --project src/Kioku.Mcp.Server/ -- --http
```

Logs are written to **stderr** because stdout is reserved for the MCP protocol.
