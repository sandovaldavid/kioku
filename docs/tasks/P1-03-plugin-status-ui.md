# P1-03 — Status bar and bridge control commands (plugin)

| Field | Value |
|---|---|
| Priority | P1 |
| Branch | `feat/plugin-status-ui` |
| Commit | `feat(plugin): add bridge status bar item and control commands` |
| Size | S |
| Spec | [features/03-plugin-status-ui.md](../features/03-plugin-status-ui.md) |
| Dependencies | None |

## Objective

Give visibility into the bridge's state without opening the console: a status bar item
(`[online] Kioku :7765 (1)` / `[offline] Kioku`), `start`/`stop`/`copy-status` commands
alongside the existing `restart`, and a `showStatusBar` setting.

## Scope

- `bridge.ts`: `isRunning`/`clientCount` getters + `onClientConnected`/
  `onClientDisconnected`/`onStateChange` callbacks.
- `main.ts`: status bar item (click = restart), 3 new commands, setting + toggle.
- `types.ts`: `KiokuSettings.showStatusBar` (default `true`); pure status-formatting
  function (testable) outside `main.ts`.
- `styles.css`: `.kioku-status`, `.kioku-status-online`, `.kioku-status-offline`.
- No emojis; `[online]`/`[offline]` prefixes (repo rule).

## Acceptance criteria

- [ ] The status bar reflects live: startup, stop, port-in-use error, and connection/
  disconnection of the C# server (test manually with Obsidian + server).
- [ ] `kioku-copy-status` copies JSON with `{running, port, clients, protocolVersion, pluginVersion}`.
- [ ] With `showStatusBar=false` the item isn't shown (and clears live when the toggle
  changes).
- [ ] `onunload` cleans up the item, callbacks and server (no orphaned listeners).
- [ ] Tests: status-formatting function + `BridgeServer` callbacks
  (`handlers.test.ts`/new `status.test.ts`); `pnpm --filter obsidian-kioku-mcp test` green.
- [ ] `pnpm lint:plugin`, `format:check` and `tsc --noEmit` green.

## Files

- `src/obsidian-kioku-mcp/src/{main,bridge,types}.ts`, `styles.css`
- `src/obsidian-kioku-mcp/src/__mocks__/obsidian.ts`, tests
