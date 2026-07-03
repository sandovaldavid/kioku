# P0-03 — Re-agrupar tools de merge-conflict fuera de `plugin`

| Campo | Valor |
|---|---|
| Prioridad | P0 |
| Rama | `fix/merge-tools-group` |
| Commit | `fix(server): move merge-conflict tools out of the plugin capability group` |
| Tamaño | S |
| Dependencias | Ninguna |

## Contexto

`fix_merge_conflicts` y `resolve_merge_conflict` viven en `Tools/PluginIntegrationTools.cs`
(grupo `plugin`), pero **no usan el bridge de Obsidian**: escanean y editan archivos locales
con marcadores de conflicto git (`<<<<<<<`). Si el usuario desactiva el grupo `plugin`
(p. ej. porque no usa el plugin de Obsidian), pierde dos tools que no lo necesitan.

## Alcance

1. Mover ambos tools a `Tools/GitTools.cs` (grupo `git`) — conceptualmente son tooling de
   conflictos git. Alternativa si se prefiere no tocar `git`: nueva clase pequeña; preferimos
   `git` para no crear un grupo más.
2. Mantener los nombres de tools (sin breaking change para agentes).
3. Documentar en el PR que su grupo de capabilities cambió `plugin` → `git` (usuarios con
   `capabilities.require_explicit` o `disabled` podrían verse afectados).

## Criterios de aceptación

- [ ] Con `capabilities.disabled: [plugin]`, `fix_merge_conflicts` y
  `resolve_merge_conflict` siguen disponibles (grupo `git` habilitado).
- [ ] Con `capabilities.disabled: [git]`, dejan de registrarse.
- [ ] Build + tests verdes; `dotnet format` sin cambios.
- [ ] `docs/commands-reference.md` regenerado (los tools aparecen bajo `GitTools`).
- [ ] Tablas de README raíz / server README actualizadas (fila Git y fila Plugin Bridge).

## Archivos

- `src/Kioku.Mcp.Server/Tools/PluginIntegrationTools.cs` (quitar)
- `src/Kioku.Mcp.Server/Tools/GitTools.cs` (añadir)
- `docs/commands-reference.md`, `README.md`, `src/Kioku.Mcp.Server/README.md`,
  `docs/vault-config.md` (si menciona ejemplos de grupos)
