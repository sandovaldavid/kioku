# P1-03 — Status bar y comandos de control del bridge (plugin)

| Campo | Valor |
|---|---|
| Prioridad | P1 |
| Rama | `feat/plugin-status-ui` |
| Commit | `feat(plugin): add bridge status bar item and control commands` |
| Tamaño | S |
| Spec | [features/03-plugin-status-ui.md](../features/03-plugin-status-ui.md) |
| Dependencias | Ninguna |

## Objetivo

Dar visibilidad al estado del bridge sin abrir la consola: status bar item
(`[online] Kioku :7765 (1)` / `[offline] Kioku`), comandos `start`/`stop`/`copy-status`
además del `restart` existente, y setting `showStatusBar`.

## Alcance

- `bridge.ts`: getters `isRunning`/`clientCount` + callbacks `onClientConnected`/
  `onClientDisconnected`/`onStateChange`.
- `main.ts`: status bar item (click = restart), 3 comandos nuevos, setting + toggle.
- `types.ts`: `KiokuSettings.showStatusBar` (default `true`); función pura de formato de
  estado (testeable) fuera de `main.ts`.
- `styles.css`: `.kioku-status`, `.kioku-status-online`, `.kioku-status-offline`.
- Sin emojis; prefijos `[online]`/`[offline]` (regla del repo).

## Criterios de aceptación

- [ ] El status bar refleja en vivo: arranque, parada, error de puerto ocupado, conexión y
  desconexión del server C# (probar manualmente con Obsidian + server).
- [ ] `kioku-copy-status` copia JSON con `{running, port, clients, protocolVersion, pluginVersion}`.
- [ ] Con `showStatusBar=false` el item no se muestra (y se limpia al vuelo al cambiar el toggle).
- [ ] `onunload` limpia item, callbacks y servidor (sin listeners huérfanos).
- [ ] Tests: función de formato de estado + callbacks del `BridgeServer`
  (`handlers.test.ts`/nuevo `status.test.ts`); `pnpm --filter obsidian-kioku-mcp test` verde.
- [ ] `pnpm lint:plugin`, `format:check` y `tsc --noEmit` verdes.

## Archivos

- `src/obsidian-kioku-mcp/src/{main,bridge,types}.ts`, `styles.css`
- `src/obsidian-kioku-mcp/src/__mocks__/obsidian.ts`, tests
