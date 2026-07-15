## Estado de seguimiento

Revisión en vivo realizada el 2026-07-15 contra `Cortex-L7-Test-vault` con la build instalada
desde `develop`. Los estados `[COMPLETADO]` reflejan correcciones implementadas y verificadas.

#### [COMPLETADO] ALTO — `get_vault_snapshot` falla sin diagnóstico

La herramienta respondió exactamente:

```text
An error occurred invoking 'get_vault_snapshot'.
```

Ocurrió tanto al inicio como después del cleanup y del `rebuild_index`. No devuelve stack trace,
causa ni contexto. Esto bloquea una vista consolidada del vault y hace imposible distinguir entre
un error de serialización, una consulta defectuosa o un problema del índice.

**Propuesta:** devolver un error estructurado con `code`, mensaje de causa y etapa fallida; añadir
un test de integración sobre un vault con notas, links, tags y componentes desconectados.

**Resolución:** el snapshot ya no falla con el error genérico. La llamada MCP respondió
`[ok] Vault snapshot — 311 notes` en vivo, y el manejo de resolución de notas evita colisiones por
basename. Se añadieron regresiones para el grafo y el snapshot.

#### [COMPLETADO] ALTO — `create_note(kind="zettel")` no conserva el título solicitado

Solicité `kioku-live-test-zettel-20260715`, pero la herramienta creó:
`2026-07-15-09-53-56.md` y solo dejó el título solicitado en el contenido/frontmatter auxiliar.
El resultado sí devolvió la ruta real, pero un agente que construya un wikilink con el nombre
solicitado crea un enlace no resoluble. En la prueba, `suggest_links` rechazó el título solicitado
y solo aceptó el ID de timestamp.

**Propuesta:** documentar explícitamente que el filename es el ID zettel, o aceptar un alias/title
resoluble y devolver siempre `path`, `title` e `id` como campos separados. La creación debería
evitar que el contrato de `name` parezca ser el nombre de archivo.

**Resolución:** el zettel conserva el título como alias y la respuesta devuelve `path`, `id`,
`title` y `link`. La prueba en vivo resolvió correctamente el zettel usando el título solicitado.

#### [COMPLETADO CON CONFIGURACIÓN] MEDIO — Las escrituras no mantienen `updated`

La nota normal creada llevaba `date`, pero no `updated`. Después de `edit_note`,
`update_frontmatter` y `move_note`, la metadata continuó sin `updated`. Esto confirma en vivo la
fricción descrita en la propuesta 1: agentes que escriben fuera de Obsidian dejan el timestamp
incompleto hasta que Linter/Obsidian vuelve a guardar la nota.

**Propuesta:** aplicar la propuesta 1: emitir `updated` al crear y actualizarlo en toda operación
mutante, configurable para vaults que no lo requieran.

**Resolución:** las operaciones mutantes mantienen `updated` cuando
`frontmatter.maintain_updated: true` está configurado. El default sigue siendo `false` para no
competir con Obsidian Linter; en el vault de prueba `updated: null` fue por tanto el resultado
esperado. Hay cobertura unitaria para ambos modos.

#### [COMPLETADO CON CONFIGURACIÓN] MEDIO — MOCs generados pueden quedar obsoletos tras mutaciones

El MOC de la carpeta de prueba conservó el tag anterior de una nota después de que
`update_frontmatter` lo cambiara. El movimiento sí actualizó wikilinks, pero no regeneró el
contenido derivado del MOC.

**Propuesta:** documentar que el MOC es snapshot, o regenerarlo de forma explícita después de
`update_frontmatter`, `move_note` y `delete_note`. Si se elige regeneración automática, debe
evitar sobrescribir contenido manual fuera de la sección administrada.

**Resolución:** los MOCs y folder-readmes incluyen marcadores de secciones administradas y
metadatos de procedencia. El refresh automático se habilita con
`generated_indexes.refresh: on_mutation`; el default es manual y los índices legacy sin marcadores
no se sobrescriben. La creación de un MOC en vivo produjo correctamente la sección administrada.

#### [COMPLETADO] MEDIO — Herramientas opcionales no están expuestas en esta sesión

