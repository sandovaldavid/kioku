# 04 — Autenticación del bridge WebSocket

> Área: server + plugin · Tarea: [P1-04](../tasks/P1-04-bridge-auth-token.md) · Impacto ★★ · Esfuerzo M

## Motivación

El WebSocket del plugin escucha en `127.0.0.1:7765` **sin autenticación**: cualquier proceso
local puede conectarse y ejecutar los 22 comandos del bridge (abrir archivos, insertar texto,
ejecutar comandos de Obsidian arbitrarios vía `trigger-command`). El README lo declara como
limitación conocida. Un token compartido opcional cierra ese vector sin romper instalaciones
existentes, y es un requisito razonable antes de publicar en la Community Store.

## Diseño

### Token compartido opcional

- **Plugin**: setting `authToken: string` (default `""` = sin auth) en `KiokuSettings`,
  con botón "Generate" en el settings tab (32 bytes aleatorios hex vía `crypto`).
- **Server**: env var nueva `KIOKU_BRIDGE_TOKEN` en `KiokuConfiguration.FromEnvironment()`.

### Handshake (PROTOCOL_VERSION = 2, retrocompatible)

1. Al conectar, el cliente C# envía como primer mensaje:
   `{command: "auth", payload: {token}, protocolVersion: 2, requestId}`.
2. Plugin con `authToken` configurado:
   - `auth` correcto → `{success: true}`; la conexión queda autenticada.
   - Cualquier otro comando antes de autenticar, o token inválido → `{success: false,
     error: "[error] [UNAUTHORIZED] ..."}` y **cierre de la conexión** (code 4401).
3. Plugin sin `authToken` (default): acepta conexiones como hoy; el comando `auth` responde
   `{success: true}` (no-op). Clientes v1 siguen funcionando → **sin breaking change**.
4. Comparación de tokens en tiempo constante (`crypto.timingSafeEqual`).

`PROTOCOL_VERSION` sube a 2 en `types.ts` y `ObsidianBridgeService.BridgeProtocol`; el
mecanismo `onProtocolMismatch` existente sigue avisando ante desalineación de versiones.

### UX de errores

- Server sin token contra plugin con token → log `[error] [UNAUTHORIZED] Bridge requires
  KIOKU_BRIDGE_TOKEN` y los tools bridge devuelven `KiokuError.Unauthorized`.
- Notice opcional en el plugin al rechazar una conexión (respetando `showNotifications`).

## Archivos afectados

- `src/obsidian-kioku-mcp/src/types.ts` (`PROTOCOL_VERSION`, `KiokuSettings`)
- `src/obsidian-kioku-mcp/src/bridge.ts` (estado autenticado por conexión, cierre 4401)
- `src/obsidian-kioku-mcp/src/handlers.ts` (comando `auth`)
- `src/obsidian-kioku-mcp/src/main.ts` (setting + generador)
- `src/obsidian-kioku-mcp/src/protocol-schema.json` (comando `auth`)
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs` (`KIOKU_BRIDGE_TOKEN`)
- `src/Kioku.Mcp.Server/Services/ObsidianBridgeService.cs` (auth tras conectar y tras
  cada reconexión)
- Tests: contract tests del protocolo (`protocol.contract.test.ts`) + unit tests de rechazo;
  lado C# si P1-05 ya aportó cobertura del bridge
- Docs: `install.md`, `troubleshooting.md`, README raíz y server (tabla env vars),
  `.mcp/server.json`

## Riesgos

- **Reconexión**: `ObsidianBridgeService` se reconecta automáticamente — debe re-autenticar
  en cada conexión nueva (incluir en tests).
- Deriva de docs de env vars (ya conocida) — actualizar todas las tablas en el mismo PR.
- El token viaja en claro por localhost; es aceptable (mismo modelo que `KIOKU_API_KEY`
  en HTTP local). Documentar que no protege contra procesos con acceso al config del plugin.
