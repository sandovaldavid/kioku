# P1-01 — Exponer los comandos latentes del bridge como tools MCP

| Campo | Valor |
|---|---|
| Prioridad | P1 |
| Rama | `feat/bridge-latent-tools` |
| Commit | `feat(server): expose latent bridge commands as MCP tools` |
| Tamaño | S |
| Spec | [features/01-bridge-latent-tools.md](../features/01-bridge-latent-tools.md) |
| Dependencias | Ninguna (los handlers del plugin ya existen y están testeados) |

## Objetivo

Añadir 6 tools MCP sobre los 8 comandos que `handlers.ts` ya implementa y el server no usa:
`get_selection_in_obsidian`, `toggle_reading_mode`, `fold_all_headings`,
`unfold_all_headings`, `get_obsidian_status` (agrega `is-obsidian-ready` +
`get-app-version` + `get-vault-path`) y `reload_css_snippets`.

## Alcance

- `ObsidianBridgeTools.cs`: +5 tools (grupo `bridge`), mismo patrón de los existentes
  (`SendRequestAsync` + `KiokuError.DependencyUnavailable` si el plugin no responde).
- `CssThemingTools.cs`: +1 tool `reload_css_snippets` (grupo `css`).
- Sin cambios en el plugin.

## Criterios de aceptación

- [ ] Los 6 tools aparecen en `tools/list` con descripciones claras.
- [ ] Con Obsidian cerrado devuelven `[error] [DEPENDENCY_UNAVAILABLE] ...` (no excepción).
- [ ] Prueba manual end-to-end con Obsidian abierto: `get_obsidian_status` devuelve
  `ready`, versiones y vault; `get_selection_in_obsidian` refleja la selección real.
- [ ] `docs/commands-reference.md` regenerado (102 → 108 tools).
- [ ] Tablas de tools de README raíz y server README actualizadas.
- [ ] `dotnet build` + `dotnet format` + tests verdes.

## Archivos

- `src/Kioku.Mcp.Server/Tools/ObsidianBridgeTools.cs`
- `src/Kioku.Mcp.Server/Tools/CssThemingTools.cs`
- `docs/commands-reference.md`, `README.md`, `src/Kioku.Mcp.Server/README.md`
