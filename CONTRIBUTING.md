# Contributing to Kioku

Kioku is a .NET 10 MCP server. Ordinary changes branch from `origin/develop`, pull requests target `develop`, and release promotion to `main` follows the repository release workflow. After each release, synchronize `main` back into `develop` before starting the next release cycle. The companion Obsidian plugin lives in its own repository, [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian), with its own contribution and release workflow.

The server reads and writes the vault filesystem directly. Core server development and tests must not require Obsidian to be open. A running Obsidian application and plugin are required only when validating the optional `bridge` or `plugin` capability groups.

## Development setup

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
```

See the [documentation index](docs/README.md), [installation guide](docs/install.md), and [Dev Container guide](docs/dev-container.md) for supported setup paths.

## Branches and commits

```bash
git checkout -b feat/my-change origin/develop
```

Use Conventional Commits with a required scope:

```text
type(scope): imperative description
```

Use one of the scopes `server`, `plugin`, `docs`, `ci`, `config`, `deps`, `release`, or `integrations`. The `plugin` scope is reserved for compatibility and cross-repository contract changes; plugin implementation belongs in `sandovaldavid/kioku-obsidian`.

Keep the header under 100 characters and do not add a trailing period.

## Code style

- C#: nullable analysis, analyzers, deterministic builds, and warnings-as-errors are configured repository-wide.
- Use structured logging instead of `Console` in production code.
- Keep stdout reserved for MCP protocol traffic under `stdio`; diagnostics belong on stderr.
- Do not add decorative separator comments or emojis to logs and protocol messages.
- Preserve unknown YAML frontmatter fields and the vault filesystem boundary.
- Preserve headless operation for core tools, indexing, sessions, and coordination. Obsidian-dependent behavior must remain behind the optional bridge or plugin boundary.

## Documentation policy

Repository documentation describes the current target branch. It must not present an issue, plan, proposal, PR body, historical benchmark, or external repository setting as implemented.

Use the status taxonomy in [AGENTS.md](AGENTS.md):

- `Implemented`
- `In progress`
- `Planned`
- `Blocked`
- `Deprecated`
- `Historical`
- `Discarded`
- `Unconfirmed`

Keep current behavior, architecture, contracts, setup, testing, deployment, and troubleshooting in this repository. Move alternatives, rationale, completed plans, historical snapshots, cross-repository strategy, and session handoffs to Cortex-L7.

When editing documentation:

- verify commands against versioned scripts or workflows;
- link to generated references instead of copying tool and environment-variable inventories;
- mark compatibility-only behavior as `Deprecated`;
- remove or relocate historical execution documents rather than leaving them beside active guidance;
- run the repository-relative Markdown link validator, sidebar contract validator, release-facing metadata validator, and generated public metadata check.

## MCP contracts and public metadata

Tool schemas, annotations, prompts, resources, environment variables, manifest metadata, and version semantics are generated or mechanically verified. After changing any public MCP or configuration contract, run:

```bash
dotnet build Kioku.slnx --configuration Release --no-restore
node scripts/generate-public-docs.mjs --write
node scripts/generate-public-docs.mjs --check
```

Do not hand-edit these generated files:

- `docs/commands-reference.md`
- `docs/configuration-reference.md`
- `docs/versioning.md`
- `src/Kioku.Mcp.Server/.mcp/server.json`

Update `docs/public-metadata.json` when adding or changing a public environment variable, capability profile, transport, manifest identity, or versioning rule. The generator compares that metadata with live MCP discovery, `KiokuOptions`, package manifests, and version files.

Validate maintained documentation and release metadata with:

```bash
node scripts/validate-markdown-links.mjs
node scripts/validate-docs-navigation.mjs
node scripts/validate-release-documentation.mjs
```

The link validator checks local file existence only. It does not claim that external URLs, redirects, hosted pages, or Markdown anchors are available. The release validator checks that package, manifest, README, agent guide, versioning page, and site badge versions remain synchronized and covered by Release Please.

## Verification before a pull request

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
```

Run change-specific checks when applicable:

```bash
# Client installer changes
for client in claude-code codex opencode antigravity; do
  ./scripts/add-to-client.sh "$client" --vault /absolute/path/to/test-vault --dry-run --yes
done

# Dev Container changes
bash .devcontainer/scripts/validate-devcontainer.sh

# Compose changes
docker compose config
```

Use a fresh temporary vault for tests that mutate files. Keep pull requests focused and include the exact commands and results used for verification. A skipped or unavailable workflow is not a passing result.

Security issues must follow [SECURITY.md](SECURITY.md), not a public issue.
