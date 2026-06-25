# Kioku MCP Server

> Versión: **1.6.2** — [Release notes](https://github.com/sandovaldavid/kioku/releases)

Servidor MCP en C# .NET 10 que expone ~85 herramientas para que agentes de IA lean, escriban y organicen una bóveda de Obsidian.

## Arquitectura

```
MCP Transport (stdio / HTTP-SSE)
        │
        ▼
  Program.cs — entry point dual (stdio | http)
        │
        ├── Tools/        ← 16 [McpServerTool] classes
        ├── Services/     ← VaultIndex, Embedding, HybridSearch, etc.
        ├── Middleware/   ← ApiKeyMiddleware
        └── Domain/       ← Note, NoteMetadata, SearchResult
```

## Tool Classes

| Class | Tools |
|---|---|
| `NoteQueryTools` | `read_note`, `search_notes`, `search_notes_semantic`, `search_notes_hybrid`, `filter_notes`, `get_backlinks`, `get_outgoing_links`, `find_similar_notes`, `get_note_metadata`, `list_notes` |
| `NoteCommandTools` | `create_note`, `update_note_content`, `prepend_to_note`, `append_to_note`, `update_frontmatter`, `add_tag`, `remove_tag`, `move_note`, `rename_note` |
| `ObsidianBridgeTools` | `open_note_in_obsidian`, `get_active_note_in_obsidian`, `get_open_notes_in_obsidian`, `trigger_obsidian_command` |
| `TaskManagementTools` | `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`, `list_overdue_tasks`, `extract_action_items` |
| `ZettelkastenTools` | `create_zettel`, `create_moc`, `create_literature_note`, `link_related_notes`, `create_folder_readme` |
| `VaultOrganizationTools` | `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`, `suggest_folder`, `reclassify_note`, `find_duplicate_notes`, `audit_vault`, `reorder_notes_in_folder`, `find_broken_links` |
| `SessionContextTools` | `start_work_session`, `end_work_session`, `get_recent_activity`, `get_work_context` |
| `WorkflowTools` | `create_note_from_template`, `list_templates`, `create_template`, `process_inbox`, `sunday_hygiene` |
| `CssThemingTools` | `apply_css_snippet`, `list_css_snippets`, `remove_css_snippet` |
| `KnowledgeGraphTools` | `get_concept_map`, `get_knowledge_timeline`, `get_vault_snapshot` |
| `ResearchTools` | `export_citations`, `get_literature_gap`, `validate_research_notes` |
| `PluginIntegrationTools` | `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins`, `fix_merge_conflicts`, `resolve_merge_conflict` |
| `GraphAnalysisTools` | `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density` |
| `GitTools` | `get_git_status`, `list_git_commits`, `create_git_commit` |
| `AssetTools` | `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`, `normalize_attachment_names`, `move_attachments_to_folder` |
| `UtilityTools` | `ping`, `get_vault_stats`, `get_index_status`, `rebuild_index` |

## Services

| Service | Descripción |
|---|---|
| `VaultIndexService` | FileSystemWatcher con debounce (500ms). Índice invertido en memoria para búsqueda full-text. Excluye `.obsidian/`, `.trash/`, `.agents/`. |
| `EmbeddingService` | Cliente HTTP hacia Ollama (`nomic-embed-text`, 768-dim). Degradación grácil si Ollama no está disponible. |
| `EmbeddingPersistence` | Caché binaria en `{vault}/.kioku/embeddings.bin` (~15MB para 5000 notas). Carga en <100ms. |
| `HybridSearchService` | Combina keyword + semántico con Reciprocal Rank Fusion (k=60). |
| `TaskService` | Escanea checkboxes nativos de Obsidian (`[ ]`, `[x]`) con soporte para fechas de vencimiento. |
| `ObsidianBridgeService` | Cliente WebSocket hacia el plugin de Obsidian (`ws://localhost:{port}`). Reconexión automática. |

## Configuración

| Variable | Requerida | Default | Descripción |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | ✅ | — | Ruta absoluta a la bóveda de Obsidian |
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` o `http` |
| `KIOKU_HTTP_PORT` | no | `5173` | Puerto HTTP-SSE |
| `KIOKU_API_KEY` | no | — | Bearer token para auth HTTP |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | URL de Ollama |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Modelo de embeddings |
| `KIOKU_OBSIDIAN_PORT` | no | `7765` | Puerto WebSocket del plugin |
| `KIOKU_MAX_RESULTS` | no | `20` | Máximo de resultados |

## Desarrollo

```bash
# Build
dotnet build src/Kioku.Mcp.Server/

# Publicar como self-contained
dotnet publish src/Kioku.Mcp.Server/ -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# Ejecutar en modo stdio (default)
KIOKU_VAULT_PATH=/path/to/vault dotnet run --project src/Kioku.Mcp.Server/

# Ejecutar en modo HTTP-SSE
KIOKU_VAULT_PATH=/path/to/vault dotnet run --project src/Kioku.Mcp.Server/ -- --http
```

## Logging

Los logs se escriben a **stderr** (stdout está reservado para el protocolo MCP). Usar extensiones `ILogger<T>`:

```csharp
_logger.Info("Indexing vault at {Path}", vaultPath);
_logger.Warn("Ollama unavailable: {Message}", ex.Message);
_logger.Error(ex, "Failed to process note");
```

## Dependencias

- `ModelContextProtocol` — SDK MCP oficial (stdio y HTTP-SSE)
- `System.Numerics.Tensors` — Cosine similarity para embeddings
- Sin dependencias de reflexión (parser YAML manual con `Span<char>`)
