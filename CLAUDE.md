# Kioku

MCP server (C# .NET 10) exposing vault tools via stdio/HTTP MCP. The companion Obsidian plugin,
which bridges via WebSocket on port 7765, lives in its own repository:
[`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian).

## Structure

```
src/Kioku.Mcp.Server/       C# MCP server (stdio transport)
  Tools/                    MCP tools: default/all-capabilities profiles are 43/76 tools; see commands-reference.md
  Services/                 VaultIndexService, EmbeddingService, EmbeddingPersistence,
                              ObsidianBridgeService, FrontmatterParser, MarkdownTextExtractor
  Domain/                   Note, NoteMetadata, SearchResult
  Logging/                  KiokuLogger (ILogger<T> extension methods)
integrations/               Client-specific packaging (Claude Code plugin, Antigravity plugin)
scripts/add-to-client.sh    One-command MCP registration for Claude Code/Codex/OpenCode/Antigravity
```

## Commands

| Task | Command |
|------|---------|
| Build server | `dotnet build src/Kioku.Mcp.Server/` |
| Format C# | `dotnet format src/Kioku.Mcp.Server/` |

## Commit conventions

Scope is **required**. Valid scopes: `server | plugin | docs | ci | config | deps | release | integrations`

Format: `type(scope): imperative description` — lowercase, no period, max 100 chars

```
feat(server): add search_by_alias tool
fix(plugin): handle null vault path on startup
docs(docs): add WebSocket protocol reference
```

## Code style rules

- No separator comments (`// ── Name ──────────`). Use plain `// Name` instead.
- No emojis in strings. Use `[error]`, `[ok]`, `[loading]`, `[info]`, `[online]` prefixes.
- C# logging: inject `ILogger<T>` and use `.Info()/.Warn()/.Error()/.Debug()` from `Kioku.Mcp.Server.Logging`

## Branch workflow

All changes branch from `origin/develop`. Regular feature/fix PRs target `develop`
and are squash-merged. Never commit directly to `main` or `develop`.
Release Please runs only on `main` (single channel, prerelease/beta by default).
Promoting `develop` into `main` is a periodic sync PR from an intermediate branch
(never push directly to `develop` or `main`) — see `scripts/sync-develop-to-main.sh`.
Merge that sync PR with a merge commit, never squash (squashing destroys
granular history and can make Release Please misfire on old breaking-change
markers). The reverse catch-up PR (bringing `main` back into `develop`) must
use rebase-merge instead, since `develop` requires linear history but squashing
a multi-commit catch-up has the same poisoned-history risk. Release Please then
opens its own automated release PR against `main`.

```bash
git checkout -b feat/my-feature origin/develop
# ... work ...
gh pr create --base develop
```

## MCP tool surface

The server exposes 43 tools in the default profile and 76 with all capabilities enabled.
`NoteQueryTools`, `NoteCommandTools`, and `UtilityTools` are always enabled. The default-disabled
groups are `research`, `generation`, `css`, `assets`, `bridge`, `plugin`, and `coordination`.
`git`, `restore`, and `zettelkasten` are removed groups; use native Git, `manage_trash`, and
structured `create_note` kinds instead.

Use [`docs/commands-reference.md`](docs/commands-reference.md) as the authoritative inventory.
The migration table is in [`docs/migration-v3.md`](docs/migration-v3.md).

## Environment variables (server)

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KIOKU_VAULT_PATH` | yes | — | Absolute path to the Obsidian vault |
| `KIOKU_MAX_RESULTS` | no | 20 | Max search results |
| `KIOKU_OBSIDIAN_PORT` | no | 7765 | WebSocket port of the plugin |
| `KIOKU_OLLAMA_URL` | no | `http://localhost:11434` | Ollama base URL |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Ollama embedding model |

## Semantic search

The server uses Ollama to generate embeddings for semantic searches
(`search_notes` with `mode='semantic'`).
If Ollama is unavailable at startup, the service degrades gracefully: keyword and hybrid search
still work.

```bash
# Pull the embedding model once
ollama pull nomic-embed-text

# Embeddings are cached at:
{KIOKU_VAULT_PATH}/.kioku/embeddings.bin   (~15MB for 5000 notes)
```
