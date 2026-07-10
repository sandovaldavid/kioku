# Contributing to Kioku

Thanks for your interest in contributing! This project is a monorepo: a C# .NET 10 MCP
server (`src/Kioku.Mcp.Server/`) and a TypeScript Obsidian plugin
(`src/obsidian-kioku-mcp/`), bridged over a local WebSocket.

## Before you start

- Check [open issues](https://github.com/sandovaldavid/kioku/issues) and
  [pull requests](https://github.com/sandovaldavid/kioku/pulls) to avoid duplicate work.
  For anything non-trivial, open an issue first to discuss the approach.
- By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
- Found a security issue? Follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Development setup

See [docs/install.md](docs/install.md) for full setup instructions. In short:

```bash
# Server (requires .NET 10 SDK)
dotnet build src/Kioku.Mcp.Server/
dotnet test src/Kioku.Mcp.Server.Tests/

# Plugin (requires Node.js 24+ and pnpm 11+)
pnpm install
pnpm build:plugin
pnpm lint:plugin
```

## Branch workflow

All changes branch from `origin/develop`; PRs target `develop` (squash-merge only).
Never commit directly to `main` or `develop`. Release Please runs only on `main`
(single channel, prerelease/beta by default); it never opens a `develop` → `main`
PR itself. Periodically, a maintainer promotes `develop` into `main` via a sync PR
opened from a short-lived intermediate branch — see `scripts/sync-develop-to-main.sh`.
Version numbers, `CHANGELOG.md`, and other release-managed files are never hand-edited
in that sync — they're always resolved to `main`'s current value and left for
Release Please's own automated release PR to update afterward.

Always merge that sync PR with a **merge commit**, never squash. It can carry
many individual commits from `develop`; squashing folds every one of their
messages into a single commit body, which destroys granular history on `main`
and can make Release Please misfire on old `!` breaking-change markers buried
in that combined text.

The reverse also applies: if `main` ever needs to be caught back up into
`develop` (e.g. after a sync PR), that catch-up PR must be merged with
**"Rebase and merge"**, never squash — `develop`'s branch protection requires
linear history, which rules out a merge commit there, but squashing a
multi-commit catch-up hits the exact same problem as above (one squashed PR
into `develop`, later carried into `main` by a real merge, can still poison
`main`'s history with a concatenated commit body). Rebase-merge keeps history
linear *and* keeps each original commit message separate.

To cut a stable release instead of the next beta, temporarily remove `"prerelease"`,
`"prerelease-type"`, and `"versioning"` from `release-please-config.json`, merge that
to `main`, let Release Please open and merge its release PR, then restore those three
keys to resume the beta series.

```bash
git checkout -b feat/my-feature origin/develop
# ... work ...
gh pr create --base develop
```

## Commit messages

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) and are
linted locally via commitlint (installed as part of `pnpm install`, runs on `git commit`):

```
type(scope): imperative description
```

- **Scope is required** and must be one of: `server`, `plugin`, `docs`, `ci`, `config`,
  `deps`, `release`.
- Lowercase, no trailing period, header under 100 characters.

```
feat(server): add search_by_alias tool
fix(plugin): handle null vault path on startup
docs(docs): add WebSocket protocol reference
```

## Code style

- **C#**: format with `dotnet format src/Kioku.Mcp.Server/` before committing. No
  separator comments (`// ── Name ──`) — use plain `// Name`. Inject `ILogger<T>` and use
  the `.Info()/.Warn()/.Error()/.Debug()` extensions from `Kioku.Mcp.Server.Logging`.
- **TypeScript**: format with `pnpm format:plugin`, lint with `pnpm lint:plugin`. Use
  `import { log } from "./logger"` and `log.info/warn/error/debug` instead of `console.*`.
- No emojis in strings or logs — use `[error]`, `[ok]`, `[loading]`, `[info]` prefixes
  instead.

## Adding or changing MCP tools

If your change adds, renames, or changes the signature of a tool, regenerate the tools
reference before opening the PR:

```bash
dotnet build src/Kioku.Mcp.Server/
dotnet run --project scripts/GenerateCommandsRef
```

This regenerates `docs/commands-reference.md`. If the change adds new environment
variables or capability groups, also update the root `README.md`,
`src/Kioku.Mcp.Server/README.md`, `docs/install.md`, and `docs/vault-config.md`.

## Tests

- Server: `dotnet test src/Kioku.Mcp.Server.Tests/` — please add tests for new tools and
  bug fixes. Tools that write/move files should use a fresh temporary vault per test
  (`IAsyncLifetime`), not the shared `VaultFixture`.
- Plugin: covered by Vitest (`pnpm --filter obsidian-kioku-mcp test`, if configured for
  your change).

## Submitting a pull request

1. Make sure `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` are
   all green for anything touching the server (equivalent `pnpm` commands for the plugin).
2. Push your branch and open a PR against `develop` with a clear summary of what changed
   and why, plus how you tested it.
3. Keep PRs focused — one logical change per PR is easier to review than a bundle of
   unrelated fixes.
