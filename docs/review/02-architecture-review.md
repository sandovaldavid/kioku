# 02 — Architecture Review

How well the implementation matches the architecture declared in `docs/planning.md`, where it has
drifted, and an assessment of the config-v2 work in progress.

> Severity: 🔴 high · 🟡 medium · 🟢 low · ✅ compliant

---

## 1. The intended architecture (from `planning.md`)

A **two-component, local-first** design:

```
AI agent ── stdio | HTTP-SSE ──▶ Kioku.Mcp.Server (C# .NET 10)
                                   ├─ Tool classes ([McpServerToolType])
                                   ├─ Services (VaultIndex, Embedding/Ollama, HybridSearch, …)
                                   └─ WebSocket client ──▶ :7765 ──▶ Obsidian plugin (thin client)
                                                                       └─ Obsidian App
```

Stated principles:

1. **Server does all the heavy lifting**; the plugin is a *thin client* (no indexing, no heavy work).
2. **Total decoupling** — everything works with Obsidian closed (server reads `.md` from disk).
3. **On-demand startup** — server launches when the agent invokes it; no system service for local use.
4. **Self-Contained** binary now; **AOT-ready** later (avoid reflection-heavy deps).
5. **CQRS-ish tool split** — Query vs Command vs Bridge.
6. **Community-Store-compliant plugin** from day one.

---

## 2. Adherence scorecard

| Principle | Status | Notes |
|-----------|:------:|-------|
| Heavy lifting in server / thin plugin | ✅ | Plugin only bridges UI actions; all indexing/search/embeddings are server-side |
| Works with Obsidian closed | ✅ | File-based read/write; bridge tools degrade with a clear error |
| On-demand startup (stdio) | ✅ | stdio is default; HTTP-SSE opt-in via `--http`/`KIOKU_TRANSPORT` |
| Self-Contained build | ✅ | `.csproj` sets `SelfContained`+`PublishSingleFile`; 4 RIDs incl. macOS |
| AOT-readiness | 🟡 | `TrimmerRootDescriptor` present, but Markdig + YamlDotNet are reflection-heavy (§5) |
| CQRS tool split | ✅🟡 | Query/Command/Bridge classes exist, but the surface grew to **17** classes / **119** tools |
| Tool response prefixes, no emojis | ✅ | `[ok]/[error]/[loading]/[info]/[online]` used consistently |
| Logging via `ILogger<T>` / `log.*` | ✅ | One pre-DI `Console.Error` (`Program.cs:17`); plugin uses `log.*` |
| Plugin Community-Store-compliant | 🟡 | Clean lifecycle & manifest, but uses some `as unknown as` internal-API casts |
| Single source of truth for the bridge protocol | 🔴 | Duplicated in C# and TS (see §4) |

Overall: the architecture is **respected in spirit and in most of the letter**. The drift is in
*documentation* and in two cross-cutting concerns (protocol single-source, AOT dependency hygiene).

---

## 3. Documentation ↔ code drift 🟡

The code has outrun the docs. Concrete mismatches:

| Claim | Where claimed | Reality | 
|-------|---------------|---------|
| "~85 tools / 16 categories" | `README.md:7,336` | **119 tools / 17 classes** (verified `grep`) |
| "18 MCP tools" | `AGENTS.md:8` | Same — `AGENTS.md` documents only the original v1 set |
| Version 1.6.2 | `README.md:5` | **1.8.0-beta.1** (`.csproj`, `manifest.json`) |
| "YamlDotNet ❌ replaced by manual parser" | `planning.md:356` | **YamlDotNet 18.0.0 is referenced & used** (`VaultConfigService`) |
| "Markdig ❌ replaced by manual extractor" | `planning.md:357` | **Markdig 0.38.0 is referenced** (`.csproj:38`) |
| Plugin source = single `main.ts` | `planning.md:140`, `AGENTS.md` | Refactored into `main.ts`+`bridge.ts`+`handlers.ts`+`types.ts` |
| `delete_note` "proposed" | `commands-reference.md` | Implemented (`NoteCommandTools`) |
| config-v2 / `VaultConfigService` | not in `planning.md` | Implemented + active branch |

**Why it matters:** AGENTS.md is the file agents read to understand the tool surface; under-reporting
by 100 tools means agents (and contributors) don't discover most of the capability. The README is the
storefront; a stale version and tool count read as "abandoned" to a prospective user/sponsor.

**Recommendation:** make `commands-reference.md` the single generated source of truth (emit it from
the `[McpServerTool]`/`[Description]` attributes at build time), and have README/AGENTS link to it
rather than restate counts. See [07](./07-production-readiness.md) for the doc-reconciliation task.

---

## 4. Bridge protocol has no single source of truth 🔴 (design-level)

