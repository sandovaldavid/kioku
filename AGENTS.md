# Kioku agent operating manual

This file defines how coding agents must work in `sandovaldavid/kioku`. It is an operational guide for the current `develop` branch, not a roadmap or historical report.

## Repository scope

Kioku is a .NET 10 Model Context Protocol server that reads and writes an Obsidian vault. The server supports local `stdio` and authenticated Streamable HTTP transports. The optional Obsidian bridge plugin is maintained and released independently in [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian).

The server accesses the vault filesystem directly. Obsidian does not need to be open for core note, search, project, session, indexing, or coordination operations. A running Obsidian application and plugin are required only for operations registered through the optional `bridge` and `plugin` capability groups.

This repository owns:

- the MCP server, contracts, prompts, resources, and integrations;
- vault indexing, retrieval, filesystem policy, work sessions, and engineering workflows;
- server packaging, Docker deployment, CI, releases, retrieval evaluation, and public documentation.

This repository does not own the Obsidian plugin source or its release workflow.

## Source-of-truth order

When statements disagree, use this precedence:

1. current code and configuration on the target branch;
2. executable tests and generated public contracts;
3. maintained operational documentation;
4. merged pull requests and closed issues as historical evidence only;
5. open issues, proposals, plans, and comments as non-implemented intent.

Never describe an issue, roadmap item, proposed architecture, benchmark expectation, or PR body as implemented unless the target branch contains the corresponding code or generated contract.

## Current verified snapshot

- Target framework: `net10.0`, configured in `Directory.Build.props`.
- Solution: `Kioku.slnx`.
- Latest published server package: `3.1.2`. <!-- x-release-please-version --> The integration branch can contain unreleased changes beyond that release.
- MCP C# SDK packages: `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` `2.2.0`.
- MCP protocol baseline: `2026-07-28`.
- Streamable HTTP runs explicitly with `Stateless = true`; local `stdio` remains supported.
- Default MCP profile: 45 tools.
- All-capabilities profile: 78 tools.
- Engineering projects support first-class `spec` documents through `create_engineering_spec`; `create_implementation_plan` can link a same-project spec, and `get_project_context` accepts `spec` / `specs` aliases.
- New project scaffolds create `decisions`, `bugs`, `specs`, `plans`, `knowledge`, `sessions`, and `backlog` eagerly; `daily` and `tickets` remain supported optional/lazy workflows.
- Optional groups disabled by default: `research`, `generation`, `css`, `assets`, `bridge`, `plugin`, and `coordination`.
- Supported client integrations: Claude Code, Codex, OpenCode, GitHub Copilot, and Antigravity via native client MCP registration.
- The generated [`docs/commands-reference.md`](docs/commands-reference.md) is authoritative for tool names, schemas, annotations, prompts, resources, and profile counts.
- The generated [`docs/configuration-reference.md`](docs/configuration-reference.md) is authoritative for public process configuration.
- Markdown files in the vault are the durable source of truth. Runtime indexes and the embeddings cache are derived data; there are no database migrations to maintain.

Do not copy the complete tool or environment-variable inventory into this file.

## Branch and pull-request workflow

- Start ordinary work from `origin/develop`.
- Target ordinary pull requests to `develop`.
- `main` is the release branch; promotion follows the repository release workflow.
- After every release, synchronize `main` back into `develop` before starting the next release cycle.
- Keep one focused concern per pull request.
- Link an issue only when the pull request fully resolves it.
- Do not claim hosted branch rules, secrets, environments, or external publishing configuration are active unless they were directly verified.

## Repository map

```text
/
├── AGENTS.md
├── README.md
├── CONTRIBUTING.md
├── Kioku.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── Dockerfile
├── docker-compose.yml
├── docs/
│   ├── README.md
│   ├── engineering-workflows.md
│   ├── commands-reference.md        generated
│   ├── configuration-reference.md   generated
│   ├── versioning.md                generated
│   └── deploy/
├── scripts/
│   ├── Kioku.Ci/
│   ├── Kioku.Eval/
│   ├── generate-public-docs.mjs
│   └── validate-release-documentation.mjs
└── src/
    ├── Kioku.Mcp.Server/
    └── Kioku.Mcp.Server.Tests/
```

## Documentation boundary and status labels

