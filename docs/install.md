---
layout: default
title: Installation Guide
sidebar: true
---

## Quick Start

### 1. Install the Server

Choose your preferred method:

#### Docker (Recommended)
```bash
# Clone the repository
git clone https://github.com/sandovaldavid/kioku.git
cd kioku

# Set your vault path
export KIOKU_VAULT_PATH=/path/to/your/vault

# Start with Docker Compose
docker-compose up -d

# Pull embedding model (first time only)
docker exec kioku-ollama ollama pull nomic-embed-text
```

#### .NET Tool
Requires the [ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (not just the base .NET Runtime) — the server targets `Microsoft.NET.Sdk.Web` to support the HTTP-SSE transport.
```bash
# Install globally
dotnet tool install -g kioku-mcp-server

# Set vault path
export KIOKU_VAULT_PATH=/path/to/your/vault

# Run
kioku
```

#### One-line Installer (Linux/macOS)
```bash
curl -fsSL https://raw.githubusercontent.com/sandovaldavid/kioku/main/scripts/install.sh | bash
```
Set `INSTALL_DIR` to customize the destination:
```bash
curl -fsSL https://raw.githubusercontent.com/sandovaldavid/kioku/main/scripts/install.sh | INSTALL_DIR=/usr/local/bin bash
```

#### Homebrew (coming soon)
```bash
brew tap sandovaldavid/kioku
brew install kioku-mcp-server
```

#### WinGet (coming soon)
```powershell
winget install sandovaldavid.kioku
```

#### Binary Release
Download from [GitHub Releases](https://github.com/sandovaldavid/kioku/releases):

```bash
# Linux
wget https://github.com/sandovaldavid/kioku/releases/latest/download/kioku-server-linux-x64
chmod +x kioku-server-linux-x64
export KIOKU_VAULT_PATH=/path/to/your/vault
./kioku-server-linux-x64

# macOS (Intel)
wget https://github.com/sandovaldavid/kioku/releases/latest/download/kioku-server-osx-x64
chmod +x kioku-server-osx-x64
export KIOKU_VAULT_PATH=/path/to/your/vault
./kioku-server-osx-x64

# macOS (Apple Silicon)
wget https://github.com/sandovaldavid/kioku/releases/latest/download/kioku-server-osx-arm64
chmod +x kioku-server-osx-arm64
export KIOKU_VAULT_PATH=/path/to/your/vault
./kioku-server-osx-arm64

# Windows
# Download kioku-server-win-x64.exe from releases
set KIOKU_VAULT_PATH=C:\path\to\your\vault
kioku-server-win-x64.exe
```

### 2. Register with an AI Coding CLI

Once the server is installed and `kioku` is on your `PATH`, register it with your AI coding CLI
in one command:

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

`scripts/add-to-client.sh` checks for the `kioku` binary and offers to install it if it's
missing, so you can also run it as step 1 instead of the methods above. See
[`../integrations/README.md`](../integrations/README.md) for what each installer sets up, and
`./scripts/add-to-client.sh --help` for all flags. For any other MCP client, or if you'd rather
edit the config by hand, see "Configure Your MCP Client" below.

### 3. Install the Plugin

#### Via BRAT (Beta)
1. Install [BRAT](https://github.com/TfTHacker/obsidian42-brat) plugin in Obsidian
2. Open BRAT settings → Beta Plugin List
3. Add: `sandovaldavid/kioku`
4. Enable "Kioku MCP" in Community Plugins

#### From Source
```bash
cd src/obsidian-kioku-mcp
pnpm install
pnpm run build

# Copy to your vault's plugins folder
cp -r . /path/to/vault/.obsidian/plugins/kioku-mcp
```

### 4. Configure Your MCP Client (Manual)

If you already ran `scripts/add-to-client.sh` (step 2) for Claude Code, Codex, OpenCode, or
Antigravity, you can skip this step. It's here as a fallback and for other clients (Cursor, VS
Code, Zed, JetBrains, Claude Desktop, ...).

Add to your MCP client configuration:

#### Claude Code / Cursor
```json
{
  "mcpServers": {
    "kioku": {
      "type": "stdio",
      "command": "kioku",
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/your/vault"
      }
    }
  }
}
```

#### HTTP Transport (Remote)
```json
{
  "mcpServers": {
    "kioku": {
      "type": "sse",
      "url": "http://localhost:5173/mcp",
      "headers": {
        "Authorization": "Bearer your-api-key"
      }
    }
  }
}
```

## Configuration

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KIOKU_VAULT_PATH` | Yes | — | Absolute path to your Obsidian vault |
| `KIOKU_TRANSPORT` | No | stdio | MCP transport: `stdio` or `http` |
| `KIOKU_HTTP_PORT` | No | 5173 | HTTP server port |
| `KIOKU_API_KEY` | No | — | Bearer token for HTTP authentication |
| `KIOKU_OLLAMA_URL` | No | http://localhost:11434 | Ollama server URL |
| `KIOKU_EMBEDDING_MODEL` | No | nomic-embed-text | Embedding model name |
| `KIOKU_GEN_MODEL` | No | — (disabled) | Ollama model for local text generation (`summarize_note`), e.g. `llama3.2` |
| `KIOKU_MAX_RESULTS` | No | 20 | Maximum number of search results |
| `KIOKU_OBSIDIAN_PORT` | No | 7765 | WebSocket bridge port |
| `KIOKU_BRIDGE_TOKEN` | No | — | Shared secret for the WebSocket bridge. Must match the plugin's "Auth token" setting |
| `KIOKU_GITHUB_TOKEN` | No | — | GitHub token for the `share_as_gist` tool |
| `KIOKU_ENABLE_METRICS` | No | false | Opt-in anonymous tool-call counters |
| `KIOKU_SENTRY_DSN` | No | — | Opt-in Sentry crash reporting DSN |

### Vault Configuration

Create `.kioku/config.yml` in your vault for advanced settings:

```yaml
# Where each note type is created (used by create_zettel, sessions, templates, ...)
folders:
  inbox: "Inbox"
  zettel: "Zettelkasten"
  literature: "Literature"

# Frontmatter domain assigned by folder (longest prefix wins)
domains:
  "Projects": "work/projects"
  "Research": "academic/research"

# Frontmatter defaults per note type
defaults:
  zettel:
    type: concept
    status: active

# Folders excluded from the index (dot-folders are always excluded)
exclude:
  - "Archive"

# Enable/disable optional tool groups
capabilities:
  disabled: []          # e.g. [git, css] — or ["*"] to disable all optional groups
```

See the [Vault Configuration Guide](vault-config.md) for the full schema, and
[`vault-config.example.yml`](vault-config.example.yml) for a complete annotated example.

## Verification

### Check Server Status
```bash
# stdio transport
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | kioku

# HTTP transport
curl http://localhost:5173/health
```

### Check Plugin Connection
1. Open Obsidian
2. Open Developer Console (Ctrl+Shift+I)
3. Look for: `[Kioku] Bridge listening on 127.0.0.1:7765`

### Test Semantic Search
```bash
# Ensure Ollama is running
ollama list

# Pull model if needed
ollama pull nomic-embed-text
```

## Zotero / BibTeX

Kioku can import a BibTeX library as literature notes (`import_bibtex`) and reconstruct a
`.bib` file from them later (`export_bibtex`), both in `ResearchTools` (group `research`).
There's no live network integration with Zotero yet — the recommended flow is via
**Better BibTeX**, which keeps a `.bib` file on disk in sync with your Zotero library:

1. Install the [Better BibTeX](https://retorque.re/zotero-better-bibtex/) Zotero plugin.
2. In Zotero, right-click a collection → **Export Collection…** → format
   **Better BibLaTeX** (or **Better BibTeX**) → check **Keep updated** so Zotero
   auto-rewrites the `.bib` file whenever the collection changes.
3. Save the export somewhere your agent can read (inside the vault, or any local path), then
   ask your agent to run `import_bibtex` on it — e.g. "import my Zotero library from
   `~/Zotero/my-library.bib`".
4. Whenever Zotero re-exports the file, re-run `import_bibtex` with the same source: entries
   are deduplicated by `citekey`, so already-imported notes are left untouched. Pass
   `update_existing=true` if you want Zotero-side metadata edits to also refresh the note's
   frontmatter (the note body — your summary, key ideas, quotes — is never overwritten).
5. Use `dry_run=true` first if you want to preview what would be created/updated before
   writing anything.

A future integration with Zotero's local HTTP API (`localhost:23119`) is possible but adds
network coupling that the file-based Better BibTeX flow avoids — it isn't planned unless
this file-based flow proves insufficient.

## Next Steps

- Check the [Architecture Diagram]({{ site.baseurl }}/) on the homepage to understand how Kioku works
- Explore [Available Tools](commands-reference.md) to see what you can do
- Check [Troubleshooting](troubleshooting.md) if you encounter issues
