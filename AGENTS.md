# Kioku - Agent Reference

## What is Kioku

Kioku is an MCP (Model Context Protocol) server that gives AI agents direct access to an Obsidian
vault. It pairs with an Obsidian plugin that provides optional UI actions over WebSocket. The
plugin lives in its own repository, [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian),
and is versioned and released independently of the server.

- **Server** (C# .NET 10): reads and writes `.md` files and exposes 49 MCP tools across 16 classes
- **Plugin** (TypeScript 6, separate repository): WebSocket server running inside Obsidian

## Architecture

```
[AI agent]
    |
  stdio or Streamable HTTP
    |
[Kioku MCP Server] ---- reads/writes ---- [Obsidian Vault]
    |
 WebSocket :7765 (optional)
    |
[Obsidian Plugin]
```

## Environment variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KIOKU_VAULT_PATH` | yes | - | Absolute path to the root of the Obsidian vault |
| `KIOKU_MAX_RESULTS` | no | 20 | Maximum number of search results |
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` or Streamable HTTP (`http`) |
| `KIOKU_HTTP_HOST` | no | `127.0.0.1` | HTTP listener; non-loopback requires an API key |
| `KIOKU_HTTP_PORT` | no | 5173 | Streamable HTTP port |
| `KIOKU_API_KEY` | no | - | Bearer token for Streamable HTTP |
| `KIOKU_OBSIDIAN_PORT` | no | 7765 | WebSocket port of the Obsidian plugin |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | Ollama base URL |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Ollama embedding model name |
| `KIOKU_GEN_MODEL` | no | - (disabled) | Ollama model for the generation group |

## MCP Tools

The generated [`docs/commands-reference.md`](docs/commands-reference.md) is authoritative and
contains every parameter. The implemented classes are:

| Class | Group | Tools |
|------|-------|-------|
| `NoteQueryTools` | core | `find_similar_notes`, `get_links`, `list_notes`, `read_note`, `search_notes` |
| `NoteCommandTools` | core | `create_note`, `delete_note`, `edit_note`, `manage_trash`, `move_note`, `update_frontmatter` |
| `UtilityTools` | core | `get_server_status`, `rebuild_index` |
| `TaskManagementTools` | tasks | `list_tasks`, `set_task_state` |
| `VaultOrganizationTools` | organization | `audit_vault`, `find_duplicate_notes`, `manage_tags`, `process_inbox`, `suggest_folder`, `suggest_tags` |
| `SessionContextTools` | sessions | `end_work_session`, `get_work_context`, `list_work_sessions`, `start_work_session` |
| `WorkflowTools` | workflows | `manage_templates` |
| `KnowledgeGraphTools` | graph | `get_concept_map`, `get_vault_snapshot` |
| `GraphAnalysisTools` | graph | `suggest_links` |
| `ResearchTools` | research | `audit_citations`, `export_citations`, `import_bibtex` |
| `ObsidianBridgeTools` | bridge | `edit_in_obsidian`, `get_obsidian_state`, `open_note_in_obsidian`, `trigger_obsidian_command` |
| `PluginIntegrationTools` | plugin | `apply_template`, `get_installed_plugins`, `lint`, `query_dataview` |
| `AssetTools` | assets | `find_orphan_assets`, `tidy_attachments` |
| `GenerationTools` | generation | `generate_flashcards`, `summarize_note` |
| `CssThemingTools` | css | `manage_css_snippets` |
| `EngineeringWorkflowTools` | engineering | `create_project_doc`, `get_project_context`, `list_projects`, `setup_agent_workflow` |

The core groups are always registered. With no vault configuration, `research`, `generation`,
`css`, `assets`, `bridge`, and `plugin` are disabled. `git`, `restore`, and `zettelkasten` are
removed groups, not valid current capability groups. See [`docs/vault-config.md`](docs/vault-config.md).

## Common workflows

- **Search**: use `search_notes` with `mode='keyword'`, `'semantic'`, or `'hybrid'`; use hybrid by default.
- **Read metadata**: use `read_note` with `metadata_only=true`.
- **Links**: use `get_links` with `direction='in'`, `'out'`, or `'both'`.
- **Edits**: use `edit_note` with `mode='replace'`, `'append'`, or `'prepend'`.
- **Tasks**: use `list_tasks`, then `set_task_state` with the returned line number.
- **Structured notes**: use `create_note` with `kind='zettel'`, `'literature'`, `'moc'`, or `'folder-readme'`.
- **Bulk organization**: preview `process_inbox`, `manage_tags`, or `suggest_links` before applying.
- **Recovery**: use `manage_trash` for Kioku soft-deleted notes. Use native Git for repository history and bulk recovery.

## Tool response format

All tools return plain text strings. Status prefixes:

| Prefix | Meaning |
|--------|---------|
| `[ok]` | Operation succeeded |
| `[error]` | Operation failed |
| `[loading]` | Index not ready yet - retry |
| `[info]` | Informational, no action needed |
| `[online]` | Server health check response |

## Adding a new MCP tool

1. Add a method to the appropriate `Tools/` class (or create a new `sealed class`)
2. Annotate it with `[McpServerTool]` and `[Description("...")]`
3. Register the class in `Program.cs` with `.WithTools<YourNewTools>()`, including capability gating when appropriate
4. Return strings using the prefixes above; do not use emojis
5. Regenerate `docs/commands-reference.md`; never edit that generated file manually

## Logging

**C# server:**
```csharp
using Kioku.Mcp.Server.Logging;

_logger.Info("Starting: {Path}", vaultPath);
_logger.Warn("Could not connect: {Message}", ex.Message);
_logger.Error(ex, "Unexpected failure");
_logger.Debug("Re-indexed: {File}", fileName);
```

C# logs go to **stderr** only; stdout is reserved for the MCP protocol.

## File structure

```
/
  README.md
  AGENTS.md
  CLAUDE.md
  docs/commands-reference.md     Generated MCP inventory
  src/Kioku.Mcp.Server/          C# MCP server
    Program.cs                   Entry point and DI setup
    Services/                    Index, embeddings, bridge, config, workflows
    Tools/                       MCP tool classes
```

## Semantic search (Ollama)

`search_notes` with `mode='semantic'` uses `EmbeddingService` with `nomic-embed-text` (768-dim
vectors). Pull the model once with `ollama pull nomic-embed-text`. If Ollama is unavailable,
keyword and hybrid search remain usable; semantic mode reports an informational result.

Embeddings are cached at `{vault}/.kioku/embeddings.bin` and updated as notes change.

## Development workflow

```bash
dotnet build src/Kioku.Mcp.Server/
```
