# Kioku — MCP Server for Obsidian

> **Kioku** (記憶) means "memory" in Japanese.
>
> Current version: **2.1.1** <!-- x-release-please-version --> · [Documentation Website](https://sandovaldavid.github.io/kioku/) · [View releases](https://github.com/sandovaldavid/kioku/releases)

Kioku is an MCP (Model Context Protocol) server that lets AI agents like **Claude Code** and **Antigravity CLI** read, search, write, and organize your Obsidian vault natively, fast, and privately — with 117 MCP tools across 18 classes and 22 plugin bridge commands.

---

## Capabilities

- **Hybrid search** — full-text + semantic (Ollama embeddings) + RRF
- **Read/write** notes, frontmatter, and metadata
- **Tag management** and taxonomic organization
- **Wikilink navigation** — backlinks, outgoing links, knowledge graph
- **Task management** — native checkboxes with filters by tag, date, due date
- **Zettelkasten** — atomic note creation, MOCs, templates, literature notes
- **CSS Theming** — snippets and full themes from the agent
- **Assets** — Excalidraw, images, orphaned files
- **Obsidian bridge** — open notes, run commands, query status (optional)
- **On-demand startup** — consumes no resources when not in use
- **Dual transport** — stdio (v1, local) and HTTP-SSE (v2, multiple agents/VM)

## Architecture

```
AI Agent (Claude Code / agy)
    │
    ├── stdio (v1 — local, on-demand)
    └── HTTP-SSE (v2 — VM, multiple agents)
    │
    ▼
Kioku.Mcp.Server (C# .NET 10)
    ├── 18 Tool Classes (117 MCP tools)
    ├── Services: VaultIndex · Embedding(Ollama) · HybridSearch
    │            TaskService · ObsidianBridge · Persistence
    └── Middleware: ApiKeyMiddleware
    │
    │ WebSocket (optional, only if Obsidian is open)
    ▼
Obsidian Plugin (TypeScript) — WebSocket Server :7765
    │
    ▼
Obsidian App
```

## Quick Start (Local Use)

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Obsidian](https://obsidian.md) installed with your vault of notes
- [Ollama](https://ollama.com) (optional, required for semantic search with `nomic-embed-text`)

### 1. Building the Server

For best performance, it's recommended to build Kioku as a **single self-contained executable**. This way you won't depend on running it through the dotnet SDK.

Run the command matching your operating system from the project root:

* **Linux:**
  ```bash
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
  ```
* **Windows (PowerShell):**
  ```powershell
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
  ```
* **macOS (Intel):**
  ```bash
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
  ```
* **macOS (Apple Silicon):**
  ```bash
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
  ```

This will generate the executable binary `Kioku.Mcp.Server` (or `Kioku.Mcp.Server.exe` on Windows) in the directory:
`src/Kioku.Mcp.Server/bin/Release/net10.0/<runtime>/publish/`

### 2. Registering with MCP Clients

#### Quick install (recommended)

For Claude Code, Codex CLI, OpenCode, and Antigravity CLI/IDE, skip the manual JSON/TOML editing
below and use the one-command installer. It checks for the `kioku` binary (offering to run
`dotnet tool install -g kioku-mcp-server` if it's missing) and registers the server using each
client's own mechanism:

```bash
# Claude Code — installs a plugin bundling the server + the kioku-vault skill
claude plugin marketplace add sandovaldavid/kioku && claude plugin install kioku@kioku

# Codex CLI
./scripts/add-to-client.sh codex --vault /path/to/your/vault

# OpenCode
./scripts/add-to-client.sh opencode --vault /path/to/your/vault

# Antigravity CLI/IDE
./scripts/add-to-client.sh antigravity --vault /path/to/your/vault
```

See [`integrations/README.md`](integrations/README.md) for what each of these installs, and
`./scripts/add-to-client.sh --help` for all flags (`--scope`, `--workspace`, `--dry-run`, ...).

#### Manual configuration (all clients, or as a fallback)

Build the server (step 1) and then add Kioku to your favorite MCP client:

> [!TIP]
> Use `<PATH_TO_BINARY>` as `dotnet run --project /path/to/kioku/src/Kioku.Mcp.Server/` for development, or the path to the compiled binary from step 1.

#### OpenCode

File: `~/.config/opencode/opencode.jsonc` or `./opencode.jsonc` (project)

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "kioku": {
      "type": "local",
      "command": ["<PATH_TO_BINARY>"],
      "environment": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      },
      "enabled": true
    }
  }
}
```

#### Claude Code

File: `.mcp.json` (project root) or `~/.claude.json` (global)

```json
{
  "mcpServers": {
    "kioku": {
      "type": "stdio",
      "command": "<PATH_TO_BINARY>",
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### Claude Desktop

File:
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
- **Linux:** `~/.config/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### VS Code

File: `.vscode/mcp.json` (workspace)

```json
{
  "servers": {
    "kioku": {
      "type": "stdio",
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### Cursor

File: `.cursor/mcp.json` (project root)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### Zed

File: `.zed/settings.json` (project) or `~/.config/zed/settings.json` (global)

```json
{
  "context_servers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### JetBrains IDEs (IntelliJ, PyCharm, WebStorm, etc.)

File:
- **macOS:** `~/Library/Application Support/JetBrains/AIAssistant/mcp.json`
- **Windows:** `%APPDATA%\JetBrains\AIAssistant\mcp.json`
- **Linux:** `~/.config/JetBrains/AIAssistant/mcp.json`

Or from Settings → Tools → AI Assistant → MCP.

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### Warp

Graphical configuration from Warp Settings → MCP Servers. Alternatively, in the local agent's configuration file:

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### GitHub Copilot CLI

File: `.mcp.json` (project root) or `.vscode/mcp.json`.

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### Codex CLI (OpenAI)

File: `config.toml` (project root or `~/.codex/`)

```toml
[mcp_servers.kioku]
command = "<PATH_TO_BINARY>"
args = []
env = { KIOKU_VAULT_PATH = "/path/to/your/vault" }
```

#### Antigravity CLI and IDE

File: `.antigravity/mcp.json` (project root)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<PATH_TO_BINARY>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

### 3. Installing the Obsidian Plugin (Optional)

> [!NOTE]
> The Obsidian plugin **is only needed if you want to use the UI Bridge tools** (such as automatically opening notes in the editor, seeing which note is active, or running Obsidian commands). All other read, write, and semantic search features work directly against the files, even with Obsidian closed.

To install the plugin locally in your Obsidian vault:

1. **Install dependencies and build the plugin:**
   From the project root, run:
   ```bash
   pnpm install
   pnpm build:plugin
   ```
   This will generate the `main.js`, `manifest.json`, and `styles.css` files in the `src/obsidian-kioku-mcp/` folder.

2. **Copy the files to your vault:**
   Create a folder named `kioku` inside your Obsidian vault's hidden plugins folder (`.obsidian/plugins/`):
   ```bash
   # Create the plugin directory
   mkdir -p /path/to/your/vault/.obsidian/plugins/kioku
   
   # Copy the built files
   cp src/obsidian-kioku-mcp/{main.js,manifest.json,styles.css} /path/to/your/vault/.obsidian/plugins/kioku/
   ```

3. **Enable the plugin in Obsidian:**
   * Open Obsidian.
   * Go to **Settings** -> **Community plugins**.
   * Click **Reload** (Reload icon) to detect the new plugin.
   * Toggle the switch next to **Kioku MCP Bridge**.

---

### Environment Variables

| Variable | Required | Description | Default |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | ✅ | Absolute path to the Obsidian vault | — |
| `KIOKU_TRANSPORT` | ❌ | MCP transport: `stdio` or `http` | `stdio` |
| `KIOKU_HTTP_PORT` | ❌ | HTTP-SSE transport port | `5173` |
| `KIOKU_API_KEY` | ❌ | Bearer token to authenticate the HTTP transport | — |
| `KIOKU_OLLAMA_URL` | ❌ | Base URL of the local Ollama client | `http://localhost:11434` |
| `KIOKU_EMBEDDING_MODEL` | ❌ | Ollama model used for embeddings | `nomic-embed-text` |
| `KIOKU_GEN_MODEL` | ❌ | Ollama model for local generation (`summarize_note`), e.g. `llama3.2` | — (disabled) |
| `KIOKU_MAX_RESULTS` | ❌ | Maximum number of search results | `20` |
| `KIOKU_OBSIDIAN_PORT` | ❌ | WebSocket port for the Obsidian bridge | `7765` |
| `KIOKU_BRIDGE_TOKEN` | ❌ | Shared token for the WebSocket bridge; must match the plugin's "Auth token" setting | — |
| `KIOKU_GITHUB_TOKEN` | ❌ | GitHub token for `share_as_gist` | — |
| `KIOKU_ENABLE_METRICS` | ❌ | In-memory tool usage counters (opt-in) | `false` |
| `KIOKU_SENTRY_DSN` | ❌ | Sentry DSN for crash reporting (opt-in) | — |

## Available MCP Tools

117 tools organized into 18 classes. For the full inventory with parameters, see [`docs/commands-reference.md`](docs/commands-reference.md).

Classes outside the core (query, write, utilities) are enabled or disabled by **capability groups** in `{vault}/.kioku/config.yml` — see [`docs/vault-config.md`](docs/vault-config.md).

| Category | Key Tools |
|---|---|
| **Query** | `read_note`, `search_notes`, `search_notes_semantic`, `search_notes_hybrid`, `filter_notes`, `get_note_metadata`, `get_backlinks`, `get_outgoing_links`, `find_similar_notes`, `list_notes` |
| **Write** | `create_note`, `update_note_content`, `prepend_to_note`, `append_to_note`, `update_frontmatter`, `add_tag`, `remove_tag`, `move_note`, `rename_note`, `delete_note` |
| **Tasks** | `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`, `list_overdue_tasks` |
| **Zettelkasten** | `create_zettel`, `create_moc`, `create_literature_note`, `link_related_notes`, `create_folder_readme` |
| **Workflows and Templates** | `create_note_from_template`, `list_templates`, `create_template`, `extract_action_items` |
| **Organization** | `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`, `suggest_folder`, `reclassify_note`, `find_duplicate_notes`, `find_broken_links`, `audit_vault` |
| **Sessions** | `start_work_session`, `end_work_session`, `get_recent_activity`, `get_work_context`, `list_work_sessions`, `get_session_activity` |
| **Knowledge graph** | `get_concept_map`, `get_knowledge_timeline`, `get_vault_snapshot` |
| **Graph analysis** | `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density` |
| **Research** | `export_citations`, `export_note`, `get_literature_gap`, `get_citation_graph`, `import_bibtex`, `export_bibtex`, `share_as_gist`, `validate_research_notes` |
| **Local generation** (requires Ollama) | `summarize_note`, `generate_flashcards` |
| **Restore** | `revert_note`, `list_deleted_notes`, `restore_note_from_trash`, `restore_note_version`, `revert_all_uncommitted` |
| **CSS Theming** | `apply_css_snippet`, `list_css_snippets`, `remove_css_snippet`, `reload_css_snippets` |
| **Assets** | `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`, `normalize_attachment_names`, `move_attachments_to_folder`, `reorder_notes_in_folder` |
| **Git** | `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`, `commit_staged`, `fix_merge_conflicts`, `resolve_merge_conflict` |
| **Plugin Bridge** | `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins` |
| **Obsidian UI** (requires plugin) | `open_note_in_obsidian`, `get_active_note_in_obsidian`, `get_open_notes_in_obsidian`, `trigger_obsidian_command`, `insert_at_cursor`, `replace_selection`, `create_note_ui`, `scroll_to_block`, `open_in_split`, `get_selection_in_obsidian`, `toggle_reading_mode`, `fold_all_headings`, `unfold_all_headings`, `get_obsidian_status` |
| **Utilities** | `ping`, `get_vault_stats`, `get_index_status`, `rebuild_index` |

## MCP Prompts & Resources

Besides the 116 tools, Kioku exposes the other two MCP primitives (SDK `ModelContextProtocol` 1.4.0). The full inventory lives alongside the tools inventory in [`docs/commands-reference.md`](docs/commands-reference.md).

**Prompts** — curated workflows that appear as native slash commands in any MCP client (Claude Code, Cursor, VS Code):

| Prompt | Arguments | Description |
|---|---|---|
| `research_digest` | `folder?` | Summarizes recent reading/research activity and lists open questions |
| `process_inbox` | `inbox?` | Guides the propose → confirm → apply flow of `process_inbox` |
| `weekly_review` | — | Weekly review: digest + overdue tasks + orphans + link suggestions |
| `literature_review` | `topic` | Gathers existing evidence on a topic and synthesizes it with `[[wikilink]]` citations |

**Resources** — allow mounting vault content as context without spending a tool call:

| Resource | Type | Description |
|---|---|---|
| `kioku://note/{path}` | Template | Full content (with frontmatter) of a note by its path relative to the vault |
| `kioku://vault/stats` | Direct | Snapshot of vault statistics (notes, tags, folders, index status) |

`resources/list` returns only the ~20 most recent notes (not all 5000+ in the vault) — use the `kioku://note/{path}` resource template to read any note by its path.

## Integrated Obsidian Plugins (via Plugin Bridge)

| Plugin | Commands |
|---|---|
| **Dataview** | `query_dataview` — runs DQL queries over the vault |
| **Templater** | `apply_template` — applies templates with variables |
| **Linter** | `lint_note`, `lint_vault` — formats and fixes notes |

## Project Status

- **v1** (stdio): ✅ Complete — core tools + 22 plugin bridge commands
- **v2** (HTTP-SSE): ✅ Complete — dual transport, Ollama embeddings, Bearer Token auth, VM deployment
- **v3** (Ecosystem Tools): ✅ Complete — 102 tools across 17 classes: templates, tasks, Zettelkasten, CSS theming, assets, Git, restore, graph

## License

MIT — see [LICENSE](LICENSE)
