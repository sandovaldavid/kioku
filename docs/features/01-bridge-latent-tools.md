# 01 — Tools latentes del bridge

> Área: server · Tarea: [P1-01](../tasks/P1-01-bridge-latent-tools.md) · Impacto ★★ · Esfuerzo S

## Motivación

El plugin implementa 22 comandos en `src/obsidian-kioku-mcp/src/handlers.ts`, pero el server
solo consume 14. Hay **8 comandos ya implementados, testeados y sin exponer** como tools MCP:

| Comando del plugin | Qué hace |
|---|---|
| `get-selection` | Devuelve la selección actual del editor |
| `toggle-reading-mode` | Alterna entre modo edición y lectura |
| `fold-all-headings` / `unfold-all-headings` | Pliega/despliega todos los headings |
| `get-vault-path` | Ruta y nombre del vault abierto |
| `is-obsidian-ready` | Health check del plugin |
| `get-app-version` | Versión de Obsidian y del plugin |
| `reload-snippets` | Recarga los CSS snippets |

Exponerlos es costo mínimo (el lado difícil ya existe) y completa la paridad server↔plugin.

## Diseño

Nuevos tools en `Tools/ObsidianBridgeTools.cs` (grupo `bridge`), vía
`ObsidianBridgeService.SendRequestAsync(command, payload)`:

| Tool MCP | Comando bridge | Notas |
|---|---|---|
| `get_selection_in_obsidian()` | `get-selection` | Devuelve `{selection, hasSelection, length}` |
| `toggle_reading_mode()` | `toggle-reading-mode` | — |
| `fold_all_headings()` | `fold-all-headings` | — |
| `unfold_all_headings()` | `unfold-all-headings` | — |
| `get_obsidian_status()` | `is-obsidian-ready` + `get-app-version` + `get-vault-path` | **Un solo tool** que agrega los 3 diagnósticos: `{ready, obsidianVersion, kiokuVersion, vaultPath, vaultName}`. Evita 3 tools triviales. |

Y en `Tools/CssThemingTools.cs` (grupo `css`):

| Tool MCP | Comando bridge | Notas |
|---|---|---|
| `reload_css_snippets()` | `reload-snippets` | Complementa `apply_css_snippet`, que hoy no fuerza recarga |

Total: **6 tools nuevos** (102 → 108). Manejo de errores igual que los tools bridge
existentes: si el plugin no está conectado, devolver `KiokuError.DependencyUnavailable`.

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/ObsidianBridgeTools.cs` (+5 tools)
- `src/Kioku.Mcp.Server/Tools/CssThemingTools.cs` (+1 tool)
- `docs/commands-reference.md` (regenerar)
- Ningún cambio en el plugin (los handlers ya existen y están cubiertos por
  `src/handlers.test.ts`)

## Riesgos

- Bajo. `get_obsidian_status` hace 3 round-trips WebSocket secuenciales (~ms en localhost);
  si preocupa, el plugin puede añadir un comando agregado `get-status` en una iteración futura.
- `get-vault-path` usa API interna de Obsidian (`vault.adapter.basePath`) — ya mitigado en el
  plugin con fallback `"unknown"`.
