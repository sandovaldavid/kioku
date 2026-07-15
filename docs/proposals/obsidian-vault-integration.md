# Propuesta: integración fina de Kioku con vaults Obsidian reales

> Origen: configuración de Kioku (post #228–#231, develop 2026-07-14) sobre el vault
> real **Cortex-L7** — 381 notas, Templater + QuickAdd + Linter + obsidian-git +
> folder-notes, canon propio de frontmatter. Cada ítem sale de una fricción
> observada al hacer esa integración de punta a punta.

## 1. Emitir y mantener `updated:` en el frontmatter

**Fricción.** El canon del vault exige `date` + `updated` en toda nota. `BuildFrontmatter`
emite `date` pero nunca `updated`, y las tools de escritura (`update_note_content`,
`append_to_note`, `update_frontmatter`, engineering) no lo tocan al modificar. El vault
lo mitiga con la regla `yaml-timestamp` del Linter (solo corrige al guardar EN Obsidian),
así que los docs creados/editados por agentes quedan con `updated` desactualizado hasta
que un humano los abre.

**Propuesta.** (a) `BuildFrontmatter` emite `updated:` = `date` al crear; (b) toda tool
que modifique una nota actualiza `updated:` si la clave existe (o config
`frontmatter.touch_updated: true`).

## 2. `update_frontmatter` con claves arbitrarias

**Fricción.** Solo soporta `tags/status/type`. El prompt `write_daily` ya documenta el
workaround ("no intentes setear project con update_frontmatter"). Transiciones reales del
workflow necesitan más: `priority`, `deadline`, `supersedes` (ADRs), `publish_date`,
`sprint_id`…

**Propuesta.** Aceptar cualquier clave escalar con lista blanca opcional en config
(`frontmatter.allowed_keys`) y validación de `status` por tipo como ya hace engineering.

## 3. Orden de claves YAML configurable

**Fricción.** El orden de emisión está hardcodeado (`tags, aliases, cssclasses, type,
status, domain, date, zettel_id, extras`). El vault tuvo que reconfigurar la regla
`yaml-key-sort` de Linter para igualarlo y evitar diffs de churn con obsidian-git
(auto-commit cada 60 s). Otro vault con otro orden sufrirá ese churn.

**Propuesta.** `frontmatter.key_order: [...]` en config; default el actual.

## 4. Tools dedicadas `create_ticket` y `create_daily`

**Fricción.** Son los únicos doc-types engineering sin tool de creación (van vía
`create_note_from_template` + prompt). Resultado: no hay garantía de frontmatter
(`type: ticket`, `status: open`), ni de nombre de archivo, ni de `project_link`, y el
prompt es más frágil que una firma tipada.

**Propuesta.** `create_ticket(project, title, idea, requirements="", tags="")` y
`create_daily(project, content="")` con el mismo pipeline que el resto (scaffold lazy,
template override, Templater eval, reindex síncrono).

## 5. Normalización configurable del nombre de agente

**Fricción.** El slug del agente en los archivos de sesión sale de
`IMcpServer.ClientInfo.Name` normalizado con un mapa fijo. Clientes nuevos caen al
fallback genérico.

**Propuesta.** `sessions.agent_aliases: {claude-code: claude, antigravity-cli: anti}` en
config, mezclado sobre el mapa embebido.

## 6. Resource MCP `vault://conventions`

**Fricción.** Cortex-L7 define su canon en `readme.md` + `agents.md` (frontmatter,
taxonomía de tags, reglas de vinculación). Hoy cada agente tiene que saber que existen y
leerlos; los que no lo hacen escriben notas fuera de canon.

**Propuesta.** Exponer un resource `vault://conventions` que devuelva el contenido de una
lista configurable (`conventions_files: [readme.md, agents.md]`), y que los prompts de
engineering instruyan leerlo primero.

## 7. Snippet CSS de fábrica para `cssclasses`

**Fricción.** Kioku ya emite `cssclasses: [kioku-adr, ...]` (excelente para theming) pero
el estilo hay que escribirlo a mano; en Cortex-L7 se creó `kioku-docs.css` (franja de
color + H1 tintado por tipo). Cualquier otro vault empieza de cero.

**Propuesta.** Embeber ese snippet como recurso y ofrecerlo en
`setup_agent_workflow(write_css=true)` vía el mecanismo existente de `apply_css_snippet`.

## 8. `audit_vault` con las dimensiones que encontramos a mano

**Fricción.** La auditoría real del vault requirió scripts externos: `audit_vault` no
detecta (a) tags huérfanos de uso único, (b) tags con profundidad excesiva, (c) tags con
segmentos duplicados (`domain/tech/backend/framework/backend` — 16+ casos generados por
auto-tagging), (d) MOCs sin `meta/moc` / `type: moc`, (e) valores de `type`/`status`
fuera del canon declarado en `defaults`, (f) `type`/`status` como listas.

**Propuesta.** Añadir esas secciones al reporte (usando `defaults` como canon declarado)
+ parámetros `max_tag_depth`, `tag_min_uses`.

## 9. Renombrado de carpetas con actualización de queries

**Fricción.** La migración kebab de Cortex-L7 (todas las carpetas) rompió: queries
dataview `FROM "20_Execution"`, exclusiones `WHERE file.name != "Backend"` en
folder-notes, y configs de plugins con rutas. `rename_note` actualiza wikilinks pero no
existe equivalente para carpetas, y los wikilinks no cubren dataview.

**Propuesta.** `rename_folder(folder, new_name, update_references=true)` que además de
mover reescriba: `FROM/dv.pages` con el prefijo viejo, exclusiones por nombre de
folder-note, y opcionalmente informe de las rutas halladas en `.obsidian/*.json` (sin
tocarlas, para no pelear con Obsidian abierto).

## 10. Interacción Templater ↔ creación externa de notas

**Observación (riesgo, no bug confirmado).** Con Obsidian abierto,
`trigger_on_file_creation: true` de Templater dispara sobre archivos creados
externamente. Kioku también evalúa Templater in-place vía bridge y auto-registra folder
templates para las subcarpetas engineering. Posible doble inyección o carrera cuando
kioku crea una nota en una carpeta con folder-template mientras Obsidian está abierto.

**Propuesta.** Documentar la semántica esperada en `docs/vault-config.md` y, si hay
carrera, que el bridge cree las notas vía API de Obsidian (como `create_note_ui`) cuando
el bridge esté conectado, dejando la escritura directa a disco solo para modo headless.

## 11. BUG: la raíz de proyectos se detecta como proyecto "."

**Reproducción (Cortex-L7).** Con `folders.projects: 20-execution` y un folder-note
`20-execution/20-execution.md` con `type: moc` (patrón folder-notes + taxonomía del
vault), `IsProjectFolder(ProjectsRoot)` da true → `WalkForProjects` devuelve `.` y **no
recurse**: `list_projects` muestra un único proyecto "." vacío y oculta los reales.

**Fix.** En `DiscoverProjects`, no evaluar `IsProjectFolder` sobre `ProjectsRoot` mismo:
empezar el walk directamente por sus subdirectorios. Workaround aplicado en el vault:
el folder-note raíz quedó `type: guide` (conservando el tag `meta/moc`).

## 12. BUG: `update_frontmatter` con `type` reportó ok sin cambiar el valor

**Reproducción.** `update_frontmatter(note='20-execution/20-execution.md', field='type',
value='guide')` respondió `[ok] Frontmatter updated` pero el archivo quedó con
`type: moc`; además la reescritura añadió BOM UTF-8 al inicio y eliminó la clave
`updated:`. (Posible interacción con la nota abierta en Obsidian o con la ruta de
serialización de frontmatter.)

**Propuesta.** Releer y verificar el valor tras escribir (fail loudly si no cambió),
escribir sin BOM, y preservar todas las claves existentes al serializar.

## 13. Enumeración recursiva dentro de las subcarpetas de proyecto

**Reproducción.** Con `api.core/knowledge/employee-debt/*.md` (knowledge agrupado en
subcarpetas temáticas), `list_projects` reporta el proyecto como *empty* y
`get_project_context` no lista esos docs: `EnumerateProjectDocs` solo mira los `.md`
directos de cada subcarpeta estándar. Workaround aplicado: aplanar el knowledge.

**Propuesta.** Enumerar recursivamente dentro de cada subcarpeta estándar (o al menos un
nivel más), mostrando la ruta relativa como contexto.

## 14. Falsos positivos en `find_broken_links` / `audit_vault`

**Reproducción (Cortex-L7).** Tras la limpieza, los únicos 7 "broken links" reportados
son falsos positivos de dos clases:

1. **Wikilinks dentro de código**: notas que documentan la propia sintaxis
   (`wikilinks-system.md` con `` `[[Note Name]]` ``, `excalidraw-visual-thinking.md` con
   `[[file]]` en fences) se reportan como rotos. El parser de links no excluye inline
   code ni code blocks — se verificó que envolver en backticks NO evita la detección
   (hubo que reescribir un placeholder de template a texto plano).
2. **Links a carpetas excluidas**: `[[60-archive/60-archive]]` es un link válido en
   Obsidian, pero como `60-archive` está en `exclude:`, la nota destino no está en el
   índice y el link se reporta roto.

**Propuesta.** (a) Ignorar `[[...]]` dentro de inline code y fenced code blocks al
extraer links salientes; (b) para la detección de rotos, resolver el destino contra el
filesystem (o un índice de nombres sin contenido) cuando caiga en carpeta excluida,
en vez de solo contra el índice.

---

*Los ítems 1–3 son los de mayor impacto para convivir con Linter + obsidian-git; 4 y 6
completan el workflow de agentes; 11–14 son bugs/límites reproducidos durante la
integración; el resto es calidad de vida.*


# Mejoras propuestas por agentes que usaron kioku MCP

## OPENCODE 
Mejoras de workflow para agentes AI
Sugerencia	Por qué
start_work_session debería devolver un hint: "Append to Log after each milestone"	El agente no sabe que debe escribir incrementalmente
complete_task debería aceptar el texto del task (no line number)	Los line numbers son frágiles ante ediciones
end_work_session debería auto-poblar Modified during this session	Evita secciones vacías
Los templates deberían incluir un {{auto_tasks}} con - [ ] de los steps	El plan template tiene {{steps}} pero el agente tiene que adivinar el formato markdown exacto
Veredicto general
Kioku MCP es funcional y útil para el flujo agente-proyecto. La curva está en que el agente AI necesita conocer el formato exacto de cada template para no generar contenido fuera de spec. Las herramientas base (create_note, read_note, start/end_work_session, complete_task) son sólidas. Los bugs principales son record_adr (auto-increment) y rename_note (duplicación).

## Antigravity cli

# Feedback: Kioku MCP — Perspectiva del Agente

> Evaluación realizada el 2026-07-14 tras usar el MCP end-to-end en el proyecto `product-api`.

---

## TL;DR

Kioku es sólido como concepto y la implementación del flujo de engineering ya funciona. El punto más débil no es el diseño sino la **ergonomía de las herramientas de lectura**, que genera fricción cuando el agente quiere acceder a una nota y tiene que adivinar el argumento correcto.

---

## ✅ Lo que funciona bien

### 1. `get_project_context` — la herramienta estrella
Esta es claramente la pieza central del sistema y **funciona exactamente como debe**. Un solo call devuelve:
- MOC del proyecto
- Sesiones recientes con su summary
- ADRs indexados (ID + snippet de contexto)
- Plans, bugs, tickets, backlog, knowledge
- Hint al final sobre qué hacer para profundizar

Este es el "handoff perfecto" entre agentes. No hay ambigüedad sobre qué leer primero. 10/10.

### 2. Estructura de carpetas del proyecto engineering
Las subcarpetas `decisions/`, `plans/`, `bugs/`, `knowledge/`, `sessions/` son intuitivas y alineadas con cómo piensan los ingenieros. El scaffold lazy (se crea al primer uso) es una decisión correcta — no hay cruft vacío al iniciar un proyecto.

### 3. Templates de Kioku (`99-system/templates/kioku/`)
Los 9 templates (adr, bug, daily, idea, knowledge, plan, project-moc, session, ticket) tienen una calidad muy consistente:
- Todos usan `> [!callout]` como primer bloque informativo
- Todos tienen `project_link` y `date` en el callout
- Todos terminan con sección de "Related" o "References"
- El hint "Status lives in the frontmatter" es excelente para agentes — les dice que no busquen status en el body.

### 4. Naming convention del session file
`{fecha}-{hora}-{agente}.md` es perfecto para el handoff. Un agente futuro puede ver en `get_project_context` quién hizo qué y cuándo, sin ambigüedad.

### 5. `agents.md` como contrato explícito
La tabla "Cuándo usar cada tool" en agents.md es exactamente lo que un agente necesita. Es un decision tree claro que evita que el agente improvise.

---

## ⚠️ Problemas encontrados

### CRÍTICO — `read_note` tiene una API ambigua
Este fue el problema más grave durante la sesión. Llamé `read_note` de 4 formas diferentes antes de desistir y leer directamente del filesystem:

```
# Todos fallaron:
read_note(path: "20-execution/product-api/product-api.md")
read_note(folder: "20-execution/product-api", note_name: "product-api")
read_note(path: "20-execution/product-api/product-api")
read_note(note_name: "product-api")
```

El error devuelto es siempre el mismo genérico: `"An error occurred invoking 'read_note'"`. Esto no da ninguna pista al agente sobre qué está mal. Tuve que caer back al filesystem con `view_file`, lo que rompe la ilusión de que kioku es la fuente de verdad.

**Root cause probable:** el schema del tool no documenta claramente qué argumento acepta (`path`, `note_name`, `folder+note_name`). O hay un bug en la resolución de paths con carpetas anidadas.

**Impacto:** Un agente que solo tiene acceso a kioku (sin filesystem) quedaría bloqueado.

### MEDIO — El plan `draft` no se auto-actualiza a `done`
El plan `PLAN-2026-07-14-Implementar-API-CRUD-de-Productos` tiene todos sus steps marcados con `[x]` pero su frontmatter sigue en `status: draft`. La sesión terminó como `done` pero el plan no se sincronizó. Hay desincronización entre el estado real y el estado del documento.

Esto genera ruido en `get_project_context` → muestra el plan como activo cuando no lo está.

**Sugerencia:** `end_work_session` podría escanear los planes tocados en la sesión y si todos los steps tienen `[x]`, proponer o auto-cambiar el status a `done`.

### MEDIO — `search_notes` devuelve snippets truncados sin forma de paginar
El resultado de `search_notes` trunca el contenido del snippet con `…` y no hay paginación ni forma de obtener el contenido completo desde ese tool. Para leer el full content hay que llamar `read_note` (que falla, ver arriba) o recurrir al filesystem.

### LEVE — El MOC del proyecto (`product-api.md`) tiene secciones vacías
El MOC generado tiene `## About` con el placeholder `_(what this project is...)_` y `## Key links` con campos vacíos. Esto es ruido para `get_project_context` que lo incluye verbatim.

**Sugerencia:** `get_project_context` podría filtrar las secciones que son solo placeholders del template antes de devolver el MOC. O el agente debería llenar el About al hacer `start_work_session`.

### LEVE — No hay tool `update_plan_step` / `check_step`
Para marcar un step como completado dentro de un plan, el agente tiene que usar `append_to_note` o `update_note_content`, que son tools genéricas. Una tool específica `complete_plan_step(plan, step_index)` o similar haría el tracking mucho más limpio y evitaría que los planes queden en `draft` para siempre.

---

## 💡 Sugerencias de mejora

### 1. Mejorar ergonomía de `read_note`
```yaml
# Opción A: aceptar path relativo al vault (sin extensión)
read_note(path: "20-execution/product-api/product-api")

# Opción B: aceptar path completo con extensión
read_note(path: "20-execution/product-api/product-api.md")

# Opción C: búsqueda por title dentro de un folder
read_note(title: "product-api", folder: "20-execution/product-api")
```
Lo más importante: **mensajes de error descriptivos**. Si el path no existe, decirlo. Si el argumento está mal formateado, decirlo.

### 2. `get_project_context` — filtrar placeholders del MOC
En vez de devolver el MOC verbatim (incluyendo los `_(placeholder)_`), el tool podría omitir secciones que son solo texto de template sin contenido real. Esto reduciría el ruido en contexto.

### 3. Agregar `complete_plan_step(project, plan_name, step_index)` 
Tool específica para marcar steps de planes. Beneficio secundario: podría auto-detectar si todos los steps están completos y cambiar el status del plan a `done` automáticamente.

### 4. `end_work_session` — prompt para actualizar plan status
Al finalizar sesión, si el agente registró pasos completados en un plan, `end_work_session` podría verificar si el plan está completo y auto-actualizarlo. Esto cierra el loop session ↔ plan.

### 5. Tool `summarize_project(project)` diferente a `get_project_context`
`get_project_context` devuelve mucho contenido. Para un agente que solo quiere saber "¿en qué estado está este proyecto?" sin todo el detalle, una tool más ligera sería útil. Podría devolver solo: status, last session summary, open bugs count, active plans count.

### 6. Mejorar el error message del MCP
Todos los errores de tools fallidas devuelven `"An error occurred invoking 'X'"`. Añadir el stack trace o al menos el mensaje de la excepción (incluso oculto en un campo `debug`) ayudaría enormemente durante el desarrollo del MCP.

---

## 📊 Evaluación por área

| Área | Nota | Comentario |
|---|---|---|
| Concepto / diseño del flujo | ⭐⭐⭐⭐⭐ | Sólido, coherente, bien pensado |
| `get_project_context` | ⭐⭐⭐⭐⭐ | La mejor tool, funciona perfecto |
| Templates de documentos | ⭐⭐⭐⭐½ | Consistentes, bien estructurados |
| `agents.md` como contrato | ⭐⭐⭐⭐⭐ | Muy útil para agentes |
| `read_note` / lectura | ⭐⭐ | Bloqueante — API ambigua, errores opacos |
| Tracking de estado (plan/session) | ⭐⭐⭐ | Desincronización plan↔session |
| Mensajes de error | ⭐ | Genéricos, sin información útil |
| Búsqueda (`search_notes`) | ⭐⭐⭐⭐ | Funciona bien, snippets útiles |

---

## Conclusión

El flujo de engineering de Kioku como **sistema de handoff entre agentes** es genuinamente valioso y superior a no tener nada. El agente que retoma un proyecto sabe inmediatamente qué se hizo, qué se decidió, qué bugs hay y qué está en el backlog.

El fix más urgente es `read_note` con errores descriptivos — es el único tool que bloqueó completamente el flujo. El resto son mejoras de calidad de vida que harán el sistema más robusto con el tiempo.


## consulta sobre creacion de sesion:
> dime tu porque no creaste una nueva sesion ?

▸ Thought for 3s
  The user is asking why I didn't create a new work session using Kioku's `start_w...
  Honestamente, por dos razones concretas:
  ──────
  ### 1. No leí  agents.md  antes de actuar

  El flujo correcto según tu propia documentación era:

    get_project_context → start_work_session → [trabajo] → end_work_session

  Yo hice  get_project_context  (bien), pero salté directamente al trabajo sin  start_work_session . Lo que debí haber hecho:

    start_work_session(project="product-api", goal="Leer contexto y dar feedback del MCP kioku")

  No lo hice porque traté la tarea como una consulta de lectura, no como una "sesión de trabajo". Error de clasificación de mi parte.
  ──────
  ### 2. El MCP no me lo exigió ni me lo sugirió

  Este es el feedback más valioso que te puedo dar: el agente no tiene fricción suficiente para seguir el flujo. Si  get_project_context  hubiera respondido algo como:

  │  ⚠ No hay sesión activa para este proyecto. ¿Deseas iniciar una con start_work_session antes de continuar?

  Yo lo hubiera seguido automáticamente.

  Actualmente el flujo es opt-in total — el agente tiene que decidir solo seguirlo. Eso es frágil.