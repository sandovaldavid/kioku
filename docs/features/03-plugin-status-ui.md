# 03 — UI de estado del plugin (status bar + comandos)

> Área: plugin · Tarea: [P1-03](../tasks/P1-03-plugin-status-ui.md) · Impacto ★★ · Esfuerzo S

## Motivación

El plugin corre un servidor WebSocket de larga vida pero **no tiene ninguna señal visual de
estado**: ni status bar, ni ribbon, ni indicador de clientes conectados. Hoy la única forma de
saber si el bridge está vivo es abrir la consola de desarrollador. Único comando existente:
`kioku-restart-bridge`.

## Diseño

### Status bar (principal)

`main.ts` registra un status bar item (`addStatusBarItem()`):

- `[online] Kioku :7765 (1)` — bridge escuchando, 1 cliente conectado
- `[online] Kioku :7765` — escuchando, sin clientes
- `[offline] Kioku` — bridge detenido o error de arranque (p. ej. puerto ocupado)

Sin emojis (regla del repo); prefijos `[online]`/`[offline]` + clase CSS `.kioku-status`
(variantes `.kioku-status-online` / `.kioku-status-offline` en `styles.css`). Click en el
item → ejecuta el comando de reinicio.

### Cambios en `bridge.ts`

`BridgeServer` expone lo necesario para la UI:

- `get clientCount(): number` (tamaño del set de clientes existente)
- `get isRunning(): boolean`
- Callbacks `onClientConnected` / `onClientDisconnected` / `onStateChange` que `main.ts`
  usa para refrescar el status bar (mismo patrón que `onStartupError` /
  `onProtocolMismatch` actuales).

### Comandos nuevos

| ID | Nombre | Acción |
|---|---|---|
| `kioku-stop-bridge` | Stop Kioku MCP Bridge | `bridge.stop()` + refresco de status |
| `kioku-start-bridge` | Start Kioku MCP Bridge | `bridge.start()` + refresco |
| `kioku-copy-status` | Copy Kioku bridge status | Copia al clipboard JSON `{running, port, clients, protocolVersion, pluginVersion}` para reportes de bugs |

(`kioku-restart-bridge` se mantiene.)

### Setting nuevo

- `showStatusBar: boolean` (default `true`) en `KiokuSettings` + toggle en `KiokuSettingTab`.

## Archivos afectados

- `src/obsidian-kioku-mcp/src/main.ts` (status bar, comandos, setting)
- `src/obsidian-kioku-mcp/src/bridge.ts` (getters + callbacks)
- `src/obsidian-kioku-mcp/src/types.ts` (`KiokuSettings`, `DEFAULT_SETTINGS`)
- `src/obsidian-kioku-mcp/styles.css` (`.kioku-status*`)
- `src/obsidian-kioku-mcp/src/__mocks__/obsidian.ts` + tests (mock de `addStatusBarItem`)

## Riesgos

- Bajo. Cuidar el ciclo de vida: limpiar callbacks y el item en `onunload` (requisito de la
  Community Store). `main.ts` está excluido de cobertura — mover la lógica de formato de
  estado a una función pura testeable (p. ej. en `types.ts` o un `status.ts` nuevo).
