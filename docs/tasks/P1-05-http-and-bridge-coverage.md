# P1-05 — Cobertura de tests: transporte HTTP, ApiKeyMiddleware y bridge

| Campo | Valor |
|---|---|
| Prioridad | P1 |
| Rama | `test/http-and-bridge-coverage` |
| Commit | `test(server): cover http transport, api key middleware and bridge service` |
| Tamaño | M |
| Spec | — (gap señalado en [review/06-testing-strategy.md](../review/06-testing-strategy.md)) |
| Dependencias | Ninguna; recomendado **antes** de P1-04 |

## Contexto

Los ~122 tests actuales cubren parsers/helpers/write-tools, pero **cero cobertura** de:
transporte HTTP (`RunHttpAsync`, `/health`, CORS), `Middleware/ApiKeyMiddleware.cs` y
`Services/ObsidianBridgeService.cs` (correlación por requestId, timeouts, reconexión,
mismatch de versión de protocolo).

## Alcance

1. **ApiKeyMiddleware** (unit, prioridad máxima — es la frontera de seguridad):
   sin clave → pasa; `/health` exento; Bearer correcto/incorrecto/ausente/case; espacios.
2. **HTTP transport** (integración con `WebApplicationFactory` o arranque en puerto
   efímero): `/health` 200, `/mcp` responde initialize, 401 sin token cuando
   `KIOKU_API_KEY` está configurada.
3. **ObsidianBridgeService** (unit/integración con un WebSocket server fake en proceso,
   espejo del enfoque del plugin en `protocol.contract.test.ts`): request/response con
   requestId, timeout de 10s, comando desconocido, reconexión tras caída del server fake,
   `protocolVersion` estampado.

## Criterios de aceptación

- [ ] Cobertura nueva visible en Codecov (flag `server`) para los 3 componentes.
- [ ] Los tests corren en CI sin red externa (todo en loopback/efímero) y sin flakiness
  (timeouts generosos, puertos dinámicos).
- [ ] `dotnet test` verde en linux y osx-arm64 (matriz de CI existente).

## Archivos

- `src/Kioku.Mcp.Server.Tests/ApiKeyMiddlewareTests.cs` (nuevo)
- `src/Kioku.Mcp.Server.Tests/HttpTransportTests.cs` (nuevo)
- `src/Kioku.Mcp.Server.Tests/ObsidianBridgeServiceTests.cs` (nuevo, con fake WS server)
- Posible `Microsoft.AspNetCore.Mvc.Testing` en el csproj de tests (scope `deps` si va en
  commit aparte)
