# Kioku — persistent memory for AI agents in Obsidian

> **Kioku** (記憶) means “memory” in Japanese.
>
> Latest tagged server release: **3.1.2** <!-- x-release-please-version --> · [NuGet](https://www.nuget.org/packages/kioku-mcp-server) · [Documentation](https://sandovaldavid.github.io/kioku/) · [Releases](https://github.com/sandovaldavid/kioku/releases)

Kioku is a local-first Model Context Protocol server that lets Claude Code, Codex, OpenCode, and other MCP clients continue work across fresh sessions by reading and updating structured knowledge in an Obsidian vault.

It combines typed MCP contracts, a strict vault filesystem boundary, first-class engineering specs and plans, concurrent work-session ownership, full-text and semantic retrieval, and an optional Obsidian bridge. The server supports local `stdio` and authenticated **Streamable HTTP** deployments.

Kioku reads and writes the vault directory directly. The Obsidian application does not need to be open for core note, search, project, session, indexing, or coordination operations. Obsidian and the companion plugin are required only for optional UI and supported-plugin bridge operations.

The `develop` branch can contain verified but unreleased changes beyond the latest tag. Use generated contracts from the branch you are running.

## Why Kioku

- **Deterministic handoff** — agents can recover project context, approved engineering specs, implementation plans, decisions, bugs, knowledge, and session handoffs.
- **Obsidian-native storage** — Markdown and YAML frontmatter remain readable and editable without Kioku.
- **Headless server operation** — core MCP workflows continue when Obsidian is closed.
- **Safe vault access** — writes stay inside the configured vault; external reads and permanent deletion require explicit opt-in.
- **Stable MCP contracts** — tool schemas, annotations, prompts, and resources are mechanically documented from live discovery.
- **Local AI support** — optional Ollama embeddings and generation keep note content on your machine under the default configuration.
- **Optional UI bridge** — the [Obsidian plugin](https://github.com/sandovaldavid/kioku-obsidian) can open notes, run approved commands, and integrate supported plugins.

## Repository scope

This repository is the source of truth for the .NET MCP server, its integrations, packaging, deployment, tests, and public operational documentation. The Obsidian plugin is maintained and released separately in [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian).

Open issues, historical plans, and pull-request descriptions are not implementation evidence. Current behavior is defined by the target branch's code, tests, and generated contracts.

## Quick start

### Step 1: Install the server

```bash
dotnet tool install --global kioku-mcp-server
```

Or install via one-liner script:

```bash
curl -fsSL https://raw.githubusercontent.com/sandovaldavid/kioku/main/scripts/install.sh | bash
```

### Step 2: Register in your MCP client

Set `KIOKU_VAULT_PATH` and register using your client's native registration mechanism:

#### Claude Code
```bash
# Global user scope
claude mcp add kioku --scope user --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku

# Or via plugin marketplace
claude plugin marketplace add sandovaldavid/kioku
claude plugin install kioku@kioku
```

#### Codex CLI
```bash
codex mcp add kioku --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### OpenCode
```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
opencode mcp add
```

When OpenCode prompts you, use:

```text
MCP server name: kioku
MCP server type: Local
Command to run: kioku
```

If OpenCode asks where to save the configuration, choose **Global** to make Kioku available across projects, or **Current project** only when you intentionally want repository-local configuration. Then verify the connection:

```bash
opencode mcp list
```

`KIOKU_VAULT_PATH` must also be present in the environment when future OpenCode sessions start; persist it in your shell profile if you want the setting to survive new terminals.

#### GitHub Copilot CLI
```bash
copilot mcp add kioku --env KIOKU_VAULT_PATH="/absolute/path/to/your/vault" -- kioku
```

#### Antigravity CLI (`agy`)
```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
# Native MCP configuration (~/.gemini/config/mcp_config.json):
# { "mcpServers": { "kioku": { "command": "kioku" } } }

# Or install local plugin bundle (from cloned kioku repository):
# agy plugin install ./integrations/antigravity-plugin
```

See the [Installation Guide](docs/install.md) for detailed configuration, scope options, manual TOML/JSON files, Docker, and the optional [Obsidian plugin](https://github.com/sandovaldavid/kioku-obsidian).

## Durable engineering workflow

Kioku separates durable design requirements from implementation steps:

```text
request / issue
    ↓
engineering SPEC
    ↓
implementation PLAN
    ↓
SESSION / execution / handoff
```

Use `create_engineering_spec` to persist what must be built and how it must behave. `create_implementation_plan` can then link the implementation plan to that same-project spec through additive frontmatter metadata. Approved specs are recoverable through `get_project_context(types="spec")` or `get_project_context(types="specs")` without making Kioku depend on a particular external coding methodology.

New projects scaffold `decisions`, `bugs`, `specs`, `plans`, `knowledge`, `sessions`, and `backlog` as durable core folders. `daily` and `tickets` remain supported optional workflows and materialize only when explicitly written.

See [Engineering Workflows](docs/engineering-workflows.md) for spec lifecycle, SPEC → PLAN linking, durable revision behavior, and the generic external-workflow boundary.

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
          ├──────────────► Obsidian vault on disk
          │
          └── optional ──► running Obsidian plugin
```

See the [current architecture](docs/architecture.md) for operational component boundaries.

## Public contracts and guides

Start with the [documentation index](docs/README.md). The main maintained references are:

- [MCP contract reference](docs/commands-reference.md) — live `tools/list`, schemas, annotations, prompts, resources, and profile counts.
- [Engineering workflows](docs/engineering-workflows.md) — first-class specs, SPEC → PLAN relationships, project scaffold semantics, and durable workflow boundaries.
- [Server configuration reference](docs/configuration-reference.md) — every public `KIOKU_*` variable and canonical `Kioku:*` path.
- [Vault configuration](docs/vault-config.md) — folders, defaults, exclusions, capabilities, frontmatter, and generated indexes.
- [Focused-tool migration](docs/focused-tool-migration.md) — current replacements for deprecated generic creation wrappers.
- [2.3.0 to 3.0.0 migration](docs/migration-2.3.0-to-3.0.0.md) — breaking tool, discovery-profile, result, and mutation changes.
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
