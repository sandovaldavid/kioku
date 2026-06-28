# Installation Guide

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
```bash
# Install globally
dotnet tool install -g kioku-mcp-server

# Set vault path
export KIOKU_VAULT_PATH=/path/to/your/vault

# Run
kioku-mcp-server
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

### 2. Install the Plugin

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

### 3. Configure Your MCP Client

Add to your MCP client configuration:

#### Claude Code / Cursor
```json
{
  "mcpServers": {
    "kioku": {
      "type": "stdio",
      "command": "kioku-mcp-server",
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
| `KIOKU_HTTP_PORT` | No | 5173 | HTTP server port |
| `KIOKU_API_KEY` | No | — | Bearer token for HTTP authentication |
| `KIOKU_OLLAMA_URL` | No | http://localhost:11434 | Ollama server URL |
| `KIOKU_EMBEDDING_MODEL` | No | nomic-embed-text | Embedding model name |
| `KIOKU_OBSIDIAN_PORT` | No | 7765 | WebSocket bridge port |
| `KIOKU_ENABLE_METRICS` | No | false | Opt-in anonymous tool-call counters |
| `KIOKU_SENTRY_DSN` | No | — | Opt-in Sentry crash reporting DSN |

### Vault Configuration

Create `.kioku/config.yml` in your vault for advanced settings:

```yaml
# Folder-specific settings
folders:
  Projects:
    domain: projects
    tags:
      - project
  Research:
    domain: research
    tags:
      - research
  
# Template variables
templates:
  default:
    tags:
      - "{{domain}}"
    status: draft
```

See [Vault Configuration Guide](vault-config.md) for details.

## Verification

### Check Server Status
```bash
# stdio transport
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | kioku-mcp-server

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

## Next Steps

- Read the [Architecture Guide](architecture.md) to understand how Kioku works
- Explore [Available Tools](commands-reference.md) to see what you can do
- Check [Troubleshooting](troubleshooting.md) if you encounter issues
