#### ALTO — `get_vault_snapshot` falla sin diagnóstico

La herramienta respondió exactamente:

```text
An error occurred invoking 'get_vault_snapshot'.
```

Ocurrió tanto al inicio como después del cleanup y del `rebuild_index`. No devuelve stack trace,
causa ni contexto. Esto bloquea una vista consolidada del vault y hace imposible distinguir entre
un error de serialización, una consulta defectuosa o un problema del índice.

**Propuesta:** devolver un error estructurado con `code`, mensaje de causa y etapa fallida; añadir
un test de integración sobre un vault con notas, links, tags y componentes desconectados.

#### ALTO — `create_note(kind="zettel")` no conserva el título solicitado

Solicité `kioku-live-test-zettel-20260715`, pero la herramienta creó:
`2026-07-15-09-53-56.md` y solo dejó el título solicitado en el contenido/frontmatter auxiliar.
El resultado sí devolvió la ruta real, pero un agente que construya un wikilink con el nombre
solicitado crea un enlace no resoluble. En la prueba, `suggest_links` rechazó el título solicitado
y solo aceptó el ID de timestamp.

**Propuesta:** documentar explícitamente que el filename es el ID zettel, o aceptar un alias/title
resoluble y devolver siempre `path`, `title` e `id` como campos separados. La creación debería
evitar que el contrato de `name` parezca ser el nombre de archivo.

#### MEDIO — Las escrituras no mantienen `updated`

La nota normal creada llevaba `date`, pero no `updated`. Después de `edit_note`,
`update_frontmatter` y `move_note`, la metadata continuó sin `updated`. Esto confirma en vivo la
fricción descrita en la propuesta 1: agentes que escriben fuera de Obsidian dejan el timestamp
incompleto hasta que Linter/Obsidian vuelve a guardar la nota.

**Propuesta:** aplicar la propuesta 1: emitir `updated` al crear y actualizarlo en toda operación
mutante, configurable para vaults que no lo requieran.

#### MEDIO — MOCs generados pueden quedar obsoletos tras mutaciones

El MOC de la carpeta de prueba conservó el tag anterior de una nota después de que
`update_frontmatter` lo cambiara. El movimiento sí actualizó wikilinks, pero no regeneró el
contenido derivado del MOC.

**Propuesta:** documentar que el MOC es snapshot, o regenerarlo de forma explícita después de
`update_frontmatter`, `move_note` y `delete_note`. Si se elige regeneración automática, debe
evitar sobrescribir contenido manual fuera de la sección administrada.

#### MEDIO — Herramientas opcionales no están expuestas en esta sesión

Aunque el vault está configurado y el estado del servidor es saludable, este agente no recibió
las herramientas de research, bridge, plugin, assets, generation y CSS. No fue posible probar:

`audit_citations`, `export_citations`, `import_bibtex`, `edit_in_obsidian`, `get_obsidian_state`,
`open_note_in_obsidian`, `trigger_obsidian_command`, `apply_template`, `get_installed_plugins`,
`lint`, `query_dataview`, `find_orphan_assets`, `tidy_attachments`, `generate_flashcards`,
`summarize_note` y `manage_css_snippets`.

Esto impide afirmar que se probaron literalmente todas las tools del servidor desde este cliente.
**Propuesta:** exponer un diagnóstico de capabilities al iniciar la sesión MCP, indicando qué
grupos están registrados y por qué los opcionales están deshabilitados.

### Observaciones de ergonomía

- `manage_trash(action="list")` devuelve las 178 notas históricas completas en cada llamada; un
  filtro por fecha, prefijo o límite reduciría ruido y coste de contexto.
- `list_tasks(status="open")` sobre todo el vault produce una salida muy grande. El filtro por
  carpeta funciona, pero convendría que el límite/paginación fuera más visible en el contrato.
- `process_inbox` respondió correctamente que `Inbox` no existe. El mensaje es claro, pero podría
  devolver la carpeta configurada efectiva para que el agente no tenga que adivinarla.
- `get_project_context` todavía incluye placeholders del MOC (`About`, `Key links`). Filtrarlos
  o marcarlos como pendientes reduciría ruido en handoffs.

### Resultado final

La batería fue satisfactoria para el núcleo del MCP: lectura, búsqueda, escritura, organización,
workflow de ingeniería, sesiones y recuperación. El vault quedó limpio y el índice saludable.
Los siguientes fixes tienen prioridad práctica: diagnosticar `get_vault_snapshot`, hacer explícito
el contrato de filenames zettel y mantener `updated`; después conviene resolver la exposición de
capabilities opcionales para que una prueba de "todas las tools" sea realmente completa.
