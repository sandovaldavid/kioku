# Kioku — MCP Server for Obsidian

> **Kioku** (記憶) means "memory" in Japanese.
>
> Current version: **2.3.0** <!-- x-release-please-version --> · [Documentation Website](https://sandovaldavid.github.io/kioku/) · [View releases](https://github.com/sandovaldavid/kioku/releases)

Kioku is an MCP (Model Context Protocol) server that lets AI agents like **Claude Code** and **Antigravity CLI** read, search, write, and organize your Obsidian vault natively, fast, and privately — with 49 MCP tools across 16 classes.

---

## Capabilities

- **Hybrid search** — full-text + semantic (Ollama embeddings) + RRF
- **Read/write** notes, frontmatter, and metadata
- **Tag management** and taxonomic organization
- **Wikilink navigation** — backlinks, outgoing links, knowledge graph
- **Task management** — native checkboxes with filters by tag, date, due date
- **Structured notes** — atomic notes, MOCs, literature notes, and folder readmes through `create_note`
- **CSS Theming** — snippets and full themes from the agent
- **Assets** — Excalidraw, images, orphaned files
- **Obsidian bridge** — open notes, run commands, query status (optional)
- **On-demand startup** — consumes no resources when not in use
- **Dual transport** — stdio (local) and Streamable HTTP (multiple agents/VM)

## Architecture

```
AI Agent (Claude Code / agy)
    │
    ├── stdio (v1 — local, on-demand)
    └── Streamable HTTP (v2 — VM, multiple agents)
    │
    ▼
Kioku.Mcp.Server (C# .NET 10)
    ├── 16 Tool Classes (49 MCP tools)
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
| `KIOKU_HTTP_HOST` | ❌ | Streamable HTTP listener; non-loopback requires authentication | `127.0.0.1` |
| `KIOKU_HTTP_PORT` | ❌ | Streamable HTTP transport port | `5173` |
| `KIOKU_API_KEY` | ❌ | Bearer token; required for a non-loopback HTTP listener | — |
| `KIOKU_HTTP_ALLOWED_ORIGINS` | ❌ | Exact comma-separated browser Origin allowlist | loopback + Obsidian |
| `KIOKU_HTTP_TRUSTED_PROXIES` | ❌ | Exact comma-separated proxy IPs trusted for forwarded headers | — |
| `KIOKU_HTTP_MAX_REQUEST_BODY_BYTES` | ❌ | Maximum HTTP request body | `1048576` |
| `KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS` | ❌ | Timeout for MCP POST calls | `300` |
| `KIOKU_ALLOW_INSECURE_HTTP` | ❌ | Explicit unsafe non-loopback/no-auth override | `false` |
| `KIOKU_OLLAMA_URL` | ❌ | Base URL of the local Ollama client | `http://localhost:11434` |
| `KIOKU_EMBEDDING_MODEL` | ❌ | Ollama model used for embeddings | `nomic-embed-text` |
| `KIOKU_GEN_MODEL` | ❌ | Ollama model for local generation (`summarize_note`, `generate_flashcards`) | — (disabled) |
| `KIOKU_MAX_RESULTS` | ❌ | Maximum number of search results | `20` |
| `KIOKU_OBSIDIAN_PORT` | ❌ | WebSocket port for the Obsidian bridge | `7765` |
| `KIOKU_BRIDGE_TOKEN` | ❌ | Shared token for the WebSocket bridge; must match the plugin's "Auth token" setting | — |
| `KIOKU_ENABLE_METRICS` | ❌ | In-memory tool usage counters (opt-in) | `false` |
| `KIOKU_SENTRY_DSN` | ❌ | Sentry DSN for crash reporting (opt-in) | — |

Streamable HTTP binds to loopback by default, validates `Origin`, and separates public liveness
from protected readiness. See [Streamable HTTP security](docs/deploy/auth-options.md) before
exposing Kioku through a reverse proxy, VM, LAN, or container.

## Available MCP Tools

49 tools organized into 16 classes. For the full inventory with parameters, see [`docs/commands-reference.md`](docs/commands-reference.md).

Classes outside the core (query, write, utilities) are enabled or disabled by **capability groups** in `{vault}/.kioku/config.yml` — see [`docs/vault-config.md`](docs/vault-config.md).

| Category | Key Tools |
|---|---|
| **Query** | `read_note`, `list_notes`, `search_notes`, `get_links`, `find_similar_notes` |
| **Write** | `create_note`, `edit_note`, `update_frontmatter`, `move_note`, `manage_trash` |
| **Tasks** | `list_tasks`, `set_task_state` |
| **Organization** | `audit_vault`, `find_duplicate_notes`, `manage_tags`, `process_inbox`, `suggest_folder`, `suggest_tags` |
| **Sessions** | `start_work_session`, `end_work_session`, `get_work_context`, `list_work_sessions` |
| **Engineering** | `create_project_doc`, `get_project_context`, `list_projects`, `setup_agent_workflow` |
| **Templates** | `manage_templates` |
| **Knowledge graph** | `get_concept_map`, `get_vault_snapshot` |
| **Graph analysis** | `suggest_links` |
| **Research** (disabled by default) | `audit_citations`, `export_citations`, `import_bibtex` |
| **Local generation** (disabled by default; requires Ollama) | `summarize_note`, `generate_flashcards` |
| **CSS** (disabled by default) | `manage_css_snippets` |
| **Assets** (disabled by default) | `find_orphan_assets`, `tidy_attachments` |
| **Plugin bridge** (disabled by default) | `query_dataview`, `apply_template`, `lint`, `get_installed_plugins` |
| **Obsidian UI** (disabled by default; requires plugin) | `edit_in_obsidian`, `get_obsidian_state`, `open_note_in_obsidian`, `trigger_obsidian_command` |
| **Utilities** | `get_server_status`, `rebuild_index` |

## MCP Prompts & Resources

Besides the 49 tools, Kioku exposes prompts and resources. The full inventory lives alongside the tools inventory in [`docs/commands-reference.md`](docs/commands-reference.md).

**Prompts** — curated workflows that appear as native slash commands in any MCP client (Claude Code, Cursor, VS Code):

| Prompt | Arguments | Description |
|---|---|---|
| `research_digest` | `folder?` | Summarizes recent reading/research activity and lists open questions |
| `process_inbox` | `inbox?` | Guides the propose → confirm → apply flow of `process_inbox` |
| `weekly_review` | — | Weekly review: digest + overdue tasks + orphans + link suggestions |
| `literature_review` | `topic` | Gathers existing evidence on a topic and synthesizes it with `[[wikilink]]` citations |
| `resume_project` | `project` | Loads project context before work resumes |
| `project_task` | `project`, `task` | Orchestrates context, execution, documentation, verification, and handoff for a project task |
| `record_decision` | `project`, `topic` | Records an architecture decision |
| `log_bugfix` | `project` | Records a bug and its fix |
| `plan_feature` | `project`, `feature` | Drafts an implementation plan |
| `work_on_ticket` | `project`, `ticket` | Structures a ticket and creates a plan |
| `write_daily` | `project` | Drafts a project's daily note |

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
| **Linter** | `lint` — formats and fixes a note or the vault |

## Project Status

- **v1/v2**: ✅ Complete — stdio and Streamable HTTP transports, Ollama embeddings, and Bearer Token auth
- **PR2 tool surface**: ✅ Complete — 49 tools across 16 classes with capability gating

For migration from the previous tool surface, see [`docs/migration-v3.md`](docs/migration-v3.md).

## License

MIT — see [LICENSE](LICENSE)
