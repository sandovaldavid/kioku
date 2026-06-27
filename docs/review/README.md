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
