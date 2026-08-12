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
When your vault directory path contains spaces, wrap the path in quotes in shell commands or configuration files:
- Shell: `export KIOKU_VAULT_PATH="/Users/yourname/My Obsidian Vault"`
- JSON / TOML: `"KIOKU_VAULT_PATH": "/Users/yourname/My Obsidian Vault"`

---

### Claude Code

Claude Code supports native CLI MCP registration as well as native plugin installation.

#### Native CLI Registration

```bash
# Global user scope (recommended for personal setup)
claude mcp add kioku --scope user --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku

# Project scope (workspace-local)
claude mcp add kioku --scope project --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### Native Plugin Installation

Claude Code users can install the Kioku plugin from the marketplace:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

Export `KIOKU_VAULT_PATH` before installing and keep it available whenever Claude Code launches.
The plugin passes the variable to the `kioku` process; no vault path is stored in this repository.
Missing or invalid values fail through Kioku's required configuration validation. Claude Code still
supports optional Ollama and embedding-model settings through the plugin's `userConfig`.

#### Manual JSON Configuration (`.mcp.json`)

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

### Codex CLI

Codex CLI manages MCP servers natively via `codex mcp add` or via `~/.codex/config.toml` (or `.codex/config.toml`).

#### Native CLI Registration

```bash
codex mcp add kioku --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### Manual TOML Configuration (`~/.codex/config.toml`)

Codex configuration uses TOML tables for MCP server definitions:

```toml
[mcp_servers.kioku]
command = "kioku"
args = []

[mcp_servers.kioku.env]
KIOKU_VAULT_PATH = "/absolute/path/to/your/vault"
```

---

### OpenCode

OpenCode configures MCP servers via the interactive `opencode mcp add` command or directly through `opencode.json` at the root of your workspace or user config. The current stable CLI does not document a fully parameterized one-line `mcp add` equivalent to Claude Code, Codex, or Copilot, so Kioku documents the exact answers for the interactive wizard.

#### Native CLI Registration

Export the vault path before starting the wizard:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
opencode mcp add
```

Use these values when prompted:

```text
MCP server name: kioku
MCP server type: Local
Command to run: kioku
```

Some OpenCode versions also ask where the configuration should be saved. If that prompt appears:

- choose **Global** to make Kioku available across OpenCode projects;
- choose **Current project** only when you intentionally want repository-local MCP configuration.

The wizard stores the MCP definition, but the exported vault path is process-local shell state. `KIOKU_VAULT_PATH` must therefore be present whenever you launch a future OpenCode session. Persist it using the mechanism appropriate for your shell if you want the setting to survive new terminals.

Verify that OpenCode can see and start Kioku:

```bash
opencode mcp list
```

The `kioku` entry should report as connected. If it does not, first verify that both the executable and vault variable are visible in the same shell used to launch OpenCode:

```bash
command -v kioku
printf '%s\n' "$KIOKU_VAULT_PATH"
```

#### Workspace Configuration (`opencode.json`)

`opencode.json` supports environment variable interpolation using `{env:VARIABLE_NAME}`:

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

---

### GitHub Copilot CLI / VS Code

GitHub Copilot CLI supports native MCP server registration via `copilot mcp add`. VS Code also supports workspace configuration via `.vscode/mcp.json`.

#### Native CLI Registration (Terminal Users)

```bash
copilot mcp add kioku --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### VS Code Workspace Configuration (`.vscode/mcp.json`)

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

### Antigravity

Antigravity supports global MCP server configuration in `~/.gemini/config/mcp_config.json` as well as native plugin bundle installation via `agy`.

#### Native MCP Configuration (`~/.gemini/config/mcp_config.json`)

For standalone installations (`dotnet tool install -g kioku-mcp-server` or prebuilt binary), add `kioku` to `~/.gemini/config/mcp_config.json`:

```json
{
  "mcpServers": {
    "kioku": {
      "command": "kioku",
      "args": []
    }
  }
}
```

Ensure `KIOKU_VAULT_PATH` is exported in your environment prior to launching Antigravity:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
```

#### Native Plugin Bundle Installation (`agy`)

When working from a checkout of the repository (or local plugin directory), install the plugin bundle:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
agy plugin install ./integrations/antigravity-plugin
```

`agy plugin install` registers and imports the plugin globally under `~/.gemini/antigravity-cli/plugins/kioku/`.

---

## Verifying Server Startup & Tools

Confirm that the executable itself is installed and resolvable:

```bash
kioku --version
```

`--version` (or `-v`) prints the server version and exits without starting a transport, reading the vault, or requiring `KIOKU_VAULT_PATH`. Any other invocation starts the MCP server.

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