`BridgeMessage`/`BridgeResponse` are declared **twice** — `ObsidianBridgeService.cs:249-270` and
`types.ts:40-53` — and kept in sync by hand. This is the one place the two-component architecture
lacks a contract. It works today, but it's the most likely source of a future "the bridge silently
stopped working" bug.

**Options (cheapest first):**
1. Add `protocolVersion` to both records + a startup handshake that warns on mismatch.
2. A JSON-Schema file in `docs/` that both sides validate against in tests (contract test).
3. Generate one side from the other (e.g. C# records → TS via a small build step).

See BUG-8 in [01](./01-diagnosis-and-bugs.md) and the contract test in [06](./06-testing-strategy.md).

---

## 5. AOT-readiness vs current dependencies 🟡

`planning.md` makes AOT a v4 goal and prescribes avoiding reflection-heavy libraries. The code has
since added **YamlDotNet** (config parsing) and retained **Markdig** — both use reflection and are
not AOT-friendly out of the box. This is a reasonable trade (config-v2 needs real YAML; a hand parser
for arbitrary user YAML is risky), but it means:

- The "AOT path" is now a **decision**, not a default. Either (a) keep Self-Contained and drop the AOT
  goal, or (b) isolate YAML/Markdown behind interfaces and provide AOT-safe implementations.
- Recommendation: **keep Self-Contained as the supported build**; treat AOT as an experiment, not a
  promise. Update `planning.md` to reflect that YamlDotNet/Markdig are in use and why. Startup is
  already ~200ms, which is fine for an on-demand tool.

---

## 6. Tool-surface sprawl (a design smell, not a bug) 🟡

119 tools is a lot for one MCP server. Benefits: capability breadth. Costs: (a) every tool's
`[Description]` is tokens the agent loads each session — a large surface inflates context cost, the
very thing Kioku exists to reduce; (b) discoverability and testing burden.

**Recommendations:**
- Consider **tool namespacing / capability groups** the user can enable (e.g. `git`, `css`, `assets`
  off by default) via config-v2, so a research-focused vault loads ~30 tools, not 119.
- Audit for **near-duplicate tools** (e.g. `move_note` vs `move_note_to_folder`,
  `suggest_folder` vs `reclassify_note`) and consolidate.
- Measure the token cost of the tool manifest and report it (it's a marketing number too — "Kioku
  pays for itself in saved tokens").

---

## 7. config-v2 assessment (current branch) 🟢

`VaultConfigService` reads `.kioku/config.yml` and exposes `GetFolder`, `GetDomainForFolder`
(longest-prefix match), `GetDefaults`, `ExcludeFolders`, `GetInheritedTags`, and now `GetTemplate`.
`NoteHelpers.ExpandTemplateVariables` (`:162`) does `{{var}}` substitution. This is a **good
direction** — it turns Kioku from "opinionated" into "adapts to *your* vault's taxonomy," which is
essential for the second-brain use case.

Polish items (none blocking):
- `ExpandTemplateVariables` is case-insensitive and does naive `string.Replace`; define the rules
  (case sensitivity, what happens to unmatched `{{x}}`, escaping) and unit-test them.
- Graceful fallback to an empty config on parse error is correct, but a **malformed** `config.yml`
  should emit one clear warning (not silently behave as if unconfigured).
- Document the schema (`docs/vault-config.example.yml` exists — link it from README and validate it
  in a test).

---

## 8. Strengths worth protecting

- **Graceful degradation** (Ollama offline → keyword search still works) is implemented and is a key
  selling point — keep it covered by tests.
- **Self-Contained, cross-platform** release pipeline is genuinely production-grade.
- **The plugin refactor** (bridge/handlers/types) is clean, single-responsibility, and well-typed —
  exactly the structure that makes the protocol single-source (§4) cheap to add.

---

## Implementation Status

| Item | Task | Branch | PR | Status |
|------|------|--------|----|--------|
| §3 Doc drift | Doc reconciliation | `fix/p0-doc-reconciliation` | [#61](https://github.com/sandovaldavid/kioku/pull/61) | merged |
| §4 Protocol SSoT | Protocol version handshake + contract test | `fix/p1-protocol-version`, `feat/p1-plugin-vitest-tests` | [#71](https://github.com/sandovaldavid/kioku/pull/71), [#86](https://github.com/sandovaldavid/kioku/pull/86) | merged |
| §5 AOT decision | Update planning.md | `fix/p0-doc-reconciliation` | [#61](https://github.com/sandovaldavid/kioku/pull/61) | merged |
| §6 Tool sprawl | Tool namespacing (P2) | — | — | pending |
| §7 config-v2 polish | Config-v2 polish (P2) | — | — | pending |
