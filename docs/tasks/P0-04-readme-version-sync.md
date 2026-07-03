# P0-04 — Sincronizar versiones de README y server.json con release-please

| Campo | Valor |
|---|---|
| Prioridad | P0 |
| Rama | `chore/readme-version-sync` |
| Commit | `chore(release): bump README and server.json versions via release-please extra-files` |
| Tamaño | S |
| Dependencias | Ninguna |

## Contexto

release-please solo actualiza `csproj`, `manifest.json` y `package.json` del plugin
(`extra-files`). Las versiones escritas a mano en `README.md`,
`src/Kioku.Mcp.Server/README.md` y `src/Kioku.Mcp.Server/.mcp/server.json` derivan en cada
release (el README llegó a decir beta.4 y el server README 1.6.2 con el repo en beta.8).
La revisión de docs de 2026-07-02 las corrigió y dejó anotaciones
`<!-- x-release-please-version -->` en ambos README, pero **sin registrar los archivos en la
config, release-please no los toca**.

## Alcance

1. Añadir a `extra-files` en **ambas** configs (`release-please-config.json` y
   `release-please-config.beta.json`):
   - `README.md` (updater `generic` — usa la anotación `x-release-please-version` ya presente)
   - `src/Kioku.Mcp.Server/README.md` (ídem)
   - `src/Kioku.Mcp.Server/.mcp/server.json` (updater `json`, jsonpaths `$.version` y
     `$.packages[0].version`)
2. Verificar la sintaxis de updaters genéricos de `release-please-action@v4` para archivos
   markdown/json (documentación oficial de release-please "Updating arbitrary files").

## Criterios de aceptación

- [ ] El siguiente release PR de release-please en `develop` actualiza la versión en los
  3 archivos (verificable en el diff del PR automático).
- [ ] Las anotaciones en los README quedan en la misma línea que la versión.
- [ ] No se rompe el pipeline `release-please.yml` (dry-run local o revisión del PR bot).

## Archivos

- `release-please-config.json`
- `release-please-config.beta.json`
- `README.md`, `src/Kioku.Mcp.Server/README.md`,
  `src/Kioku.Mcp.Server/.mcp/server.json` (solo si la sintaxis de anotación requiere ajuste)
