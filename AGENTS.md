# Kioku — Agent Reference

## What is Kioku

Kioku is an MCP (Model Context Protocol) server that gives AI agents direct access to an
Obsidian vault. It pairs with an Obsidian plugin that bridges UI actions over WebSocket.

- **Server** (C# .NET 10): reads/writes `.md` files, exposes 18 MCP tools via stdio
- **Plugin** (TypeScript 6): WebSocket server running inside Obsidian; receives commands from the server

## Architecture

```
[AI agent / Claude Code]
        |
      stdio (MCP protocol)
        |
[Kioku MCP Server]  ──── reads/writes ────  [Obsidian Vault (.md files)]
        |
   WebSocket :7765
        |
[Obsidian Plugin]  (KiokuPlugin in Obsidian)
```

## Environment variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KIOKU_VAULT_PATH` | yes | — | Absolute path to the root of the Obsidian vault |
| `KIOKU_MAX_RESULTS` | no | 20 | Maximum number of search results |
| `KIOKU_OBSIDIAN_PORT` | no | 7765 | WebSocket port of the Obsidian plugin |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | Ollama base URL for embeddings |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Ollama embedding model name |

## MCP Tools

### Read-only — NoteQueryTools

| Tool | Parameters | Description |
|------|-----------|-------------|
| `read_note` | `note` | Full content of a note by name or path |
| `list_notes` | `folder?` | List notes, optionally scoped to a subfolder |
| `search_notes` | `query`, `max_results?` | Full-text search with relevance score and snippet |
| `search_notes_semantic` | `query`, `max_results?` | Semantic search via Ollama embeddings — finds related notes by meaning |
| `filter_notes` | `tag?`, `status?`, `type?`, `date_from?`, `date_to?` | Filter by frontmatter metadata (AND) |
| `get_note_metadata` | `note` | Frontmatter only — more efficient than read_note |
| `get_backlinks` | `note_name` | Notes that link to this note via `[[wikilinks]]` |
| `get_outgoing_links` | `note` | Wikilinks referenced by a note |
| `get_vault_stats` | — | Count, tags, folders, index status |

### Write — NoteCommandTools

| Tool | Parameters | Description |
|------|-----------|-------------|
| `create_note` | `name`, `content`, `tags?`, `type?`, `status?` | Create a new note with frontmatter |
| `update_note_content` | `note`, `content` | Replace body, keep frontmatter |
| `prepend_to_note` | `note`, `content` | Insert text after frontmatter |
| `append_to_note` | `note`, `content`, `add_separator?` | Add text to the end |
| `update_frontmatter` | `note`, `tags?`, `status?`, `type?` | Update YAML frontmatter fields |
| `add_tag` | `note`, `tags` | Add tags (comma-separated) |
| `remove_tag` | `note`, `tags` | Remove tags (comma-separated) |
| `move_note` | `note`, `destination_folder` | Move to another folder in the vault |
| `rename_note` | `note`, `new_name` | Rename (can include subfolder) |

### Obsidian UI Bridge — ObsidianBridgeTools

Requires Obsidian to be open with the Kioku plugin enabled.

| Tool | Parameters | Description |
|------|-----------|-------------|
| `open_note_in_obsidian` | `note` | Open and focus a note in Obsidian |
| `get_active_note_in_obsidian` | — | Metadata of the currently focused note |
| `get_open_notes_in_obsidian` | — | All notes in open Obsidian tabs |
| `trigger_obsidian_command` | `command_id` | Run any Obsidian command by ID |

### Utility — UtilityTools

| Tool | Parameters | Description |
|------|-----------|-------------|
| `ping` | — | Server health and index status |
| `get_index_status` | — | Index counts and last-indexed time |
| `rebuild_index` | — | Force full re-index of the vault |

## Tool response format

All tools return plain text strings. Status prefixes:

| Prefix | Meaning |
|--------|---------|
| `[ok]` | Operation succeeded |
| `[error]` | Operation failed |
| `[loading]` | Index not ready yet — retry |
| `[info]` | Informational, no action needed |
| `[online]` | Server health check response |

## Adding a new MCP tool

1. Add a method to the appropriate `Tools/` class (or create a new `sealed class`)
2. Annotate with `[McpServerTool]` and `[Description("...")]`
3. Register new tool type in `Program.cs` with `.WithTools<YourNewTools>()`
4. Return strings using the prefixes above — no emojis

## Logging

**TypeScript plugin:**
```typescript
import { log } from "./logger";
log.info("message");
log.warn("message");
log.error("message");
log.debug("message");
```

**C# server:**
```csharp
using Kioku.Mcp.Server.Logging;

// Inject ILogger<T> via constructor
_logger.Info("Starting: {Path}", vaultPath);
_logger.Warn("Could not connect: {Message}", ex.Message);
_logger.Error(ex, "Unexpected failure");
_logger.Debug("Re-indexed: {File}", fileName);
```

C# logs go to **stderr** only — stdout is reserved for the MCP protocol.

## File structure

```
/
  CLAUDE.md                      Claude Code session context
  AGENTS.md                      This file — agent reference
  package.json                   pnpm workspace root
  pnpm-workspace.yaml            Workspace packages
  commitlint.config.js           Commit scope enforcement
  .editorconfig                  Cross-project style rules
  .husky/                        Git hooks (commit-msg, pre-commit)
  release-please-config.json     Stable release config (main branch)
  release-please-config.beta.json   Beta release config (develop branch)
  .github/workflows/
    ci.yml                       CI: build + lint + type-check
    release-please.yml           CD: automated releases + binary artifacts
  src/
    Kioku.Mcp.Server/            C# MCP server
      Program.cs                 Entry point, DI setup
      KiokuConfiguration.cs      Environment variable loading
      Logging/KiokuLogger.cs     ILogger<T> extension methods
      Domain/                    Note, NoteMetadata, SearchResult
      Services/                  VaultIndexService, EmbeddingService, EmbeddingPersistence,
                               ObsidianBridgeService
      Tools/                     MCP tool classes
    obsidian-kioku-mcp/          Obsidian plugin (TypeScript)
      src/main.ts                KiokuPlugin — WebSocket bridge
      src/logger.ts              Logger class
      manifest.json              Obsidian plugin manifest
      esbuild.config.mjs         Build config (bundles to main.js)
```

## Semantic search (Ollama)

`search_notes_semantic` uses `EmbeddingService` to embed queries and notes with `nomic-embed-text`
(768-dim vectors, ~500MB VRAM). Requires Ollama running locally.

```bash
ollama pull nomic-embed-text   # one-time setup
```

**Cache file:** `{vault}/.kioku/embeddings.bin` — binary format, ~15MB for 5000 notes.
Loaded on startup. Updated incrementally as notes change via `FileSystemWatcher`.

**Graceful degradation:** if Ollama is unreachable at startup, `EmbeddingService.IsAvailable = false`
and `search_notes_semantic` returns an `[info]` message. All other tools remain fully functional.

**Cosine similarity** is used to rank results. Scores are returned as `NN%` in the tool output.

## Versioning

- `main` → stable (`v1.0.0`, `v1.1.0`) via Release Please
- `develop` → beta (`v1.0.0-beta.0`, `v1.0.0-beta.1`) via Release Please
- Version is synced across `.csproj PackageVersion`, `manifest.json`, and `package.json`

## Development workflow

```bash
# Start a new feature
git checkout -b feat/my-feature origin/develop

# Build and check
dotnet build src/Kioku.Mcp.Server/
pnpm --filter obsidian-kioku-mcp exec tsc --noEmit
pnpm lint:plugin

# Commit (scope required)
git commit -m "feat(server): add new tool"

# Open PR targeting develop
gh pr create --base develop
```
