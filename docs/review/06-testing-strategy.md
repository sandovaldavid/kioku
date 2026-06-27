# 06 — Testing Strategy

Today's automated coverage is thin: xUnit unit tests for the utility layer only
(`FrontmatterParserTests`, `NoteHelpersTests`, `MarkdownTextExtractorTests`, `TaskServiceTests`),
**zero** plugin tests, and no integration / HTTP / auth / Ollama / E2E tests. Estimated ~5–10% of the
risk surface. CI does run `dotnet test` ✅, but there's no coverage gate.

This is the biggest blocker to landing the [01](./01-diagnosis-and-bugs.md) fixes safely and to a
confident 1.0. The goal isn't 100% coverage — it's covering the **mutation and search paths an agent
actually drives**, plus the protocol contract.

---

## Target test pyramid

```
            ┌───────────────────────────┐
            │  E2E smoke (few)          │  real server + fixture vault, a handful of MCP calls
            ├───────────────────────────┤
            │  Integration (some)       │  tools over a temp vault; HTTP+auth; mocked Ollama
            ├───────────────────────────┤
            │  Contract (1 critical)    │  bridge protocol shape: C# ⇄ TS
            ├───────────────────────────┤
            │  Unit (many)              │  parsers, helpers, services (have a start)
            └───────────────────────────┘
```

---

## Server (xUnit) — backlog

### Unit (extend what exists)
- `VaultConfigService`: folder/domain longest-prefix match, defaults, exclude, inherited tags,
  `GetTemplate`, malformed-YAML fallback (one clear warning, not silent).
- `NoteHelpers.ExpandTemplateVariables`: case rules, unmatched `{{x}}`, `$`-in-value, built-ins.
- `EmbeddingService.CosineSimilarity`: known vectors → known score; **equal-length assert**;
  mismatched-length behavior (ties to BUG-3).
- `EmbeddingPersistence`: round-trip; truncated file; **model/dim header mismatch → invalidate**.
- `HybridSearchService`: RRF ordering with crafted keyword+semantic inputs (golden test).

### Integration (new — over a temp fixture vault)
A `VaultFixture` that creates a throwaway directory of `.md` files per test is the unlock. Then:
- **Write tools round-trip**: `create_note` → `read_note` → `update_*` → `move_note` →
  `rename_note` → `delete_note`, asserting frontmatter + index state.
- **🔴 Path-traversal regression tests**: `move_note`/`rename_note`/`create_note` with `../`,
  absolute paths, and symlinks must be rejected (locks BUG-1 forever).
- **Search**: `search_notes` relevance/snippets; `filter_notes` by frontmatter; semantic search with a
  **mocked Ollama** HTTP endpoint (no real model needed in CI).
- **Index lifecycle**: create/modify/delete a file → assert debounced reindex updates word/tag/
  backlink indices; `rebuild_index` correctness.
- **Graceful degradation**: Ollama unreachable → `search_notes_semantic` returns `[info]`, keyword
  search still works.
- **Concurrency**: hammer the index with concurrent writes + a rebuild; assert no corruption.

### HTTP transport & auth (new)
- Boot the app with `KIOKU_TRANSPORT=http` on an ephemeral port; assert `/health` is open and `/mcp`
  requires the bearer token when `KIOKU_API_KEY` is set; 401 on bad/missing token; CORS allows only
  the configured origins.

### Mocking Ollama
Stand up a tiny in-process HTTP stub returning canned `{"embeddings":[[...]]}` so embedding tests are
deterministic and CI needs no GPU/model. Cover: success, timeout, 500, malformed body.

---

## Plugin (Vitest) — backlog (currently zero)

Add Vitest + a mocked Obsidian `App`/`Vault`/`Workspace`:
- **Each handler**: happy path + missing/empty payload (locks BUG-7) → clean error, never a throw.
- **`open-file`**: rejection path returns `success:false` (locks BUG-6).
- **Dispatch**: unknown command, malformed JSON (`bridge.ts:34`), `requestId` echo.
- **Plugin-not-installed**: Dataview/Templater/Linter guards return friendly errors.
- **Lifecycle**: `onunload` closes server + clients, clears collections (no leak).

---

## Contract test (the one that prevents silent breakage)

A single test asserting the bridge wire shape matches on both sides (ties to BUG-8):
- Define the protocol once (JSON Schema in `docs/` or generated types).
- C# test: serialize `BridgeMessage`/`BridgeResponse` → validate against the schema.
- TS test: validate the same fixtures → fail if a field drifts.
- Add `protocolVersion` and assert both sides agree.

---

## E2E smoke (few, high-value)

A scripted run against a built server + a sample vault (the public demo vault from
[08](./08-monetization-and-sponsorship.md) is perfect here):
- `ping` → `[online]`; `get_vault_stats`; `search_notes`; `create_note` + `read_note`.
- Run on each release in CI before publishing artifacts — catches "the binary doesn't start" class of
  failures the unit tests can't.

---

## CI gates & tooling

| Gate | Now | Target |
|------|-----|--------|
| `dotnet test` | ✅ runs | keep; add the new suites |
| Coverage report | ❌ | wire `coverlet` → upload to **Codecov**; show a badge |
| Coverage threshold | ❌ | start at a realistic floor (e.g. 35%) and ratchet up per release |
| Plugin tests | ❌ | add `vitest` to the `build-plugin` job |
| Contract test | ❌ | run on every PR (fast) |
| E2E smoke | ❌ | run in `release-please` before attaching artifacts |
| Perf benchmark | ❌ | `BenchmarkDotNet` for index build + cosine; track regressions |

---

## Suggested order

1. `VaultFixture` + write-tool round-trip + **path-traversal regression** (unblocks BUG-1 safely).
2. Mocked-Ollama embedding/search integration tests + graceful degradation.
3. Vitest plugin handler tests (unblocks BUG-6/7).
4. Protocol contract test + `protocolVersion` (unblocks BUG-8).
5. Coverage reporting + a low threshold, then HTTP/auth tests, then E2E smoke + benchmarks.

---

## Manual QA checklist (until E2E matures)

- Fresh install on Linux/macOS/Windows: server starts, indexes a sample vault, `search_notes` works.
- Ollama present vs absent: semantic search works / degrades cleanly.
- Plugin: enable, restart bridge, port-conflict shows a `Notice`, `open_note_in_obsidian` focuses the
  right note, deps-missing shows friendly errors.
- HTTP mode behind nginx: `/health` open, `/mcp` requires token, SSE streams without buffering.

---

## Implementation Status

| # | Item | Branch | PR | Status |
|---|------|--------|----|--------|
| 1 | VaultFixture (P0) | `test/p0-vault-fixture` | [#62](https://github.com/sandovaldavid/kioku/pull/62) | done |
| 2 | Path-traversal regression tests (P0) | `test/p0-path-traversal-tests` | [#63](https://github.com/sandovaldavid/kioku/pull/63) | done |
| 3 | Plugin Vitest tests (P1) | — | — | pending |
| 4 | Protocol contract test (P1) | — | — | pending |
| 5 | Coverage gate (P1) | — | — | pending |
