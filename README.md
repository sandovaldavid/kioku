# Kioku — persistent memory for AI agents in Obsidian

> **Kioku** (記憶) means “memory” in Japanese.
>
> Latest tagged server release: **2.3.0** · [Documentation](https://sandovaldavid.github.io/kioku/) · [Releases](https://github.com/sandovaldavid/kioku/releases)

Kioku is a local-first Model Context Protocol server that lets Claude Code, Codex, OpenCode, and other MCP clients continue work across fresh sessions by reading and updating structured knowledge in an Obsidian vault.

It combines typed MCP contracts, a strict vault filesystem boundary, concurrent work-session ownership, full-text and semantic retrieval, and an optional Obsidian bridge. The server supports local `stdio` and authenticated **Streamable HTTP** deployments.

The `develop` branch can contain verified but unreleased changes beyond the latest tag. Use generated contracts from the branch you are running.

## Why Kioku

- **Deterministic handoff** — agents can record project context, decisions, plans, bugs, daily notes, and session handoffs.
- **Obsidian-native storage** — Markdown and YAML frontmatter remain readable and editable without Kioku.
- **Safe vault access** — writes stay inside the configured vault; external reads and permanent deletion require explicit opt-in.
- **Stable MCP contracts** — tool schemas, annotations, prompts, and resources are mechanically documented from live discovery.
- **Local AI support** — optional Ollama embeddings and generation keep note content on your machine under the default configuration.
- **Optional UI bridge** — the [Obsidian plugin](https://github.com/sandovaldavid/kioku-obsidian) can open notes, run approved commands, and integrate supported plugins.

## Repository scope

This repository is the source of truth for the .NET MCP server, its integrations, packaging, deployment, tests, and public operational documentation. The Obsidian plugin is maintained and released separately in [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian).

Open issues, historical plans, and pull-request descriptions are not implementation evidence. Current behavior is defined by the target branch's code, tests, and generated contracts.

## Quick start

### Install the server

```bash
dotnet tool install --global kioku-mcp-server
```

Set the vault path and register `kioku` in your MCP client:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
kioku
```

For supported client installers:

```bash
./scripts/add-to-client.sh codex --vault /absolute/path/to/your/vault
./scripts/add-to-client.sh opencode --vault /absolute/path/to/your/vault
./scripts/add-to-client.sh antigravity --vault /absolute/path/to/your/vault
```

Claude Code users can install the bundled plugin and skill:

```bash
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

See the [installation guide](docs/install.md) for manual client configuration, source builds, Docker, and the optional [Obsidian plugin](https://github.com/sandovaldavid/kioku-obsidian).

## Architecture

```text
MCP client
  ├─ stdio (local process)
  └─ Streamable HTTP (long-running authenticated server)
          │
          ▼
Kioku.Mcp.Server (.NET 10)
  ├─ typed MCP tools, prompts, and resources
  ├─ bounded vault indexing and hybrid retrieval
  ├─ application and infrastructure services
  └─ optional authenticated WebSocket bridge
          │
          ▼
Obsidian vault + optional Obsidian plugin (sandovaldavid/kioku-obsidian)
```

See the [current architecture](docs/architecture.md) for operational component boundaries.

## Public contracts and guides

Start with the [documentation index](docs/README.md). The main maintained references are:

- [MCP contract reference](docs/commands-reference.md) — live `tools/list`, schemas, annotations, prompts, resources, and profile counts.
- [Server configuration reference](docs/configuration-reference.md) — every public `KIOKU_*` variable and canonical `Kioku:*` path.
- [Vault configuration](docs/vault-config.md) — folders, defaults, exclusions, capabilities, frontmatter, and generated indexes.
- [Focused-tool migration](docs/focused-tool-migration.md) — current replacements for deprecated generic creation wrappers.
- [Versioning policy](docs/versioning.md) — server, plugin, workspace, and bridge compatibility semantics.
- [Threat and privacy model](docs/threat-and-privacy-model.md) — implemented mitigations, known gaps, and external data flows.

Regenerate and verify public metadata with:

```bash
node scripts/generate-public-docs.mjs --write
node scripts/generate-public-docs.mjs --check
```

## Security defaults

- `stdio` is the default transport.
- Streamable HTTP binds to `127.0.0.1` by default.
- Non-loopback HTTP requires `KIOKU_API_KEY` unless an explicit unsafe override is set.
- Browser origins and trusted proxies use exact allowlists.
- External reads and permanent deletion are disabled by default.
- Obsidian bridge authentication uses a separate shared token.

Review [Streamable HTTP security](docs/deploy/auth-options.md) before exposing Kioku through a VM, LAN, reverse proxy, container, or tunnel.

## Development

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
dotnet format Kioku.slnx style --verify-no-changes --no-restore
node scripts/generate-public-docs.mjs --check
```

See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md) for repository conventions.

## License

MIT — see [LICENSE](LICENSE).
