# 07 — Production Readiness

A phased checklist to take Kioku from "solid beta" to "anyone can install and trust it." The biggest
lever for adoption is **removing install friction**; the biggest lever for trust is **the P0
security fix + tests** from [01](./01-diagnosis-and-bugs.md) and [06](./06-testing-strategy.md).

Today's strengths: cross-platform self-contained binaries on GitHub releases, dual stable/beta
Release Please track, husky/commitlint, `PackAsTool` + `PackageType=McpServer` (dotnet-tool
distribution is *half-wired already*). Gaps: no Docker, no package managers, not in the Obsidian
Community Store, no BRAT, manual VM setup, no telemetry, drifted docs.

> Priorities: **P0** = before any wider release · **P1** = for a public 1.0 · **P2** = scale/polish

---

## 1. Distribution & packaging (the adoption lever)

| Item | Priority | Notes |
|------|:--------:|-------|
| **Publish to NuGet as an MCP server / dotnet tool** | P1 | `.csproj` already has `PackAsTool`, `PackageId=kioku-mcp-server`, `PackageType=McpServer`, and a `.mcp/server.json`. Wire `dotnet pack` + `dotnet nuget push` into the release workflow → users `dnx kioku-mcp-server` / `dotnet tool install -g`. Lowest-effort, highest-credibility win. |
| **Docker image + `docker-compose.yml`** | P1 | Bundle server (+ optional Ollama service) for one-command VM/self-host. The current systemd+nginx path is 10+ manual steps; compose collapses it. Publish to GHCR. |
| **Obsidian Community Store submission** | P1 | The standard discovery path for the plugin. Checklist in §4. |
| **BRAT support** | P0 | Lets beta users install the plugin from the GitHub repo today, before store approval. Verify release assets are raw `main.js`/`manifest.json`/`styles.css` on a tag. |
| **Homebrew tap / Scoop / winget** | P2 | `brew install kioku`, `winget install kioku` — nice once NuGet/Docker exist. |
| **One-line installer script** | P2 | `curl … | sh` that downloads the right binary, writes the MCP client config, and prints next steps. |

### Onboarding friction (today vs target)

| Path | Today | Target |
|------|-------|--------|
| Local agent (Claude/Cursor/…) | download/compile binary, hand-edit config | `dnx kioku-mcp-server` or one-line installer + copy-paste snippet |
| Plugin | `pnpm install && build`, copy 3 files, enable | BRAT now → Community Store later (one click) |
| VM / self-host | manual systemd + nginx + env | `docker compose up` |

---

## 2. Documentation reconciliation (P0 — cheap, high-trust)

From [02](./02-architecture-review.md) §3. Do these before promoting the project anywhere:
- README version 1.6.2 → current; tool count "~85" → **119**; link to a **generated**
  `commands-reference.md` instead of restating counts.
- AGENTS.md: document the full 17-class surface (or clearly mark it as a v1 quick-reference and point
  to the generated reference).
- `planning.md`: correct the "YamlDotNet/Markdig removed" claim (both are in use); add config-v2 and
  the plugin refactor; restate the AOT goal as optional.
- Add `docs/install.md` (per-OS, per-client) and `docs/troubleshooting.md` (port conflicts, Ollama
  not found, FileSystemWatcher on Linux).
- Generate `commands-reference.md` from `[McpServerTool]`/`[Description]` and **fail CI on drift**.

---

## 3. Security hardening (P0/P1)

| Item | Priority | Ref |
|------|:--------:|-----|
| Vault path containment on all write tools | **P0** | BUG-1 |
| Destructive-op `dry_run`/`confirm` + soft-delete to trash | P1 | [03](./03-mcp-server-improvements.md) §3–4 |
| Auth: rate-limit 401s; document that remote HTTP **must** use TLS + token (or Tailscale) | P1 | `docs/deploy/auth-options.md` |
| Bridge `protocolVersion` handshake | P1 | BUG-8 |
| Dependency scanning (Dependabot/`dotnet list package --vulnerable`, `pnpm audit`) in CI | P1 | — |
| Supply-chain: sign release binaries / attach checksums + SBOM | P2 | — |
| A `SECURITY.md` with a disclosure contact | P1 | — |

---

## 4. Obsidian Community Store checklist

- [x] `manifest.json` with `id`, `name`, `version`, `minAppVersion`, `author`, `authorUrl`, `description`
- [x] desktop-only declared (`isDesktopOnly: true`) — correct (uses `ws`/Node)
- [x] clean `onload`/`onunload`
- [ ] **`fundingUrl`** in `manifest.json` (enables the Sponsor button — see [08](./08-monetization-and-sponsorship.md))
- [ ] no undocumented-API usage without justification (the `as unknown as` casts — [04](./04-plugin-improvements.md) §5)
- [ ] no network calls beyond localhost (verify; the store reviews this)
- [ ] scoped CSS (`.kioku-*`), screenshots, polished README
- [ ] separate `obsidian-kioku-mcp` release tags if the store expects per-plugin versioning

