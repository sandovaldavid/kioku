# P1-04 — Token-based authentication for the WebSocket bridge

| Field | Value |
|---|---|
| Priority | P1 |
| Branch | `feat/bridge-auth-token` |
| Commit | `feat(server): add optional shared-token auth to the obsidian bridge` (server) + plugin changes in the same PR |
| Size | M |
| Spec | [features/04-bridge-auth-token.md](../features/04-bridge-auth-token.md) |
| Dependencies | Recommended after [P1-05](P1-05-http-and-bridge-coverage.md) (bridge coverage before changing the protocol) |

## Objective

Optional shared token for the WebSocket on 7765: `authToken` setting in the plugin +
`KIOKU_BRIDGE_TOKEN` in the server, with a backward-compatible `auth` handshake
(PROTOCOL_VERSION 2) — with no token configured, everything works as today.

## Scope

- Plugin: setting + "Generate" button, `auth` command in `handlers.ts`, rejection (close
  4401) of unauthenticated connections when a token is set, `timingSafeEqual`.
- Server: new env var, authentication after each connection **and each reconnection** in
  `ObsidianBridgeService`, `KiokuError.Unauthorized` in bridge tools if auth fails.
- `protocol-schema.json` + `PROTOCOL_VERSION = 2` on both sides.

## Acceptance criteria

- [ ] Tested matrix (tests + manual): no token on either side ✓ · correct token ✓ · wrong
  token → connection closed and tools return `[error] [UNAUTHORIZED]` · plugin with
  token + server without token → same · reconnection re-authenticates on its own.
- [ ] v1 client (no `auth`) against a tokenless plugin still works (backward compatibility).
- [ ] Protocol contract tests updated (`protocol.contract.test.ts`) and green.
- [ ] Docs updated in the same PR: `install.md`, `troubleshooting.md`, env var tables
  (root README, server README, `.mcp/server.json`), `docs/features/04` marked done.
- [ ] Build/lint/tests green for both projects.

## Files

- `src/obsidian-kioku-mcp/src/{types,bridge,handlers,main}.ts`, `protocol-schema.json`, tests
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs`,
  `src/Kioku.Mcp.Server/Services/ObsidianBridgeService.cs`
- Docs listed above
