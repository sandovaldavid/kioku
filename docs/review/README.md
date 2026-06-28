# Kioku — Project Review (2026-06-27)

A full review of the Kioku MCP ecosystem: bugs, architecture adherence, improvement and
integration ideas, a path to production, a testing strategy, and a monetization/sponsorship plan.

This review was produced against branch `feat/config-v2-templates` (HEAD `b513a8e`, ahead of
`develop` @ `1.8.0-beta.1`). It is **diagnostic only** — no code was changed. All issues are
recorded here as a prioritized backlog.

> Severity legend: 🔴 high · 🟡 medium · 🟢 low · ✅ strength / compliant

---

## Documents

| # | Document | What it covers |
|---|----------|----------------|
| — | [README.md](./README.md) | This index + executive summary + scorecard |
| 01 | [01-diagnosis-and-bugs.md](./01-diagnosis-and-bugs.md) | Bug & issue inventory with severity, `file:line`, impact, fix sketch |
| 02 | [02-architecture-review.md](./02-architecture-review.md) | Adherence to the intended architecture; protocol & doc/code drift |
| 03 | [03-mcp-server-improvements.md](./03-mcp-server-improvements.md) | Server hardening, robustness, performance, config-v2 polish |
| 04 | [04-plugin-improvements.md](./04-plugin-improvements.md) | Plugin quality, protocol sharing, Community Store / BRAT readiness |
| 05 | [05-feature-roadmap.md](./05-feature-roadmap.md) | Research / study / teaching feature roadmap & integrations |
| 06 | [06-testing-strategy.md](./06-testing-strategy.md) | Test pyramid, coverage gaps, concrete test backlog, CI gates |
| 07 | [07-production-readiness.md](./07-production-readiness.md) | Distribution, packaging, observability, security, QA matrix |
| 08 | [08-monetization-and-sponsorship.md](./08-monetization-and-sponsorship.md) | Positioning, open-core, revenue, sponsorship, go-to-market |

---

## Executive summary

Kioku is a **genuinely strong, well-engineered local-first product**. The core idea — a fast C#
MCP server that talks to the vault on disk, uses local **Ollama** embeddings for semantic search,
and offloads cheap/repetitive knowledge-work from the cloud agent — is exactly right for a
privacy-conscious "second brain" used in research, study, and teaching. The engineering bar is
high: thread-safe in-memory indices, graceful degradation when Ollama is offline, a clean dual
transport (stdio + HTTP-SSE), mature CI with cross-platform self-contained binaries, and a recently
refactored, well-typed plugin.

The gaps are the ones you'd expect from a fast-moving solo project approaching "1.0": a couple of
real correctness/security bugs, thin automated test coverage, distribution friction (no Docker / no
package managers / not in the Obsidian Community Store), and documentation that has drifted behind
the code. None are structural — they are a focused, finite checklist away from a confident public
release.

### What to do first (P0)

1. **Fix the path-traversal** in `move_note` / `rename_note` (see [01](./01-diagnosis-and-bugs.md), 🔴).
2. **Reconcile the docs**: tool count is **119 across 17 classes** (verified) — not "~85" (README)
   or "18" (AGENTS.md); README version says 1.6.2, code is 1.8.0-beta.1.
3. **Restore the bridge-startup error `Notice`** in the plugin (silent failure regression).
4. **Add the first integration tests** over a fixture vault (read/create/move/search) so the 🔴/🟡
   fixes can land safely.
5. **Ship a frictionless install path**: Docker image + `dotnet tool` + BRAT — this is the single
   biggest lever for adoption.

---

## Production-readiness scorecard

| Dimension | Score | One-line rationale |
|-----------|:-----:|--------------------|
| Architecture & design | 9/10 | Clean two-component split, dual transport, graceful degradation, on-demand server |
| Code quality | 7/10 | Idiomatic, consistent conventions; a few correctness bugs and unguarded edges |
| Security | 6/10 | WS is localhost-only ✅; but vault path-traversal 🔴 and no auth rate-limiting |
| Test coverage | 3/10 | Only utility units (~5–10%); zero plugin tests; no integration/HTTP/Ollama tests |
| Documentation | 6/10 | Comprehensive but drifted (versions, tool counts, removed-deps that are still present) |
| CI/CD | 8/10 | Cross-platform binaries, dual stable/beta track, husky/commitlint; no coverage gate |
| Distribution | 5/10 | GitHub release artifacts only; no Docker / brew / winget / BRAT / Community Store |
| Observability | 4/10 | Good structured logging to stderr; no metrics, telemetry, or error tracking |
| **Overall** | **6/10** | **Solid beta — a finite checklist from a confident 1.0** |

---

## Verified facts (used across these docs)

| Fact | Value | Source |
|------|-------|--------|
| MCP tools | **119** `[McpServerTool]` methods | `grep` over `src/Kioku.Mcp.Server/Tools/` |
| Tool classes | **17** `[McpServerToolType]` | same |
| Version (code) | **1.8.0-beta.1** | `.csproj` + `manifest.json` |
| Transports | stdio (default) + HTTP-SSE (`--http` / `KIOKU_TRANSPORT=http`), HTTP binds `localhost` | `Program.cs:22,126` |
| Server deps | Markdig 0.38.0, YamlDotNet 18.0.0, ModelContextProtocol(.AspNetCore) 1.4.0 | `.csproj:38-42` |
| Plugin WS host | `127.0.0.1` only ✅ | `bridge.ts:19` |
| Plugin id / min Obsidian | `kioku-mcp` / 1.4.0, desktop-only | `manifest.json` |
| Tests | xUnit; 4 files (parsers/helpers/tasks); **0** plugin tests | `src/Kioku.Mcp.Server.Tests/`, plugin `package.json` |

