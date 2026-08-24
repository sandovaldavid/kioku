---
layout: default
title: Installation Guide
sidebar: true
---

# Installation Guide

> Current tagged server release: **3.1.2** <!-- x-release-please-version -->

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

The unpinned commands install or update to the latest stable NuGet release. For reproducible setup, pin the exact version shown at the top of this page with `--version <version>`.

### Option B: Install via One-Line Install Script

```bash
curl -fsSL https://raw.githubusercontent.com/sandovaldavid/kioku/main/scripts/install.sh | bash
```

This installs the standalone `kioku` binary for your platform to `~/.local/bin/kioku`. Ensure `~/.local/bin` is in your `PATH`.

### Option C: Build from Source

Use this when you only need to compile or develop Kioku from a checkout. `dotnet build` does **not** install the `kioku` command as a global tool.

```bash
git clone https://github.com/sandovaldavid/kioku.git
cd kioku
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
```

### Option D: Install a Local Source Build

Use this path when you need the globally resolvable `kioku` command to run the exact source currently checked out—for example, to validate `develop`, an unreleased fix, or a pull-request branch before it is published to NuGet.

Start from the branch you intend to test:

```bash
git fetch origin
git switch develop
# Or: git switch <branch-name>
git pull --ff-only
```

Restore and pack the .NET tool with a **local-only prerelease version**. Do not reuse a published Kioku version and do not pre-claim the next real release number.

```bash
rm -rf ./artifacts/packages

dotnet restore Kioku.slnx

dotnet pack \
  src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj \
  --configuration Release \
  --no-restore \
  -p:PackageVersion=0.0.0-local.1 \
  --output ./artifacts/packages
```

The package is written under `./artifacts/packages`. When repacking another local build, increment the prerelease suffix (`0.0.0-local.2`, `0.0.0-local.3`, ...) so the artifact you install is unambiguous and cannot be confused with a tagged release.

If Kioku is already installed globally, remove that installation first:

```bash
dotnet tool uninstall --global kioku-mcp-server
```

Install the package from the local artifacts directory using the same version passed to `dotnet pack`:

```bash
dotnet tool install --global \
  --add-source ./artifacts/packages \
  --version 0.0.0-local.1 \
  kioku-mcp-server
```

`--add-source` adds the local package directory to the NuGet sources used by the tool installer. The unique `0.0.0-local.*` version keeps this developer workflow distinct from published Kioku packages. Kioku's CI uses a stricter isolated NuGet configuration and package-source mapping for release/package smoke validation; that extra isolation is not required for ordinary local branch testing.

Verify the installed artifact:

```bash
dotnet tool list --global
kioku --version
```

For the example above, `kioku --version` should report:

```text
0.0.0-local.1
```

After validating the local build, restore the latest stable installation with:

```bash
dotnet tool uninstall --global kioku-mcp-server
dotnet tool install --global kioku-mcp-server
kioku --version
```

The commands above avoid shell-specific variable assignment and can be run from Bash, Zsh, or Fish. On Windows, run the equivalent commands from PowerShell or another shell that supports the shown line-continuation syntax, or place each `dotnet` invocation on one line.

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

To refresh an existing installation after a Kioku release:

```bash
claude plugin marketplace update kioku
claude plugin update kioku@kioku
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

`--version` (or `-v`) prints the server version and exits without starting a transport, reading the vault, or requiring `KIOKU_VAULT_PATH`. The output should match the current tagged release shown at the top of this page when you installed from NuGet or the tagged binary release. A local source build instead reports the local prerelease version supplied during `dotnet pack`. Any other invocation starts the MCP server.

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
