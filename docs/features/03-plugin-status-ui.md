# 03 — Plugin status UI (status bar + commands)

> Area: plugin · Task: [P1-03](../tasks/P1-03-plugin-status-ui.md) · Impact ★★ · Effort S

## Motivation

The plugin runs a long-lived WebSocket server but has **no visual status signal** at
all: no status bar, no ribbon, no indicator of connected clients. Today the only way to
know whether the bridge is alive is to open the developer console. Only existing command:
`kioku-restart-bridge`.

## Design

### Status bar (primary)

`main.ts` registers a status bar item (`addStatusBarItem()`):

- `[online] Kioku :7765 (1)` — bridge listening, 1 client connected
- `[online] Kioku :7765` — listening, no clients
- `[offline] Kioku` — bridge stopped or startup error (e.g. port in use)

No emojis (repo rule); `[online]`/`[offline]` prefixes + CSS class `.kioku-status`
(variants `.kioku-status-online` / `.kioku-status-offline` in `styles.css`). Clicking
the item → runs the restart command.

### Changes in `bridge.ts`

`BridgeServer` exposes what the UI needs:

- `get clientCount(): number` (size of the existing client set)
- `get isRunning(): boolean`
- `onClientConnected` / `onClientDisconnected` / `onStateChange` callbacks that
  `main.ts` uses to refresh the status bar (same pattern as the current
  `onStartupError` / `onProtocolMismatch`).

### New commands

| ID | Name | Action |
|---|---|---|
| `kioku-stop-bridge` | Stop Kioku MCP Bridge | `bridge.stop()` + status refresh |
| `kioku-start-bridge` | Start Kioku MCP Bridge | `bridge.start()` + refresh |
| `kioku-copy-status` | Copy Kioku bridge status | Copies JSON `{running, port, clients, protocolVersion, pluginVersion}` to the clipboard for bug reports |

(`kioku-restart-bridge` is kept.)

### New setting

- `showStatusBar: boolean` (default `true`) in `KiokuSettings` + toggle in `KiokuSettingTab`.

## Affected files

- `src/obsidian-kioku-mcp/src/main.ts` (status bar, commands, setting)
- `src/obsidian-kioku-mcp/src/bridge.ts` (getters + callbacks)
- `src/obsidian-kioku-mcp/src/types.ts` (`KiokuSettings`, `DEFAULT_SETTINGS`)
- `src/obsidian-kioku-mcp/styles.css` (`.kioku-status*`)
- `src/obsidian-kioku-mcp/src/__mocks__/obsidian.ts` + tests (mock for `addStatusBarItem`)

## Risks

- Low. Watch the lifecycle: clean up callbacks and the item in `onunload` (a
  Community Store requirement). `main.ts` is excluded from coverage — move the status
  formatting logic into a pure, testable function (e.g. in `types.ts` or a new
  `status.ts`).
