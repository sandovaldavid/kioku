---
layout: default
title: Installation Guide
sidebar: true
---

# Installation

## Requirements

- An Obsidian vault.
- .NET 10 only when building from source; the published global tool and self-contained binaries include what they need.
- Ollama only for semantic retrieval or local generation.
- The Obsidian plugin only for UI and supported-plugin bridge operations.

## Install the .NET tool

```bash
dotnet tool install --global kioku-mcp-server
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
kioku
```

Update later with:

```bash
dotnet tool update --global kioku-mcp-server
```

## Register an MCP client

The repository installer supports Codex, OpenCode, and Antigravity:

```bash
./scripts/add-to-client.sh codex --vault /absolute/path/to/your/vault
./scripts/add-to-client.sh opencode --vault /absolute/path/to/your/vault
./scripts/add-to-client.sh antigravity --vault /absolute/path/to/your/vault
```

Claude Code can install the bundled server configuration and skill:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

A minimal manual stdio configuration is:

```json
{
  "mcpServers": {
    "kioku": {
      "type": "stdio",
      "command": "kioku",
      "env": {
        "KIOKU_VAULT_PATH": "/absolute/path/to/your/vault"
      }
    }
  }
}
```

Use your client’s equivalent MCP configuration location. Keep the vault path absolute.

## Streamable HTTP

Start a long-running loopback server:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
export KIOKU_TRANSPORT=http
export KIOKU_API_KEY="$(openssl rand -hex 32)"
kioku
```

Endpoint: `http://127.0.0.1:5173/mcp`

A client must send:

```text
Authorization: Bearer <KIOKU_API_KEY>
```

Read [Streamable HTTP security](deploy/auth-options.md) before changing the bind host or placing Kioku behind a proxy.

## Build from source

```bash
git clone https://github.com/sandovaldavid/kioku.git
cd kioku
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
KIOKU_VAULT_PATH=/absolute/path/to/vault dotnet run --project src/Kioku.Mcp.Server
```

Publish a native single-file binary by selecting a supported RID:

```bash
dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained \
  --output artifacts/kioku
```

Representative RIDs include `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, and `osx-arm64`.

## Optional Obsidian plugin

```bash
corepack enable
pnpm install --frozen-lockfile
pnpm --filter obsidian-kioku-mcp run build
```

Copy `main.js`, `manifest.json`, and `styles.css` from `src/obsidian-kioku-mcp/` into:

```text
<vault>/.obsidian/plugins/kioku-mcp/
```

Enable **Kioku MCP** under Obsidian → Settings → Community plugins. Configure the plugin auth token and `KIOKU_BRIDGE_TOKEN` with the same secret.

## Configuration

The complete, generated list of environment variables and canonical configuration paths is in the [server configuration reference](configuration-reference.md). Vault-level behavior is documented in [vault configuration](vault-config.md).

## Verify an installation

After starting the server, use an MCP client to run `tools/list`, then call `get_server_status`. For HTTP deployments:

```bash
curl http://127.0.0.1:5173/health/live
curl -H "Authorization: Bearer $KIOKU_API_KEY" http://127.0.0.1:5173/health/ready
```
