# 04 — Obsidian Plugin Improvements

Improvements to `src/obsidian-kioku-mcp`. The recent refactor (`main.ts` → `bridge.ts` +
`handlers.ts` + `types.ts`) is clean and well-typed; these build on that foundation. The plugin is a
**thin client** by design — keep it that way (no indexing, no heavy work).

> Tags: 🐞 correctness · 🔒 safety · 🎛️ UX · 🧩 capability · 📦 distribution

---

## Correctness & safety 🐞🔒

1. **Restore startup-failure `Notice` (P0).** `bridge.ts:52` only logs on `wss` error. Surface a
   `Notice` (gated by `settings.showNotifications`), especially for `EADDRINUSE` — the #1 silent
   failure (BUG-5).
2. **`open-file` async/await (P1).** Make `cmdOpenFile` async and `await openLinkText` in try/catch;
   report real `success` (BUG-6).
3. **Payload validation (P1).** Replace blind `as { … }` casts with small guards that return clean
   `{ success:false, error:"missing field: <x>" }` (BUG-7). A 30-line `validate(payload, ["path"])`
   helper covers every handler.
4. **`get-app-version` from `Plugin.manifest`.** Thread the manifest from `main.ts` instead of the
   registry lookup (BUG-10).
5. **Document the internal-API casts.** `as unknown as KiokuApp` / `KiokuDataAdapter`
   (`types.ts:27`, `handlers.ts`) use undocumented Obsidian internals. Add TSDoc explaining each and a
   runtime null-check before use, so an Obsidian update degrades gracefully instead of throwing. (The
   Community Store reviews for exactly this.)

---

## Protocol 🧩

6. **Single source of truth (P1).** Share/version `BridgeMessage`/`BridgeResponse` with the C# server
   (BUG-8). Add `protocolVersion` and a startup handshake; if the server's version is newer than the
   plugin's, show one `Notice` ("update the Kioku plugin"). This prevents the worst class of
   "it just stopped working" reports.
7. **Request timeouts on the plugin side.** The server enforces a 10s RPC timeout; the plugin should
   also bound long-running handlers (e.g. a huge Dataview query) and return a timeout error rather
   than hanging a request slot.

---

## UX 🎛️

8. **Connection status in the UI.** A status-bar item (●/○) showing bridge state + connected client
   count + port. Cheap, and turns "is it working?" into a glance. (Settings tab already exists at
   `main.ts:56` — extend it.)
9. **Settings: health check button.** A "Test connection" button that pings the server and reports
   round-trip status; surfaces port conflicts and Ollama availability in one place.
10. **First-run guidance.** On enable, if the server isn't reachable, a one-time notice linking to
    setup docs. Reduces the support burden for non-technical researchers.
11. **Optional command palette entries.** "Kioku: restart bridge", "Kioku: copy connection config"
    (emits the MCP client JSON snippet pre-filled with the vault path) — removes a manual copy/paste
    step from onboarding.

---

## Capability 🧩

12. **Graceful plugin-dependency UX.** Dataview/Templater/Linter handlers already guard for
    "not installed" ✅. Extend with a `get-installed-plugins` driven capability report so the server
    can tell the agent up front which bridge features are available.
13. **Live change push (server → plugin).** `planning.md` envisions the server pushing UI updates
    (open the note it just edited, refresh a view). The bridge is request/response today; add a
    server-initiated event channel for "reveal/refresh" so edits feel live when Obsidian is open.
14. **Selection/context tools for teaching.** "Get current selection + surrounding heading" and
    "insert callout/admonition at cursor" are high-value for live note-taking and lecturing.

---

## Distribution 📦 (also see [07](./07-production-readiness.md))

15. **BRAT support.** Ensure `manifest.json` + a GitHub release with `main.js`/`manifest.json`/
    `styles.css` lets users install via BRAT (Beta Reviewer's Auto-update Tool) — the standard path
    for pre-store Obsidian plugins. The release workflow already builds the ZIP; verify the asset
    layout BRAT expects (raw files on a tagged release).
16. **Community Store submission checklist.** desktop-only ✅, lifecycle ✅, no telemetry ✅. To pass
    review: remove/justify undocumented-API casts (#5), confirm no network calls beyond localhost,
    add a `fundingUrl` to `manifest.json` (enables the Sponsor button — see
    [08](./08-monetization-and-sponsorship.md)), and provide screenshots + a clear README.
17. **`styles.css` audit.** Keep styles scoped (`.kioku-*`) so the plugin never bleeds into user
    themes — a common Community Store rejection reason.

---

## Testing (detailed in [06](./06-testing-strategy.md))

18. **Zero tests today.** Add Vitest: unit-test each handler against a mocked Obsidian `App`, and a
    **protocol contract test** that asserts the plugin's `BridgeResponse` shape matches the C#
    schema. This is the natural place to lock #3 and #6.

---

## Suggested sequencing

| Wave | Items |
|------|-------|
| **A (with P0)** | 1 |
| **B** | 2, 3, 4, 6, 18 |
| **C** | 8, 9, 10, 11, 15, 16 |
| **D** | 5, 7, 12, 13, 14, 17 |

---

## Implementation Status

| # | Item | Branch | PR | Status |
|---|------|--------|----|--------|
| 1 | Restore startup-failure Notice (P0) | `fix/p0-bridge-notice` | [#60](https://github.com/sandovaldavid/kioku/pull/60) | merged |
| 2 | open-file async/await (P1) | `fix/p1-open-file-async` | [#67](https://github.com/sandovaldavid/kioku/pull/67) | merged |
| 3 | Payload validation (P1) | `fix/p1-payload-validation` | [#69](https://github.com/sandovaldavid/kioku/pull/69) | merged |
| 6 | Protocol version handshake (P1) | `fix/p1-protocol-version` | [#71](https://github.com/sandovaldavid/kioku/pull/71) | merged |
| 15 | BRAT support (P0) | `feat/p0-brat-support` | [#64](https://github.com/sandovaldavid/kioku/pull/64) | merged |
| 16 | Community Store checklist (P1) | `feat/p1-community-store-ready` | [#78](https://github.com/sandovaldavid/kioku/pull/78) | merged |
| 18 | Plugin Vitest tests (P1) | `feat/p1-plugin-vitest-tests` | [#86](https://github.com/sandovaldavid/kioku/pull/86) | merged |