> Note: the per-class tool counts here differ slightly from the first exploration pass; the numbers
> in this review are the **verified** ones from a direct `grep` of `[McpServerTool]`.

---

## Implementation Progress

### Phase P0 — Safe Beta (before any wider release)

| # | Task | Branch | PR | Status |
|---|------|--------|----|--------|
| 1 | BUG-1: Vault path-traversal containment | `fix/p0-path-traversal` | [#59](https://github.com/sandovaldavid/kioku/pull/59) | merged |
| 2 | BUG-5: Restore bridge-startup Notice | `fix/p0-bridge-notice` | [#60](https://github.com/sandovaldavid/kioku/pull/60) | merged |
| 3 | Doc reconciliation (README, AGENTS, planning) | `fix/p0-doc-reconciliation` | [#61](https://github.com/sandovaldavid/kioku/pull/61) | merged |
| 4 | VaultFixture test infrastructure | `test/p0-vault-fixture` | [#62](https://github.com/sandovaldavid/kioku/pull/62) | merged |
| 5 | Path-traversal regression tests | `test/p0-path-traversal-tests` | [#63](https://github.com/sandovaldavid/kioku/pull/63) | merged |
| 6 | BRAT support for plugin | `feat/p0-brat-support` | [#64](https://github.com/sandovaldavid/kioku/pull/64) | merged |

**Progress: 6/6 tasks complete**

### Phase P1 — Public 1.0

| # | Task | Branch | PR | Status |
|---|------|--------|----|--------|
| 7 | BUG-2: Reindex exception handling | `fix/p1-reindex-errors` | [#68](https://github.com/sandovaldavid/kioku/pull/68) | merged |
| 8 | BUG-3: Embedding cache stamp + invalidation | `fix/p1-embedding-cache-stamp` | [#70](https://github.com/sandovaldavid/kioku/pull/70) | merged |
| 9 | BUG-6: open-file async/await | `fix/p1-open-file-async` | [#67](https://github.com/sandovaldavid/kioku/pull/67) | merged |
| 10 | BUG-7: Payload validation layer | `fix/p1-payload-validation` | [#69](https://github.com/sandovaldavid/kioku/pull/69) | merged |
| 11 | BUG-8: Protocol version handshake | `fix/p1-protocol-version` | [#71](https://github.com/sandovaldavid/kioku/pull/71) | merged |
| 12 | Plugin Vitest test suite | `feat/p1-plugin-vitest-tests` | [#86](https://github.com/sandovaldavid/kioku/pull/86) | merged |
| 13 | Bridge protocol contract test | `feat/p1-plugin-vitest-tests` | [#86](https://github.com/sandovaldavid/kioku/pull/86) | merged |
| 14 | Coverage gate (coverlet + Codecov) | `feat/p1-coverage-gate` | [#75](https://github.com/sandovaldavid/kioku/pull/75) | merged |
| 15 | Docker image + docker-compose | `feat/p1-docker-image` | [#76](https://github.com/sandovaldavid/kioku/pull/76) | merged |
| 16 | dotnet tool publish to NuGet | `feat/p1-dotnet-tool-publish` | [#77](https://github.com/sandovaldavid/kioku/pull/77) | merged |
| 17 | Community Store readiness | `feat/p1-community-store-ready` | [#78](https://github.com/sandovaldavid/kioku/pull/78) | merged |
| 18 | Soft-delete / trash routing | `feat/p1-soft-delete-trash` | [#79](https://github.com/sandovaldavid/kioku/pull/79) | merged |
| 19 | SECURITY.md + install/troubleshooting docs | `feat/p1-security-docs` | [#82](https://github.com/sandovaldavid/kioku/pull/82) | merged |
| 20 | Generated commands-reference.md | `feat/p1-generated-commands-ref` | [#81](https://github.com/sandovaldavid/kioku/pull/81) | merged |
| 21 | Dependency scanning in CI | `feat/p1-dep-scanning` | [#80](https://github.com/sandovaldavid/kioku/pull/80) | merged |

**Progress: 15/15 tasks complete**

### Phase P2 — Scale & Polish

| # | Task | Branch | PR | Status |
|---|------|--------|----|--------|
| 22 | Operational counters (ping/get_index_status) | `feat/p2-operational-counters` | [#89](https://github.com/sandovaldavid/kioku/pull/89) | merged |
| 23 | Consistent error taxonomy | `feat/p2-error-taxonomy` | [#91](https://github.com/sandovaldavid/kioku/pull/91) | merged |
| 24 | Embedding model registry | `feat/p2-embedding-model-registry` | [#93](https://github.com/sandovaldavid/kioku/pull/93) | merged |
| 25 | Config-v2 polish (ExpandTemplateVariables + built-ins + malformed warning) | `feat/p2-config-v2-polish` | [#95](https://github.com/sandovaldavid/kioku/pull/95) | merged |
| 26 | Pagination & limits on search/list tools | `feat/p2-pagination-limits` | [#97](https://github.com/sandovaldavid/kioku/pull/97) | merged |
| 27 | Structured tool results (optional JSON variants) | — | — | pending |
| 28 | Tool namespacing / capability groups | — | — | pending |
| 29 | Opt-in privacy-first telemetry | — | — | pending |
| 30 | Performance benchmarks (BenchmarkDotNet) | — | — | pending |
| 31 | Graceful shutdown / SIGTERM flush | — | — | pending |
| 32 | brew/winget one-line installers | — | — | pending |
| 33 | SBOM / signing | — | — | pending |
| 34 | Optional crash reporting (Sentry) | — | — | pending |

**Progress: 5/13 tasks complete**
