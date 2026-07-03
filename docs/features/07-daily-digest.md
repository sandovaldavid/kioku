# 07 — Daily digest

> Área: server · Tarea: [P2-03](../tasks/P2-03-daily-digest.md) · Impacto ★★★ · Esfuerzo S

## Motivación

"¿Qué aprendí esta semana, qué está vencido, qué quedó suelto?" — hoy responderlo requiere
llamar 4-5 tools por separado. Un digest de un solo tool es la feature más demo-able para el
persona estudiante y crea un hábito diario de uso.

## Diseño

### `generate_digest(period = "day", target_folder = "", dry_run = false)`

En `WorkflowTools` (grupo `workflows`):

- `period`: `day` | `week`.
- Secciones del digest (todas con datos ya disponibles):
  1. **Actividad** — notas creadas/modificadas del período (`get_recent_activity` /
     `VaultIndexService`).
  2. **Tareas** — vencidas y por vencer (`TaskService`).
  3. **Huérfanas nuevas** — notas del período sin enlaces (`find_unlinked_notes` acotado).
  4. **Para revisar** — notas del período con status `draft`/`inbox`.
- Escribe la nota en la carpeta `daily` de `.kioku/config.yml` (`folders.daily`, fallback
  `target_folder` o raíz) con nombre `Digest {yyyy-MM-dd}.md` y frontmatter
  `type: log, tags: [digest]`. Si ya existe, la reemplaza (es generada).
- `dry_run=true` devuelve el markdown sin escribir.

### Mejora opcional con generación local

Si `GenerationService` (spec 05) está disponible, añade una sección **Resumen** de 3-4
líneas generada localmente a partir de los títulos/snippets del período. Si no, el digest
es puramente estructural — el feature **no depende** de 05.

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/WorkflowTools.cs` (+1 tool)
- Reuso: `VaultIndexService`, `TaskService`, `GraphAnalysisTools` internals (extraer a
  servicio/helper si hace falta), `VaultConfigService.GetFolder("daily")`
- Tests: construcción del digest con vault fixture (períodos, secciones vacías)
- `docs/commands-reference.md` (regenerar)

## Riesgos

- Bajo. Definir claramente el corte temporal (medianoche local; `week` = últimos 7 días).
- Sobrescribir el digest existente es intencional — documentarlo en la descripción del tool.
