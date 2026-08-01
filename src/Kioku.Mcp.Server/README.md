# Kioku MCP Server

> Server package version: **2.3.0** · NuGet package: `kioku-mcp-server`

The server is a .NET 10 MCP host for safe, structured access to an Obsidian vault. It supports local `stdio` and authenticated **Streamable HTTP** transports, a bounded indexing pipeline, typed tool results, prompts, resources, and an optional Obsidian WebSocket bridge.

## Public surface

Do not maintain tool or environment-variable inventories in this package README. The authoritative generated references are:

- [MCP contract reference](../../docs/commands-reference.md)
- [Server configuration reference](../../docs/configuration-reference.md)
- [Vault configuration](../../docs/vault-config.md)
- [Versioning policy](../../docs/versioning.md)

## Architecture

```text
Transport and MCP adapters
        │
        ▼
Application workflows and typed contracts
        │
        ▼
Vault, indexing, retrieval, bridge, and persistence infrastructure
```

Capability groups are configured in `{vault}/.kioku/config.yml`. Core tools are always available; optional groups can be enabled or disabled without changing the server package.

## Development

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
dotnet format Kioku.slnx style --verify-no-changes --no-restore
node scripts/generate-public-docs.mjs --check
```

Run locally:

```bash
KIOKU_VAULT_PATH=/absolute/path/to/vault dotnet run --project src/Kioku.Mcp.Server
KIOKU_VAULT_PATH=/absolute/path/to/vault dotnet run --project src/Kioku.Mcp.Server -- --http
```

Logs are written to stderr because stdout is reserved for the MCP protocol.
