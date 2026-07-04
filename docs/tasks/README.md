# Kioku — Task Breakdown

Prioritized backlog of pending work. **Each task = one branch from `origin/develop` +
one PR (squash) into `develop`**, following the repo's workflow (never commit directly to
`main`/`develop`). Detailed technical specs live in [`docs/features/`](../features/README.md).

Conventions for each task:

- **Branch**: suggested name (`feat/`, `fix/`, `test/`, `chore/`).
- **Commit**: `type(scope): description` — valid scopes: `server | plugin | docs | ci | config | deps | release`.
- **Size**: S (< half a day) · M (1-2 days) · L (> 2 days).
- Common PR checklist: green build + tests, `dotnet format` / `pnpm lint:plugin`, and
  **regenerate `docs/commands-reference.md`** (`dotnet run --project scripts/GenerateCommandsRef`)
  if the PR adds/changes/renames tools.

## P0 — Bugs and fixes (do first)

| ID | Task | Branch | Size | Status |
|----|-------|------|:------:|--------|
| [P0-01](P0-01-suggest-tags-collision.md) | Resolve `suggest_tags` name collision | `fix/suggest-tags-collision` | S | ✅ Merged (#120) |
| P0-02 | Update `.mcp/server.json` (version + env vars) | — | S | ✅ Resolved in the PR for this docs revision |
| [P0-03](P0-03-merge-tools-group.md) | Regroup merge-conflict tools out of `plugin` | `fix/merge-tools-group` | S | ✅ Merged (#121) |
| [P0-04](P0-04-readme-version-sync.md) | Sync README/server.json versions with release-please | `chore/readme-version-sync` | S | ✅ Merged (#123) |
| [P0-05](P0-05-add-license.md) | Add LICENSE file (README references it but it doesn't exist) | `chore/add-license` | S | ✅ Merged (#122) |

## P1 — High value, content

| ID | Task | Branch | Size | Spec | Status |
|----|-------|------|:------:|------|--------|
| [P1-01](P1-01-bridge-latent-tools.md) | Expose 8 latent bridge commands as tools | `feat/bridge-latent-tools` | S | [01](../features/01-bridge-latent-tools.md) | ✅ Merged (#124) |
| [P1-02](P1-02-wikilink-auto-update.md) | Auto-update wikilinks in `move_note`/`rename_note` | `feat/wikilink-auto-update` | M | [02](../features/02-wikilink-auto-update.md) | ✅ Merged (#130) |
| [P1-03](P1-03-plugin-status-ui.md) | Status bar + bridge control commands (plugin) | `feat/plugin-status-ui` | S | [03](../features/03-plugin-status-ui.md) | ✅ Merged (#126) |
| [P1-04](P1-04-bridge-auth-token.md) | Token-based authentication for the WebSocket bridge | `feat/bridge-auth-token` | M | [04](../features/04-bridge-auth-token.md) | ✅ Merged (#132) |
| [P1-05](P1-05-http-and-bridge-coverage.md) | Test coverage: HTTP, ApiKeyMiddleware, bridge | `test/http-and-bridge-coverage` | M | — | ✅ Merged (#128) |

## P2 — Now horizon (v1.9–2.0)

| ID | Task | Branch | Size | Spec | Status |
|----|-------|------|:------:|------|--------|
| [P2-01](P2-01-local-generation.md) | Local generation with Ollama (`KIOKU_GEN_MODEL`) — **enabler** | `feat/local-generation` | M | [05](../features/05-local-generation.md) | ✅ Merged (#135) |
| [P2-02](P2-02-link-suggestions.md) | Link suggestions (`suggest_links` + apply) | `feat/link-suggestions` | M | [06](../features/06-link-suggestions.md) | ✅ Merged (#141) |
| [P2-03](P2-03-daily-digest.md) | Daily digest (`generate_digest`) | `feat/daily-digest` | S | [07](../features/07-daily-digest.md) | ✅ Merged (#137) |
| [P2-04](P2-04-smart-inbox.md) | Smart inbox (`process_inbox`) | `feat/smart-inbox` | S | [08](../features/08-smart-inbox.md) | ✅ Merged (#139) |
| [P2-05](P2-05-mcp-prompts-resources.md) | MCP Prompts & Resources | `feat/mcp-prompts-resources` | M | [09](../features/09-mcp-prompts-resources.md) | ✅ Merged (#143) |

## P3 — Next horizon (research)

| ID | Task | Branch | Size | Spec | Status |
|----|-------|------|:------:|------|--------|
| [P3-01](P3-01-zotero-bibtex.md) | BibTeX import/export (basis for Zotero) | `feat/zotero-bibtex` | M | [10](../features/10-zotero-bibtex.md) | ✅ Merged (#145) |
| [P3-02](P3-02-flashcards.md) | Flashcards (Spaced Repetition / Anki) | `feat/flashcards` | M | [11](../features/11-flashcards.md) | ✅ Merged (#149) |
| [P3-03](P3-03-incremental-reembedding.md) | Incremental re-embedding (cache v4 + progress) | `feat/incremental-reembedding` | M | [12](../features/12-incremental-reembedding.md) | ✅ Merged (#151) |
| [P3-04](P3-04-citation-graph.md) | Citation graph between notes and sources | `feat/citation-graph` | M | [13](../features/13-citation-graph.md) | ✅ Merged (#147) |

## Dependencies between tasks

```
P2-01 (local generation) ──► P3-02 (flashcards)
                         └──► improves P2-03 (digest, optional)
P1-02 (wikilinks)        ──► improves P2-04 (smart inbox, optional)
P2-02 (link suggestions) ──► improves P2-04 (smart inbox, optional)
P1-05 (bridge coverage)  ──► recommended before P1-04 (auth changes the protocol)
P3-01 (BibTeX)           ──► P3-04 (citation graph uses citekeys)
```

Suggested execution order: P0-01 → P0-03 → P0-04 → P1-01 → P1-03 → P1-05 → P1-02 →
P1-04 → P2-01 → P2-03 → P2-04 → P2-02 → P2-05 → P3-*.

## When completing a task

1. Mark its row as `✅ Merged (#PR)` in this index (same PR or a docs one).
2. If tools changed: verify that `commands-reference.md` was regenerated.
3. If env vars or capability groups were added: update the root README, server README,
   `docs/install.md`, `docs/vault-config.md` and `.mcp/server.json` in the same PR.
