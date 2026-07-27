# Kioku — persistent memory for AI agents in Obsidian

> **Kioku** (記憶) means “memory” in Japanese.
>
> Server release: **2.3.0** · [Documentation](https://sandovaldavid.github.io/kioku/) · [Releases](https://github.com/sandovaldavid/kioku/releases)

Kioku is a local-first Model Context Protocol server that lets Claude Code, Codex, OpenCode, and other MCP clients continue work across fresh sessions by reading and updating structured knowledge in an Obsidian vault.

It combines typed MCP contracts, a strict vault filesystem boundary, concurrent work-session ownership, full-text and semantic retrieval, and an optional Obsidian bridge. The server supports local `stdio` and authenticated **Streamable HTTP** deployments.

## The handoff, proven

Kioku's core claim is that one agent's work survives its process exiting, and a second, unrelated agent can pick it up cold. That is not a hypothetical: [`scripts/Kioku.HandoffDemo`](scripts/Kioku.HandoffDemo) drives the real MCP stdio protocol to run it end to end. Agent 1 opens a work session, records a plan, an ADR, and a bug, then closes the session — its subprocess exits and its MCP connection stops existing. Agent 2, a separate `McpClient` connection in its own subprocess with its own client identity, then calls `get_project_context`, retrieves everything Agent 1 wrote, and continues the project without ever touching Agent 1's session. A third, independent connection verifies both sessions afterward. See [`docs/multi-agent-handoff-demo.md`](docs/multi-agent-handoff-demo.md) for the full transcript of a real run, including captured PIDs and exit codes. The same server registers cleanly with Claude Code, Codex, and OpenCode via `./scripts/add-to-client.sh` in one command.

## Why Kioku

- **Deterministic handoff** — agents can record project context, decisions, plans, bugs, daily notes, and session handoffs.
- **Obsidian-native storage** — Markdown and YAML frontmatter remain readable and editable without Kioku.
- **Safe vault access** — writes stay inside the configured vault; external reads and permanent deletion require explicit opt-in.
- **Stable MCP contracts** — tool schemas, annotations, prompts, and resources are mechanically documented from live discovery.
- **Local AI support** — optional Ollama embeddings and generation keep note content on your machine.
- **Optional UI bridge** — the [Obsidian plugin](https://github.com/sandovaldavid/kioku-obsidian) can open notes, run approved commands, and integrate supported plugins.

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

## Public contracts

The detailed surface is generated rather than copied into multiple READMEs:

- [MCP contract reference](docs/commands-reference.md) — live `tools/list`, schemas, annotations, prompts, resources, and profile counts.
- [Server configuration reference](docs/configuration-reference.md) — every public `KIOKU_*` variable and canonical `Kioku:*` path.
- [Vault configuration](docs/vault-config.md) — folders, defaults, exclusions, capabilities, frontmatter, and generated indexes.
- [Versioning policy](docs/versioning.md) — server, plugin, workspace, and bridge compatibility semantics.
- [Architecture decision records](docs/adr/README.md) — why storage, indexing, search, transports, capabilities, and Ollama are built the way they are.
- [Performance benchmarks](docs/benchmarks.md) — cold-start, indexing, search latency, and retrieval quality, with environment and dataset caveats stated up front.
- [Threat and privacy model](docs/threat-and-privacy-model.md) — implemented mitigations vs. future work, and exactly when data can leave the machine.

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
corepack enable
pnpm install --frozen-lockfile
node scripts/generate-public-docs.mjs --check
```

See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md) for repository conventions.

## License

MIT — see [LICENSE](LICENSE).