The repository keeps current, public, operational truth. Historical reasoning, rejected alternatives, completed plans, cross-repository strategy, and session handoffs belong in Cortex-L7.

Use these labels during an audit:

- **Implemented** — present in the target branch and supported by code, configuration, tests, or generated contracts.
- **In progress** — active work exists but is not complete on the target branch.
- **Planned** — accepted future work with no complete implementation.
- **Blocked** — planned or active work cannot proceed because a named dependency is unresolved.
- **Deprecated** — still present for compatibility but not recommended for new use.
- **Historical** — useful point-in-time context that is not an active contract.
- **Discarded** — intentionally rejected, superseded, or closed without implementation.
- **Unconfirmed** — cannot be proven from accessible source, tests, or repository settings.

Rules:

- Keep only current operational statements in `README.md`, `AGENTS.md`, `CONTRIBUTING.md`, and active `docs/`.
- Move historical plans, proposals, alternatives, and completed audit snapshots to Cortex-L7.
- Mark compatibility surfaces explicitly as **Deprecated**.
- Do not include private vault paths, credentials, personal strategy, or session notes in this public repository.

## Generated contracts

Do not hand-edit:

- `docs/commands-reference.md`
- `docs/configuration-reference.md`
- `docs/versioning.md`
- `src/Kioku.Mcp.Server/.mcp/server.json`

Change `docs/public-metadata.json`, runtime metadata, or the MCP surface first, then regenerate:

```bash
dotnet build Kioku.slnx --configuration Release --no-restore
node scripts/generate-public-docs.mjs --write
node scripts/generate-public-docs.mjs --check
```

Validate maintained repository-relative Markdown links and release-facing version metadata separately:

```bash
node scripts/validate-markdown-links.mjs
node scripts/validate-docs-navigation.mjs
node scripts/validate-release-documentation.mjs
node scripts/validate-portable-configs.mjs
```

## Implementation rules

- Keep MCP adapters thin. Put workflow behavior behind application services and external effects behind infrastructure services or ports.
- Preserve the vault filesystem boundary. External reads and permanent deletion require explicit configuration.
- Preserve unknown YAML frontmatter fields during mutations.
- Preserve headless server operation. Core tools, indexing, sessions, and coordination must not depend on a running Obsidian process; UI and supported-plugin operations belong behind `bridge` or `plugin` capability gates.
- Use focused creation tools in prompts and integrations. `create_note` and `create_project_doc` remain **Deprecated** compatibility wrappers during the documented compatibility window; first-class specs use `create_engineering_spec` directly.
- Preserve the distinction between durable engineering specs and implementation plans. Do not make an external workflow engine a Kioku runtime dependency or bypass Kioku with direct vault writes for integrated durable handoff.
- Keep optional higher-risk capabilities gated.
- Use structured logging. MCP `stdio` reserves stdout for protocol traffic; diagnostics belong on stderr.
- Do not add a cloud fallback for embeddings or generation without an explicit security and privacy review.
- Changes to bridge fixtures or compatibility policy must be coordinated with `sandovaldavid/kioku-obsidian`.

## Validation

Run the complete relevant local gate before opening a pull request:

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
dotnet format Kioku.slnx style --verify-no-changes --no-restore
node scripts/generate-public-docs.mjs --check
node scripts/validate-markdown-links.mjs
node scripts/validate-docs-navigation.mjs
node scripts/validate-release-documentation.mjs
node scripts/validate-portable-configs.mjs
```

Change-specific checks:

```bash
# Integration asset / configuration changes
node scripts/validate-portable-configs.mjs

# Dev Container changes
bash .devcontainer/scripts/validate-devcontainer.sh

# Compose changes
docker compose config
```

Record exact commands and results in the pull request. A skipped, disabled, or unavailable workflow is not a passing result.

## Documentation entry points

- [Documentation index](docs/README.md)
- [Installation](docs/install.md)
- [Engineering workflows](docs/engineering-workflows.md)
- [Architecture](docs/architecture.md)
- [MCP contracts](docs/commands-reference.md)
- [Configuration](docs/configuration-reference.md)
- [Vault configuration](docs/vault-config.md)
- [Security and privacy](docs/threat-and-privacy-model.md)
- [CI quality gates](docs/ci-quality-gates.md)
- [Troubleshooting](docs/troubleshooting.md)