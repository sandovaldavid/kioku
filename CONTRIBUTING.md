# Contributing to Kioku

Kioku is a .NET 10 MCP server. Changes branch from `origin/develop`, pull requests target `develop`, and release promotion to `main` follows the repository release workflow. The companion Obsidian plugin lives in its own repository, [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian), with its own contribution workflow.

## Development setup

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
corepack enable
pnpm install --frozen-lockfile
```

See [docs/install.md](docs/install.md) for complete setup and deployment instructions.

## Branches and commits

```bash
git checkout -b feat/my-change origin/develop
```

Use Conventional Commits with a required scope:

```text
type(scope): imperative description
```

Allowed scopes are `server`, `plugin`, `docs`, `ci`, `config`, `deps`, and `release`. Keep the header under 100 characters and do not add a trailing period.

## Code style

- C#: nullable analysis, analyzers, deterministic build, and warnings-as-errors are configured repository-wide. Run both `dotnet format` checks before submitting.
- Use structured logging instead of `Console` in production code.
- Do not add decorative separator comments or emojis to logs and protocol messages.

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

Update `docs/public-metadata.json` when adding or changing a public environment variable, capability profile, transport, manifest identity, or versioning rule. The check command compares that metadata with live MCP discovery, `KiokuOptions`, package manifests, and version files.

## Verification before a pull request

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
dotnet format Kioku.slnx style --verify-no-changes --no-restore
node scripts/generate-public-docs.mjs --check
```

Use a fresh temporary vault for tests that mutate files. Keep pull requests focused and include the exact commands used for verification.

Security issues must follow [SECURITY.md](SECURITY.md), not a public issue.
