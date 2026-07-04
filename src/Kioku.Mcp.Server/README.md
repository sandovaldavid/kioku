# Kioku MCP Server

> Versión: **1.8.0-beta.8** <!-- x-release-please-version --> — [Release notes](https://github.com/sandovaldavid/kioku/releases)

Servidor MCP en C# .NET 10 que expone 111 herramientas en 18 clases para que agentes de IA lean, escriban y organicen una bóveda de Obsidian. El inventario completo con parámetros vive en [`docs/commands-reference.md`](../../docs/commands-reference.md) (auto-generado).

## Arquitectura

```
MCP Transport (stdio / HTTP-SSE)
        │
        ▼
  Program.cs — entry point dual (stdio | http)
        │
        ├── Tools/        ← 17 [McpServerTool] classes
        ├── Services/     ← VaultIndex, Embedding, HybridSearch, etc.
        ├── Middleware/   ← ApiKeyMiddleware
        └── Domain/       ← Note, NoteMetadata, SearchResult
```

Solo `NoteQueryTools`, `NoteCommandTools` y `UtilityTools` se registran siempre. Las 14 clases restantes se activan por **grupos de capacidades** definidos en `{vault}/.kioku/config.yml` (por defecto todos habilitados) — ver [`docs/vault-config.md`](../../docs/vault-config.md).

## Tool Classes

| Class | Grupo | Tools |
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
| `GraphAnalysisTools` | `graph-analysis` | `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density` |
| `ResearchTools` | `research` | `export_citations`, `export_note`, `get_literature_gap`, `share_as_gist`, `validate_research_notes` |
| `PluginIntegrationTools` | `plugin` | `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins` |
| `GitTools` | `git` | `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`, `commit_staged`, `fix_merge_conflicts`, `resolve_merge_conflict` |
| `RestoreTools` | `restore` | `revert_note`, `list_deleted_notes`, `restore_note_from_trash`, `restore_note_version`, `revert_all_uncommitted` |
| `AssetTools` | `assets` | `reorder_notes_in_folder`, `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`, `normalize_attachment_names`, `move_attachments_to_folder` |
| `ObsidianBridgeTools` | `bridge` | `open_note_in_obsidian`, `get_active_note_in_obsidian`, `get_open_notes_in_obsidian`, `trigger_obsidian_command`, `insert_at_cursor`, `replace_selection`, `create_note_ui`, `scroll_to_block`, `open_in_split`, `get_selection_in_obsidian`, `toggle_reading_mode`, `fold_all_headings`, `unfold_all_headings`, `get_obsidian_status` |
| `GenerationTools` | `generation` | `summarize_note` |

## Services

| Service | Descripción |
|---|---|
| `VaultIndexService` | FileSystemWatcher con debounce (500ms). Índice invertido en memoria para búsqueda full-text. Excluye `.obsidian/`, `.trash/`, `.agents/`. |
| `EmbeddingService` | Cliente HTTP hacia Ollama (`nomic-embed-text`, 768-dim). Degradación grácil si Ollama no está disponible. |
| `EmbeddingPersistence` | Caché binaria en `{vault}/.kioku/embeddings.bin` (~15MB para 5000 notas). Carga en <100ms. |
| `HybridSearchService` | Combina keyword + semántico con Reciprocal Rank Fusion (k=60). |
| `TaskService` | Escanea checkboxes nativos de Obsidian (`[ ]`, `[x]`) con soporte para fechas de vencimiento. |
| `ObsidianBridgeService` | Cliente WebSocket hacia el plugin de Obsidian (`ws://localhost:{port}`). Reconexión automática. |
| `VaultConfigService` | Carga `{vault}/.kioku/config.yml`: carpetas, dominios, defaults, exclusiones, herencia de tags y gating de grupos de capacidades. |
| `MetricsService` | Contadores en memoria de llamadas a tools (opt-in vía `KIOKU_ENABLE_METRICS`). |

## Configuración

| Variable | Requerida | Default | Descripción |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | ✅ | — | Ruta absoluta a la bóveda de Obsidian |
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` o `http` |
| `KIOKU_HTTP_PORT` | no | `5173` | Puerto HTTP-SSE |
| `KIOKU_API_KEY` | no | — | Bearer token para auth HTTP |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | URL de Ollama |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Modelo de embeddings |
| `KIOKU_GEN_MODEL` | no | — (deshabilitado) | Modelo de Ollama para generación local (`summarize_note`), ej. `llama3.2` |
| `KIOKU_OBSIDIAN_PORT` | no | `7765` | Puerto WebSocket del plugin |
| `KIOKU_BRIDGE_TOKEN` | no | — | Token compartido del bridge WebSocket; debe coincidir con el setting "Auth token" del plugin |
| `KIOKU_MAX_RESULTS` | no | `20` | Máximo de resultados |
| `KIOKU_GITHUB_TOKEN` | no | — | Token de GitHub para `share_as_gist` |
| `KIOKU_ENABLE_METRICS` | no | `false` | Contadores de uso de tools (opt-in) |
| `KIOKU_SENTRY_DSN` | no | — | DSN de Sentry para reporte de crashes (opt-in) |

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

- `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` — SDK MCP oficial (stdio y HTTP-SSE)
- `Markdig` — render Markdown → HTML para `export_note`
- `YamlDotNet` — solo para `{vault}/.kioku/config.yml`
- `Sentry.AspNetCore` — reporte de crashes opt-in (`KIOKU_SENTRY_DSN`)
- El frontmatter se parsea con un parser manual `Span<char>` sin reflexión (`FrontmatterParser`); la similitud coseno usa SIMD (`Vector<float>`)