Aunque el vault está configurado y el estado del servidor es saludable, este agente no recibió
las herramientas de research, bridge, plugin, assets, generation y CSS. No fue posible probar:

`audit_citations`, `export_citations`, `import_bibtex`, `edit_in_obsidian`, `get_obsidian_state`,
`open_note_in_obsidian`, `trigger_obsidian_command`, `apply_template`, `get_installed_plugins`,
`lint`, `query_dataview`, `find_orphan_assets`, `tidy_attachments`, `generate_flashcards`,
`summarize_note` y `manage_css_snippets`.

Esto impide afirmar que se probaron literalmente todas las tools del servidor desde este cliente.
**Propuesta:** exponer un diagnóstico de capabilities al iniciar la sesión MCP, indicando qué
grupos están registrados y por qué los opcionales están deshabilitados.

**Resolución:** `get_server_status` ahora reporta los grupos habilitados y deshabilitados. En la
prueba en vivo informó `tasks, organization, sessions, workflows, graph, engineering` habilitados
y `research, generation, css, assets, bridge, plugin` deshabilitados por default. Las tools
opcionales siguen siendo opt-in y no se afirma que hayan sido probadas en esta sesión.

### Observaciones de ergonomía

- `[COMPLETADO]` `manage_trash(action="list")` admite prefijo, límite, offset y orden estable.
- `[COMPLETADO]` `list_tasks(status="open")` admite límite, offset y orden estable; la prueba en
  vivo devolvió metadatos `total`, `offset`, `limit` y `returned`.
- `[COMPLETADO]` `process_inbox` usa la carpeta configurada cuando se omite el argumento y explica
  cuál es la configuración efectiva cuando se proporciona una carpeta distinta.
- `[COMPLETADO]` `get_project_context` filtra los placeholders del MOC y devuelve contexto de
  proyecto con límites y filtros por tipo.

#### [COMPLETADO] ALTO — Eliminación concurrente con basenames iguales colisiona en `.trash`

Durante la prueba en vivo se crearon temporalmente dos notas con el mismo basename:

- `Kioku-Live-Check/Duplicate-A/shared.md`
- `Kioku-Live-Check/Duplicate-B/shared.md`

Se enviaron dos llamadas concurrentes a `delete_note`. Ambas respondieron que habían movido la
nota a `.trash/shared.md`, pero solo una copia quedó en el trash. La secuencia
`File.Exists` + `File.Move` no es atómica y permite que dos operaciones elijan el mismo destino,
con riesgo de pérdida de una nota durante un soft-delete concurrente.

**Propuesta:** reservar el nombre de destino de forma atómica o proteger la generación de nombres
de trash con un lock por vault. Añadir una prueba concurrente que elimine dos notas con el mismo
basename y verifique que existan dos archivos recuperables con nombres distintos.

**Resolución:** la selección del nombre en `.trash` y el `File.Move` del soft-delete ahora forman
una sección crítica atómica protegida por un lock por vault
(`ConcurrentDictionary<string, SemaphoreSlim>`, el mismo patrón que la asignación de números ADR).
Además, el movimiento usa `File.Move(..., overwrite: false)` para que una colisión falle de forma
ruidosa en lugar de perder una nota. Se añadió una regresión concurrente
(`DeleteNote_ConcurrentSameBasename_KeepsBothRecoverable`) que elimina en paralelo dos notas con el
mismo basename y verifica que ambas queden recuperables en el trash con nombres distintos; el test
falla de forma determinista sobre el código previo y pasa con la corrección.

### Resultado final

La batería fue satisfactoria para el núcleo del MCP: lectura, búsqueda, escritura, organización,
workflow de ingeniería, sesiones, grafo y recuperación. El índice terminó saludable con 311 notas
activas. Las notas temporales de la prueba fueron movidas de forma recuperable al trash.

Los bugs originales de snapshot, contrato de zettels, timestamps configurables, MOCs, diagnóstico
de capabilities, trash, tasks, inbox y contexto de proyectos están corregidos o documentados como
opt-in. La colisión de nombres en `.trash` bajo eliminación concurrente también quedó corregida
haciendo atómica la selección de nombre y el movimiento. No quedan bugs pendientes en esta batería.
