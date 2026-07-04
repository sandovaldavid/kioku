# P2-03 — Daily digest

| Campo | Valor |
|---|---|
| Prioridad | P2 |
| Rama | `feat/daily-digest` |
| Commit | `feat(server): add generate_digest tool for daily and weekly reviews` |
| Tamaño | S |
| Spec | [features/07-daily-digest.md](../features/07-daily-digest.md) |
| Dependencias | Ninguna dura; si P2-01 está mergeado, añade sección de resumen local |

## Objetivo

`generate_digest(period = day|week, target_folder, dry_run)` en `WorkflowTools`: nota
generada con actividad del período, tareas vencidas/por vencer, huérfanas nuevas y notas en
`draft`/`inbox`, escrita en `folders.daily` con frontmatter `type: log, tags: [digest]`.

## Criterios de aceptación

- [ ] Digest correcto con fixture: períodos day/week, secciones vacías se omiten con
  encabezado "sin novedades", corte temporal documentado (medianoche local / 7 días).
- [ ] Re-ejecutar el mismo día reemplaza la nota (comportamiento documentado en la
  descripción del tool).
- [ ] `dry_run=true` devuelve el markdown sin escribir.
- [ ] Con `GenerationService` disponible añade sección **Resumen** local; sin él, el digest
  se genera igual (sin fallo).
- [ ] `commands-reference.md` regenerado + tablas de README actualizadas.

## Archivos

- `src/Kioku.Mcp.Server/Tools/WorkflowTools.cs`
- Reuso: `VaultIndexService`, `TaskService`, `VaultConfigService`
- Tests con `VaultFixture` + docs
