# 04 — WebSocket bridge authentication

> Area: server + plugin · Task: [P1-04](../tasks/P1-04-bridge-auth-token.md) · Impact ★★ · Effort M

## Motivation

The plugin's WebSocket listens on `127.0.0.1:7765` **without authentication**: any local
process can connect and run the bridge's 22 commands (open files, insert text, run
arbitrary Obsidian commands via `trigger-command`). The README states this as a known
limitation. An optional shared token closes that vector without breaking existing
installations, and is a reasonable requirement before publishing to the Community Store.

## Design

### Optional shared token

- **Plugin**: new setting `authToken: string` (default `""` = no auth) in
  `KiokuSettings`, with a "Generate" button in the settings tab (32 random bytes as hex
  via `crypto`).
- **Server**: new env var `KIOKU_BRIDGE_TOKEN` in `KiokuConfiguration.FromEnvironment()`.

### Handshake (PROTOCOL_VERSION = 2, backward compatible)

1. On connect, the C# client sends as the first message:
   `{command: "auth", payload: {token}, protocolVersion: 2, requestId}`.
2. Plugin with `authToken` configured:
   - Correct `auth` → `{success: true}`; the connection becomes authenticated.
   - Any other command before authenticating, or an invalid token → `{success: false,
     error: "[error] [UNAUTHORIZED] ..."}` and **the connection is closed** (code 4401).
3. Plugin without `authToken` (default): accepts connections as it does today; the
   `auth` command responds `{success: true}` (no-op). v1 clients keep working →
   **no breaking change**.
4. Constant-time token comparison (`crypto.timingSafeEqual`).

`PROTOCOL_VERSION` bumps to 2 in `types.ts` and `ObsidianBridgeService.BridgeProtocol`;
the existing `onProtocolMismatch` mechanism keeps warning about version mismatches.

### Error UX

- Server without a token against a plugin with a token → log `[error] [UNAUTHORIZED]
  Bridge requires KIOKU_BRIDGE_TOKEN` and the bridge tools return
  `KiokuError.Unauthorized`.
- Optional notice in the plugin when a connection is rejected (respecting
  `showNotifications`).

## Affected files

- `src/obsidian-kioku-mcp/src/types.ts` (`PROTOCOL_VERSION`, `KiokuSettings`)
- `src/obsidian-kioku-mcp/src/bridge.ts` (per-connection authenticated state, 4401 close)
- `src/obsidian-kioku-mcp/src/handlers.ts` (`auth` command)
- `src/obsidian-kioku-mcp/src/main.ts` (setting + generator)
- `src/obsidian-kioku-mcp/src/protocol-schema.json` (`auth` command)
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs` (`KIOKU_BRIDGE_TOKEN`)
- `src/Kioku.Mcp.Server/Services/ObsidianBridgeService.cs` (auth after connect and after
  every reconnect)
- Tests: protocol contract tests (`protocol.contract.test.ts`) + unit tests for
  rejection; C# side if P1-05 has already added bridge coverage
- Docs: `install.md`, `troubleshooting.md`, root and server README (env vars table),
  `.mcp/server.json`

## Risks

- **Reconnection**: `ObsidianBridgeService` reconnects automatically — it must
  re-authenticate on every new connection (include in tests).
- Env var docs drift (already known) — update every table in the same PR.
- The token travels in cleartext over localhost; this is acceptable (same model as
  `KIOKU_API_KEY` over local HTTP). Document that it doesn't protect against processes
  with access to the plugin's config.
