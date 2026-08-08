---
layout: default
title: Installation Guide
sidebar: true
---

# Installation Guide

Kioku installation and client integration is organized into two independent steps:

```text
Step 1: Install Kioku server
Step 2: Register Kioku with your MCP client using native mechanisms
```

> [!NOTE]
> `KIOKU_VAULT_PATH` is a required user-local configuration. Kioku has no implicit default vault (such as `~/Documents/Obsidian` or the current directory) and fails explicitly during server initialization if a valid vault path is missing or invalid.

---

## Step 1: Install Kioku server

Install the prebuilt Kioku MCP server binary or global tool.

### Option A: Install via .NET Global Tool (Recommended)

```bash
dotnet tool install --global kioku-mcp-server
```

To update later:

```bash
dotnet tool update --global kioku-mcp-server
```

### Option B: Install via One-Line Install Script

```bash
curl -fsSL https://raw.githubusercontent.com/sandovaldavid/kioku/main/scripts/install.sh | bash
```

This installs the standalone `kioku` binary for your platform to `~/.local/bin/kioku`. Ensure `~/.local/bin` is in your `PATH`.

### Option C: Build from Source

```bash
git clone https://github.com/sandovaldavid/kioku.git
cd kioku
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
```

---

## Step 2: Register with your MCP client

Register `kioku` with your AI coding client using the native registration mechanism for that client.

### Paths containing spaces
When your vault directory path contains spaces, wrap the path in quotes in shell commands or JSON configurations:
- Shell: `export KIOKU_VAULT_PATH="/Users/yourname/My Obsidian Vault"`
- JSON: `"KIOKU_VAULT_PATH": "/Users/yourname/My Obsidian Vault"`

---

### Claude Code

Claude Code supports native CLI MCP registration as well as native plugin installation.

#### Native CLI Registration

To add Kioku to your Claude Code configuration:

```bash
# Global user configuration
claude mcp add kioku -e KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku

# Project scope
claude mcp add kioku --scope project -e KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### Native Plugin Installation

Claude Code users can install the Kioku plugin from the marketplace:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

During installation, Claude Code prompts for `userConfig.vault_path` (and optional Ollama settings).

---

### Codex CLI

Codex supports native MCP server registration via `codex mcp add` or directly in `~/.codex/config.json`:

#### CLI Command

```bash
codex mcp add kioku -e KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### Native Configuration (`~/.codex/config.json`)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "kioku",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/absolute/path/to/your/vault"
      }
    }
  }
}
```

---

### OpenCode

OpenCode configures MCP servers via `opencode.json` at the root of your workspace or globally. `opencode.json` supports environment variable interpolation using `{env:VARIABLE_NAME}`.

#### Project Configuration (`opencode.json`)

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "kioku": {
      "type": "local",
      "command": ["kioku"],
      "enabled": true,
      "environment": {
        "KIOKU_VAULT_PATH": "{env:KIOKU_VAULT_PATH}",
        "KIOKU_OLLAMA_URL": "http://localhost:11434",
        "KIOKU_EMBEDDING_MODEL": "nomic-embed-text"
      }
    }
  }
}
```

Ensure `KIOKU_VAULT_PATH` is exported in your environment before launching OpenCode:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
```

---

### GitHub Copilot (VS Code)

GitHub Copilot in VS Code supports MCP server registration via `.vscode/mcp.json` (workspace scope) or global VS Code settings.

#### Workspace Configuration (`.vscode/mcp.json`)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "kioku",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/absolute/path/to/your/vault"
      }
    }
  }
}
```

---

### Antigravity CLI / IDE

Antigravity supports user-scoped configuration (`~/.gemini/config/mcp_config.json`), workspace-scoped plugin bundles (`.agents/plugins/kioku/`), or global plugin bundles (`~/.gemini/config/plugins/kioku/`).

#### User Configuration (`~/.gemini/config/mcp_config.json`)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "kioku",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "${KIOKU_VAULT_PATH}"
      }
    }
  }
}
```

#### Native Plugin Bundle

Copy `integrations/antigravity-plugin` to `~/.gemini/config/plugins/kioku/` (user scope) or `.agents/plugins/kioku/` (workspace scope).

---

## Verifying Server Startup & Tools

After completing registration, start your client and verify that `kioku` connects.

1. Call `tools/list` to confirm tools are registered.
2. Invoke `get_server_status` to verify vault path resolution and status.
3. Invoke `get_server_capabilities` to inspect available tool profiles and capability gates.

If `KIOKU_VAULT_PATH` is missing or invalid, server initialization fails explicitly with an actionable message.

---

## Streamable HTTP (Remote / Loopback Server)

Start a long-running HTTP server:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
export KIOKU_TRANSPORT=http
export KIOKU_API_KEY="$(openssl rand -hex 32)"
kioku
```

Endpoint: `http://127.0.0.1:5173/mcp`  
Header: `Authorization: Bearer <KIOKU_API_KEY>`

See [Streamable HTTP security](deploy/auth-options.md) for details on authentication and host binding options.

---

## Optional Obsidian Plugin

The optional Obsidian bridge plugin is maintained in [sandovaldavid/kioku-obsidian](https://github.com/sandovaldavid/kioku-obsidian). It is needed only for Obsidian UI operations or plugin bridge integrations. Direct note, search, session, and project tools operate directly on the vault filesystem without needing Obsidian to be running.