---

## 5. Observability (P1/P2)

- **Opt-in, privacy-first telemetry** (off by default; documented; local-first ethos). Track: tool
  usage counts, errors, version, OS — *never* note contents. This data is what tells you which of the
  119 tools matter and what to charge for.
- Structured logs already go to stderr ✅; add a `--log-file`/`KIOKU_LOG_FILE` option and log levels.
- `get_index_status`/`ping` expose operational counters ([03](./03-mcp-server-improvements.md) §21).
- Optional crash reporting (Sentry) gated behind opt-in.

---

## 6. Reliability & data safety (P1)

- **Soft-delete / trash** for all destructive tools; `RestoreTools` already exists — wire it in.
- **Backup guidance**: the vault is the user's files (git-friendly) — document a `git`-based or
  snapshot backup workflow; Kioku's `GitTools` can drive it.
- **Embedding cache**: versioned + self-healing (BUG-3); safe to delete and rebuild.
- **Graceful shutdown**: flush pending embedding writes on SIGTERM (matters for the systemd/Docker path).

---

## 7. Cross-platform QA matrix (run before each release)

| | Windows 11 | Fedora/Ubuntu | macOS (arm64) |
|---|:---:|:---:|:---:|
| Server starts (stdio) | ☐ | ☐ | ☐ |
| Server starts (HTTP) | ☐ | ☐ | ☐ |
| Index a 500-note vault | ☐ | ☐ | ☐ |
| Semantic search w/ Ollama | ☐ | ☐ | ☐ |
| Degrade w/o Ollama | ☐ | ☐ | ☐ |
| Plugin enable + bridge | ☐ | ☐ | ☐ |
| FileSystemWatcher live updates | ☐ | ☐ (note polling fallback) | ☐ |

---

## 8. Release readiness gate (definition of "1.0")

1. ✅ P0 security fix (path containment) shipped + regression-tested.
2. ✅ Integration tests over a fixture vault green in CI; coverage reporting on.
3. ✅ Docs reconciled; `commands-reference.md` generated; install + troubleshooting docs exist.
4. ✅ Frictionless install for at least one path (dotnet tool **or** Docker) + BRAT for the plugin.
5. ✅ `SECURITY.md`, `CONTRIBUTING.md`, `LICENSE` (MIT exists), `fundingUrl` set.
6. ✅ Cross-platform QA matrix passed.

---

## Phased rollout

| Phase | Goal | Contents |
|-------|------|----------|
| **P0 — Safe beta** | Don't ship a foot-gun | Path containment, restore bridge `Notice`, fixture+traversal tests, BRAT, doc-version fix |
| **P1 — Public 1.0** | Anyone can install & trust | dotnet tool + Docker, Community Store, coverage gate, security hardening, generated docs, telemetry (opt-in) |
| **P2 — Scale** | Polish & reach | brew/winget, one-line installer, SBOM/signing, perf benchmarks, crash reporting |

---

## Implementation Status

### Phase P0 — Safe Beta

| # | Item | Branch | PR | Status |
|---|------|--------|----|--------|
| 1 | Path containment (BUG-1) | `fix/p0-path-traversal` | [#59](https://github.com/sandovaldavid/kioku/pull/59) | merged |
| 2 | Bridge Notice (BUG-5) | `fix/p0-bridge-notice` | [#60](https://github.com/sandovaldavid/kioku/pull/60) | merged |
| 3 | Doc reconciliation | `fix/p0-doc-reconciliation` | [#61](https://github.com/sandovaldavid/kioku/pull/61) | merged |
| 4 | VaultFixture | `test/p0-vault-fixture` | [#62](https://github.com/sandovaldavid/kioku/pull/62) | merged |
| 5 | Path-traversal tests | `test/p0-path-traversal-tests` | [#63](https://github.com/sandovaldavid/kioku/pull/63) | merged |
| 6 | BRAT support | `feat/p0-brat-support` | [#64](https://github.com/sandovaldavid/kioku/pull/64) | merged |

### Phase P1 — Public 1.0

| # | Item | Branch | PR | Status |
|---|------|--------|----|--------|
| 15 | Docker image | — | — | pending |
| 16 | dotnet tool NuGet | — | — | pending |
| 17 | Community Store | — | — | pending |
| 18 | Soft-delete/trash | — | — | pending |
| 19 | SECURITY.md + docs | — | — | pending |
| 20 | Generated commands-ref | — | — | pending |
| 21 | Dependency scanning | — | — | pending |
