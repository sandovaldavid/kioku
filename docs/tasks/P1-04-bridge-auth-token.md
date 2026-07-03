# P1-04 — Autenticación por token del bridge WebSocket

| Campo | Valor |
|---|---|
| Prioridad | P1 |
| Rama | `feat/bridge-auth-token` |
| Commit | `feat(server): add optional shared-token auth to the obsidian bridge` (server) + cambios de plugin en el mismo PR |
| Tamaño | M |
| Spec | [features/04-bridge-auth-token.md](../features/04-bridge-auth-token.md) |
| Dependencias | Recomendado después de [P1-05](P1-05-http-and-bridge-coverage.md) (cobertura del bridge antes de cambiar el protocolo) |

## Objetivo

Token compartido opcional para el WebSocket 7765: setting `authToken` en el plugin +
`KIOKU_BRIDGE_TOKEN` en el server, con handshake `auth` (PROTOCOL_VERSION 2) retrocompatible
— sin token configurado, todo funciona como hoy.

## Alcance

- Plugin: setting + botón "Generate", comando `auth` en `handlers.ts`, rechazo (close 4401)
  de conexiones no autenticadas cuando hay token, `timingSafeEqual`.
- Server: env var nueva, autenticación tras cada conexión **y cada reconexión** en
  `ObsidianBridgeService`, `KiokuError.Unauthorized` en tools bridge si el auth falla.
- `protocol-schema.json` + `PROTOCOL_VERSION = 2` en ambos lados.

## Criterios de aceptación

- [ ] Matriz probada (tests + manual): sin token ambos lados ✓ · token correcto ✓ · token
  incorrecto → conexión cerrada y tools devuelven `[error] [UNAUTHORIZED]` · plugin con
  token + server sin token → ídem · reconexión re-autentica sola.
- [ ] Cliente v1 (sin `auth`) contra plugin sin token sigue funcionando (retrocompatibilidad).
- [ ] Contract tests del protocolo actualizados (`protocol.contract.test.ts`) y verdes.
- [ ] Docs actualizadas en el mismo PR: `install.md`, `troubleshooting.md`, tablas de env
  vars (README raíz, server README, `.mcp/server.json`), `docs/features/04` marcado.
- [ ] Build/lint/tests de ambos proyectos verdes.

## Archivos

- `src/obsidian-kioku-mcp/src/{types,bridge,handlers,main}.ts`, `protocol-schema.json`, tests
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs`,
  `src/Kioku.Mcp.Server/Services/ObsidianBridgeService.cs`
- Docs listadas arriba
