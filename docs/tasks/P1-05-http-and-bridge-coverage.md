# P1-05 — Test coverage: HTTP transport, ApiKeyMiddleware and bridge

| Field | Value |
|---|---|
| Priority | P1 |
| Branch | `test/http-and-bridge-coverage` |
| Commit | `test(server): cover http transport, api key middleware and bridge service` |
| Size | M |
| Spec | — (gap flagged in [review/06-testing-strategy.md](../review/06-testing-strategy.md)) |
| Dependencies | None; recommended **before** P1-04 |

## Context

The ~122 current tests cover parsers/helpers/write-tools, but there is **zero coverage**
of: HTTP transport (`RunHttpAsync`, `/health`, CORS), `Middleware/ApiKeyMiddleware.cs` and
`Services/ObsidianBridgeService.cs` (requestId correlation, timeouts, reconnection,
protocol version mismatch).

## Scope

1. **ApiKeyMiddleware** (unit, top priority — it's the security boundary):
   no key → passes; `/health` exempt; correct/incorrect/missing/case-mismatched Bearer;
   whitespace.
2. **HTTP transport** (integration with `WebApplicationFactory` or startup on an
   ephemeral port): `/health` 200, `/mcp` responds to initialize, 401 without a token when
   `KIOKU_API_KEY` is configured.
3. **ObsidianBridgeService** (unit/integration with an in-process fake WebSocket server,
   mirroring the plugin's approach in `protocol.contract.test.ts`): request/response with
   requestId, 10s timeout, unknown command, reconnection after the fake server drops,
   `protocolVersion` stamping.

## Acceptance criteria

- [ ] New coverage visible in Codecov (`server` flag) for the 3 components.
- [ ] Tests run in CI with no external network (all on loopback/ephemeral) and no flakiness
  (generous timeouts, dynamic ports).
- [ ] `dotnet test` green on linux and osx-arm64 (existing CI matrix).

## Files

- `src/Kioku.Mcp.Server.Tests/ApiKeyMiddlewareTests.cs` (new)
- `src/Kioku.Mcp.Server.Tests/HttpTransportTests.cs` (new)
- `src/Kioku.Mcp.Server.Tests/ObsidianBridgeServiceTests.cs` (new, with fake WS server)
- Possibly `Microsoft.AspNetCore.Mvc.Testing` in the tests csproj (`deps` scope if done as
  a separate commit)
