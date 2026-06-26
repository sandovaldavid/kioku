# Kioku — Referencia de Comandos

> **Versiones:** v1 (stdio) · v2 (HTTP-SSE + Semántica) · v3 (Ecosystem Tools — completado)  
> **Referencia:** [planning.md](./planning.md) · [v2-http-sse-spec.md](./v2-http-sse-spec.md)

Este documento es el inventario oficial de todos los comandos del ecosistema Kioku:
- **MCP Tools** → expuestos al agente de IA a través del protocolo MCP.
- **Plugin Commands** → comandos que el motor C# envía al plugin de Obsidian vía WebSocket.

Cada comando incluye su estado de implementación, versión objetivo, y justificación de por qué existe.

---

## Leyenda

| Símbolo | Significado |
|---|---|
| ✅ Implementado | Disponible y funcional en `develop` |
| 🔨 En desarrollo | En construcción para la versión actual |
| 📋 Planificado | En backlog, confirmado para una rama futura |
| 💡 Propuesto | Evaluando viabilidad e impacto |
| ❌ Descartado | Fuera del alcance — ver justificación |

---

## Parte 1: MCP Tools (Servidor C#)

Comandos que el agente de IA (Claude Code, agy) puede invocar directamente a través del protocolo MCP. Cada tool está decorada con `[McpServerTool]` en C#.

---

### 📖 Grupo 1: Lectura y Consulta de Notas

Estas herramientas son de **solo lectura** — no modifican la bóveda.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 1 | `read_note` | v1 | ✅ | Lee el contenido completo de una nota por ruta o nombre |
| 2 | `list_notes` | v1 | ✅ | Lista todas las notas de la bóveda (o de una carpeta) |
| 3 | `search_notes` | v1 | ✅ | Búsqueda de texto en título, contenido y tags |
| 4 | `filter_notes` | v1 | ✅ | Filtra notas por frontmatter (tags, fecha, estado, tipo) |
| 5 | `get_note_metadata` | v1 | ✅ | Lee solo el frontmatter YAML de una nota |
| 6 | `get_backlinks` | v1 | ✅ | Devuelve todas las notas que enlazan a una nota dada |
| 7 | `get_outgoing_links` | v1 | ✅ | Devuelve todos los wikilinks que salen de una nota |
| 8 | `search_notes_semantic` | v2 | ✅ | Búsqueda por similitud conceptual (embeddings Ollama) |
| 9 | `search_notes_hybrid` | v2 | ✅ | Búsqueda combinada: texto + semántica (RRF) |
| 10 | `find_similar_notes` | v2 | ✅ | Notas conceptualmente similares a una nota dada |
| 11 | `get_note_embedding` | v2 | ✅ | Devuelve el embedding de una nota (debug/diagnóstico) |
| 12 | `get_recent_activity` | v3 | ✅ | N notas modificadas más recientemente (por `mtime`) |
| 13 | `get_work_context` | v3 | ✅ | Snapshot de la bóveda: inbox, drafts, sesión activa |
| 14 | `get_knowledge_timeline` | v3 | ✅ | Notas ordenadas por `date` en un rango de fechas |
| 15 | `get_concept_map` | v3 | ✅ | Grafo de notas relacionadas como JSON (nodos + aristas) |
| 16 | `get_vault_snapshot` | v3 | ✅ | Árbol de carpetas + top tags + stats en un solo llamado |

#### Justificaciones

- **`read_note`**: Operación fundamental. El agente necesita leer el contenido completo antes de razonar sobre él.
- **`list_notes`**: Permite al agente tener una vista de la bóveda completa para planear acciones.
- **`search_notes`**: Búsqueda rápida por texto. Esencial para localizar notas por tema sin iterar toda la bóveda.
- **`filter_notes`**: Permite al agente filtrar subconjuntos específicos (ej. "todas las notas con status: draft del mes pasado").
- **`get_note_metadata`**: Más eficiente que `read_note` cuando solo se necesitan los metadatos YAML.
- **`get_backlinks` / `get_outgoing_links`**: Permiten al agente navegar el grafo de conocimiento de la bóveda.
- **`search_notes_semantic`**: Para bóvedas donde los términos exactos no coinciden pero el concepto sí (ej. buscar "redes neuronales recurrentes" y encontrar notas sobre "LSTM").
- **`search_notes_hybrid`**: Combina la precisión del texto con la flexibilidad semántica — mejor resultado general.
- **`find_similar_notes`**: El agente puede sugerir conexiones no obvias entre notas.
- **`get_recent_activity`**: Solo `File.GetLastWriteTime()` — implementación trivial, alto valor para retomar sesiones.
- **`get_work_context`**: El agente no tiene memoria entre sesiones — este snapshot le da el estado en un llamado.
- **`get_knowledge_timeline`**: Ver cómo evolucionó el conocimiento sobre un tema en el tiempo.
- **`get_concept_map`**: Habilita visualizaciones externas del grafo y razonamiento sobre la red de conocimiento.
- **`get_vault_snapshot`**: Reemplaza `list_notes + get_vault_stats + múltiples get_note_metadata` — reduce tokens.

---

### ✏️ Grupo 2: Escritura y Modificación de Notas

Estas herramientas **modifican la bóveda**. Requieren confirmación implícita del agente.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 17 | `create_note` | v1 | ✅ | Crea una nueva nota con frontmatter y contenido |
| 18 | `update_note_content` | v1 | ✅ | Reemplaza o edita el contenido de una nota |
| 19 | `append_to_note` | v1 | ✅ | Añade texto al final de una nota (útil para bitácoras) |
| 20 | `prepend_to_note` | v1 | ✅ | Añade texto al inicio del cuerpo (después del frontmatter) |
| 21 | `update_frontmatter` | v1 | ✅ | Actualiza o añade campos en el frontmatter YAML |
| 22 | `add_tag` | v1 | ✅ | Añade uno o más tags a una nota |
| 23 | `remove_tag` | v1 | ✅ | Elimina un tag de una nota |
| 24 | `move_note` | v1 | ✅ | Mueve una nota a otra carpeta |
| 25 | `rename_note` | v1 | ✅ | Renombra una nota |
| 26 | `delete_note` | v1 | 💡 | Elimina una nota (requiere confirmación explícita) |

#### Justificaciones

- **`create_note`**: Permite al agente crear notas de resumen, notas de sesión de trabajo, o nuevas entradas.
- **`update_note_content`**: Para que el agente pueda editar notas existentes directamente.
- **`append_to_note`**: Patrón muy común en flujos de bitácora. El agente añade un registro sin tocar el contenido previo.
- **`prepend_to_note`**: Útil para añadir un TL;DR o resumen ejecutivo al inicio de notas largas.
- **`update_frontmatter`**: Permite clasificar o cambiar el status de una nota sin editar su cuerpo.
- **`add_tag` / `remove_tag`**: Operaciones frecuentes de clasificación y reorganización de la taxonomía de tags.
- **`delete_note`**: Marcado como 💡 porque es destructivo. Evaluar si se mueve a la papelera de Obsidian.
- **`move_note` / `rename_note`**: Cruciales para reorganización. Deben actualizar los wikilinks referenciadores.

---

### ✅ Grupo 3: Task Management

Herramientas para gestión de tareas nativas de Obsidian (compatible con el plugin Tasks).

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 27 | `list_tasks` | v3 | ✅ | Lista todas las tareas (checkboxes) en la bóveda |
| 28 | `complete_task` | v3 | ✅ | Marca una tarea como completada (`[x]`) |
| 29 | `reopen_task` | v3 | ✅ | Reabre una tarea completada (`[ ]`) |
| 30 | `list_tasks_by_tag` | v3 | ✅ | Filtra tareas por tag o contexto |
| 31 | `list_overdue_tasks` | v3 | ✅ | Lista tareas con fecha de vencimiento pasada |
| 32 | `extract_action_items` | v3 | ✅ | Consolida checkboxes de una nota en una nota de tareas |

#### Justificaciones

- **`list_tasks`**: El agente puede ver todas las tareas pendientes sin que el usuario abra Obsidian.
- **`complete_task` / `reopen_task`**: Flujo bidireccional — el agente gestiona tareas igual que el usuario.
- **`list_tasks_by_tag`**: Para bóvedas GTD con contextos (`@home`, `@work`, `@computer`).
- **`list_overdue_tasks`**: El agente puede hacer un "daily briefing" de tareas atrasadas al inicio de la sesión.
- **`extract_action_items`**: Puente entre notas de reunión y el sistema de tareas — sin depender del plugin Tasks.

---

### 🧠 Grupo 4: Zettelkasten y Gestión del Conocimiento

Herramientas para construir y navegar la red de conocimiento Zettelkasten.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 33 | `create_zettel` | v3 | ✅ | Crea una nota Zettel atómica con ID único |
| 34 | `create_moc` | v3 | ✅ | Crea un Map of Content (MOC) para un tema |
| 35 | `create_folder_readme` | v3 | ✅ | Genera un folder note ({Folder}.md) para una carpeta de la bóveda (máx. nivel 2) — compatible con Folder Notes plugin |
| 36 | `link_related_notes` | v3 | ✅ | Encuentra notas relacionadas y agrega wikilinks |
| 37 | `create_literature_note` | v3 | ✅ | Crea nota de literatura con frontmatter estándar |
| 38 | `create_note_from_template` | v3 | ✅ | Crea nota desde template con variables `{{ var }}` |
| 39 | `list_templates` | v3 | ✅ | Lista templates disponibles en la carpeta configurada |
| 40 | `create_template` | v3 | ✅ | Guarda un nuevo template en la carpeta de templates |

#### Justificaciones

- **`create_zettel`**: El ID único (timestamp) garantiza permanencia de los wikilinks aunque se renombre la nota.
- **`create_moc`**: Los MOCs son el mecanismo de indexación en Zettelkasten — el agente los genera automáticamente.
- **`create_folder_readme`**: Crea un folder note ({Folder}.md) listando todas las notas de la carpeta. Máximo nivel 2. Compatible con el plugin Folder Notes de Obsidian.
- **`link_related_notes`**: Construye el grafo de conocimiento automáticamente usando embeddings existentes.
- **`create_literature_note`**: Estándar para bóvedas académicas — citekey, DOI, autores, año en frontmatter.
- **`create_note_from_template`**: El patrón más frecuente del día a día — evita 4-5 tool calls encadenados.
- **`list_templates`**: El agente necesita saber qué templates existen antes de elegir uno.

---

### 🗂️ Grupo 5: Organización y Taxonomía

Herramientas para mantener la bóveda ordenada y bien clasificada.

| # | Tool Name | Versión | Estado | Patrón | Descripción |
|---|---|---|---|---|---|
| 41 | `reorder_notes_in_folder` | v3 | ✅ | dry_run | Renombra notas con prefijo numérico para definir orden |
| 42 | `normalize_tags` | v3 | ✅ | dry_run | Estandariza capitalización y formato de tags en toda la bóveda |
| 43 | `rename_tag_globally` | v3 | ✅ | dry_run | Renombra un tag en todas las notas de la bóveda |
| 44 | `merge_tags` | v3 | ✅ | dry_run | Fusiona dos tags en uno |
| 45 | `reclassify_note` | v3 | ✅ | — | Mueve una nota a la carpeta más apropiada según su contenido |
| 46 | `suggest_tags` | v3 | ✅ | — | Sugiere tags relevantes para una nota basándose en el contenido |
| 47 | `suggest_folder` | v3 | ✅ | — | Sugiere la carpeta más adecuada para una nota |
| 48 | `find_duplicate_notes` | v3 | ✅ | dry_run | Detecta notas con contenido similar (posibles duplicados) |
| 49 | `audit_vault` | v3 | ✅ | — | Reporte de salud: notas sin tags, sin fecha, wikilinks rotos |
| 68 | `find_unlinked_notes` | v3 | 📋 | — | Notas sin backlinks NI outgoing links (islas del grafo) |
| 69 | `find_graph_islands` | v3 | 📋 | — | Componentes conexos pequeños (< N notas) no conectados al principal |
| 70 | `measure_vault_density` | v3 | 📋 | — | Métricas globales: avg backlinks, ratio de notas con tags/frontmatter |

> **Patrón `dry_run`:** Los tools marcados aceptan `dry_run: bool = false`. Con `true` devuelven un preview de qué cambiaría sin ejecutar nada.

#### Justificaciones

- **`normalize_tags`**: Con 500 notas, es común acumular variaciones del mismo tag (`machine-learning`, `MachineLearning`, `ML`).
- **`rename_tag_globally`**: Refactoring de taxonomía. Cambiar un tag en cientos de notas manualmente es inviable.
- **`merge_tags`**: Elimina redundancias en la taxonomía fusionando tags semánticamente equivalentes.
- **`reclassify_note`**: El agente analiza el contenido y mueve la nota a la carpeta más apropiada.
- **`find_duplicate_notes`**: Con 500+ notas, es probable tener duplicados. El agente los detecta y propone fusionarlos.
- **`audit_vault`**: Dashboard de calidad. Devuelve schema estructurado `{ summary, items: [{path, issue, severity}] }`.

---

### 🖼️ Grupo 6: Assets, Theming y Archivos No-Markdown

Herramientas para trabajar con activos visuales, bases de datos y personalización visual.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 50 | `list_excalidraw_files` | v3 | ✅ | Lista todos los diagramas Excalidraw de la bóveda |
| 51 | `get_asset_metadata` | v3 | ✅ | Metadatos de un asset (nombre, ubicación, tamaño, fecha) |
| 52 | `find_orphan_assets` | v3 | ✅ | Assets no referenciados por ninguna nota |
| 53 | `find_broken_links` | v3 | ✅ | Wikilinks rotos en toda la bóveda |
| 54 | `list_database_tables` | v3 | 💡 | Lista las bases de datos de DB Folder / Dataview |
| 55 | `query_database_table` | v3 | 💡 | Consulta filas de una base de datos nativa de Obsidian |
| 56 | `apply_css_snippet` | v3 | ✅ | Crea/actualiza un snippet CSS en `.obsidian/snippets/` |
| 57 | `list_css_snippets` | v3 | ✅ | Lista snippets CSS con estado enabled/disabled |
| 71 | `normalize_attachment_names` | v3 | 📋 | Renombra attachments con patrón consistente + actualiza referencias (dry_run) |
| 72 | `move_attachments_to_folder` | v3 | 📋 | Centraliza attachments dispersos en carpeta estándar + actualiza refs |
| 77 | `remove_css_snippet` | v3 | 📋 | Elimina un snippet CSS de .obsidian/snippets/ y lo quita de app.json |

#### Justificaciones

- **`list_excalidraw_files`**: El agente puede referenciar diagramas existentes al escribir notas.
- **`find_orphan_assets`**: Con 500 notas, es común acumular imágenes y diagramas que nadie referencia.
- **`find_broken_links`**: Detecta wikilinks que apuntan a notas que ya no existen.
- **`apply_css_snippet`**: Escribe en `.obsidian/snippets/` y llama `reload-snippets` — sin APIs privadas. Permite "modo sepia", fuentes personalizadas, colorizar tags por categoría.
- **`list_css_snippets`**: El agente necesita saber qué snippets existen antes de agregar/modificar.

---

### 🔬 Grupo 7: Research y Academic

Herramientas para gestión de conocimiento académico y exportación de referencias.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 58 | `export_citations` | v3 | ✅ | Exporta notas con `citekey` en formato `.bib` o Markdown |
| 59 | `get_literature_gap` | v3 | ✅ | Citekeys referenciados en el texto sin nota propia |
| 60 | `import_zotero_item` | v3 | 💡 | Crea nota de literatura desde Zotero API local |
| 73 | `validate_research_notes` | v3 | 📋 | Valida que notas type:literature/research tengan citekey, year, authors, status |

#### Justificaciones

- **`export_citations`**: Solo escanea frontmatter `citekey` — lectura pura del índice existente. Genera `.bib` o lista Markdown.
- **`get_literature_gap`**: Detecta papers citados que no tienen su propia nota de lectura — identifica qué falta leer.
- **`import_zotero_item`**: 💡 porque depende de Zotero desktop activo en `localhost:23119` — dependencia frágil.

---

### 🕐 Grupo 8: Sesión de Trabajo y Contexto

Herramientas para gestionar el estado y continuidad entre sesiones de trabajo.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 61 | `start_work_session` | v3 | ✅ | Crea nota de sesión con timestamp y notas abiertas actuales |
| 62 | `end_work_session` | v3 | ✅ | Cierra sesión: agrega resumen de notas creadas/modificadas |
| 75 | `list_work_sessions` | v3 | 📋 | Lista notas de sesión con fecha, duración y estado abierta/cerrada |
| 76 | `get_session_activity` | v3 | 📋 | Notas creadas/modificadas durante una sesión específica |

#### Justificaciones

- **`start_work_session`**: Macro sobre `create_note` + `get-open-notes` — registra el estado de la bóveda al iniciar.
- **`end_work_session`**: Macro sobre `append_to_note` — genera el log automático de la sesión de trabajo.

---

### 🔧 Grupo 9: Utilidades del Sistema

Herramientas de diagnóstico e información del servidor.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 63 | `get_vault_stats` | v1 | ✅ | Estadísticas de la bóveda (total notas, tags, carpetas) |
| 64 | `get_index_status` | v1 | ✅ | Estado del índice en memoria y última actualización |
| 65 | `rebuild_index` | v1 | ✅ | Fuerza una re-indexación completa de la bóveda |
| 66 | `ping` | v1 | ✅ | Verificación de que el servidor está activo (health check) |

---

### 🔗 Grupo 10: Ecosystem e Integración Git

Herramientas para interactuar con el entorno externo (git, configuración) que rodea la bóveda.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 78 | `get_git_status` | v3 | 📋 | `git status --short` sobre el vault (archivos modificados/añadidos/borrados) |
| 79 | `list_git_commits` | v3 | 📋 | Últimos N commits con hash, mensaje y fecha |
| 80 | `create_git_commit` | v3 | 💡 | Stagea y crea commit con dry_run (destructivo — exige confirmación explícita) |

---

## Parte 2: Plugin Commands (Obsidian ↔ Motor C#)

Comandos que el motor C# envía al plugin de Obsidian vía WebSocket local. El plugin actúa como un **servidor WebSocket local** y el motor C# como cliente.

> Estos comandos solo funcionan cuando Obsidian está abierto. Si está cerrado, el motor C# ignora los comandos del plugin sin error.

---

### 🖥️ Grupo A: Navegación y Foco

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| A1 | `open-file` | v1 | ✅ | Abre y enfoca una nota específica en Obsidian |
| A2 | `get-active-note` | v1 | ✅ | Devuelve la nota que actualmente tiene el foco |
| A3 | `get-open-notes` | v1 | ✅ | Lista todas las notas actualmente abiertas en pestañas |
| A4 | `scroll-to-block` | v1 | ✅ | Desplaza la vista hasta un bloque específico (por ID de bloque) |
| A5 | `open-in-split` | v1 | ✅ | Abre una nota en un panel dividido sin cerrar la vista actual |
| A6 | `close-note` | v1 | 💡 | Cierra la pestaña de una nota específica |

#### Justificaciones

- **`open-file`**: Cuando el agente trabaja con una nota, el usuario puede querer verla. El agente la abre automáticamente.
- **`get-active-note`**: Permite al agente saber qué nota está leyendo el usuario en este momento.
- **`get-open-notes`**: El agente puede adaptar sus respuestas según qué notas tiene el usuario abiertas.
- **`scroll-to-block`**: Esencial cuando el agente referencia un fragmento específico de una nota larga.
- **`open-in-split`**: El usuario tiene una nota abierta y el agente necesita mostrarle una referencia sin interrumpir.

---

### ⚡ Grupo B: Ejecución de Comandos y UI

| # | Comando | Versión | Estado | API | Descripción |
|---|---|---|---|---|---|
| B1 | `trigger-command` | v1 | ✅ | pública | Ejecuta un comando nativo de Obsidian por su ID |
| B2 | `toggle-reading-mode` | v3 | ✅ | pública | Alterna modo edición/lectura en la nota activa |
| B3 | `get-selection` | v3 | ✅ | pública | Devuelve el texto seleccionado en el editor activo |
| B4 | `fold-all-headings` | v3 | ✅ | pública | Colapsa todos los headings (`editor:fold-all`) |
| B5 | `unfold-all-headings` | v3 | ✅ | pública | Expande todos los headings (`editor:unfold-all`) |
| B6 | `reload-snippets` | v3 | ✅ | pública | Recarga snippets CSS (`app:reload-css-snippets`) |
| B7 | `trigger-plugin-command` | v1 | 📋 | pública | Ejecuta un comando de otro plugin instalado |
| B8 | `open-command-palette` | v1 | 💡 | pública | Abre la paleta de comandos de Obsidian |

#### Justificaciones

- **`trigger-command`**: Puente hacia el ecosistema completo de Obsidian. Permite al agente ejecutar cualquier acción.
- **`toggle-reading-mode`**: Usa `app.commands.executeCommandById('markdown:toggle-preview')` — API pública, alto valor para flujos de review.
- **`get-selection`**: `editor.getSelection()` — elimina fricción: el usuario selecciona un párrafo y el agente puede operar directamente sobre él.
- **`fold-all-headings` / `unfold-all-headings`**: Útil cuando el agente quiere que el usuario vea la estructura macro de una nota larga.
- **`reload-snippets`**: Necesario para aplicar CSS snippets sin reiniciar Obsidian — usa `app:reload-css-snippets` (API pública).

---

### 🏛️ Grupo C: Información del Vault (desde Obsidian)

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| C1 | `get-vault-path` | v1 | ✅ | Ruta raíz de la bóveda activa |
| C2 | `is-obsidian-ready` | v1 | ✅ | Indica si Obsidian está completamente cargado y listo |
| C3 | `get-app-version` | v1 | ✅ | Versión de Obsidian y del plugin Kioku |
| C4 | `get-installed-plugins` | v1 | 💡 | Lista de plugins de comunidad instalados |

#### Justificaciones

- **`get-vault-path`**: El motor C# puede inferir la ruta por configuración, pero verificar con Obsidian elimina ambigüedad (múltiples bóvedas).
- **`is-obsidian-ready`**: Permite al motor C# esperar a que Obsidian cargue completamente antes de enviar comandos de navegación.

---

### ✏️ Grupo D: Creación Asistida (Solo si Obsidian está abierto)

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| D1 | `create-note-ui` | v2 | ✅ | Crea una nueva nota y la abre en Obsidian inmediatamente |
| D2 | `insert-at-cursor` | v2 | ✅ | Inserta texto en la posición del cursor del editor activo |
| D3 | `replace-selection` | v2 | ✅ | Reemplaza el texto seleccionado en el editor activo |
| D4 | `highlight-block` | v2 | 💡 | Resalta visualmente un bloque de texto en el editor |

#### Justificaciones

- **`create-note-ui`**: Diferente a `create_note` del servidor MCP (que crea en disco). Este comando crea la nota Y la abre en Obsidian.
- **`insert-at-cursor`**: El agente puede insertar contenido exactamente donde el usuario tiene el cursor.
- **`replace-selection`**: Flujo de refactoring asistido: el usuario selecciona un fragmento y el agente lo reescribe.

---

## Resumen de Estado por Versión

### v1 — MVP (Stdio Transport) ✅ COMPLETO

**MCP Tools:** 11 herramientas  
- Lectura (1–7), Escritura (17–25), Utilidades (63–66)

**Plugin Commands:** A1–A2, A3, B1, C1, C2  
**Criterio:** El agente puede leer, buscar, crear y modificar notas sin necesitar que Obsidian esté abierto.

### v2 — HTTP-SSE + Semántica ✅ COMPLETO

**Añade:** 4 MCP Tools  
- Búsqueda semántica e híbrida (8–11)

**Criterio:** El agente puede buscar por significado, no solo por texto literal. Embeddings via Ollama.

### v3 — Ecosystem Tools ✅ COMPLETADO

**Estado actual:** ~85 MCP Tools + 16 Plugin Commands — todas las ramas (F–N) implementadas

#### MCP Tools Completados
- Session Context (12–13, 61–62): `get_recent_activity`, `get_work_context`, `start/end_work_session`
- Task Management (27–31): `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`, `list_overdue_tasks`
- Task Extraction (32): `extract_action_items`
- Zettelkasten (33–37): `create_zettel`, `create_moc`, `create_folder_readme`, `link_related_notes`, `create_literature_note`
- Templates (38–40): `create_note_from_template`, `list_templates`, `create_template`
- Tag & Org (42–44, 46, 48–49, 53): `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`, `find_duplicate_notes`, `audit_vault`, `find_broken_links`
- CSS Theming (56–57): `apply_css_snippet`, `list_css_snippets`
- Knowledge Graph (14–16): `get_knowledge_timeline`, `get_concept_map`, `get_vault_snapshot`
- Research (58–59): `export_citations`, `get_literature_gap`
- Plugin Integrations (via `PluginIntegrationTools.cs`): `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins`, `fix_merge_conflicts`, `resolve_merge_conflict`

#### Plugin Commands Completados
- Navigation (A1–A3): `open-file`, `get-active-note`, `get-open-notes`
- Execution (B1–B6): `trigger-command`, `toggle-reading-mode`, `get-selection`, `fold-all`, `unfold-all`, `reload-snippets`
- Vault Info (C1–C3): `get-vault-path`, `is-obsidian-ready`, `get-app-version`
- Integration (via PluginIntegrationTools): `run-dataview-query`, `run-templater`, `run-linter`, `run-linter-vault`, `get-installed-plugins`

**Implementado (Ramas F, G, H):**
- Assets (50–52): `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`
- Organization (41, 45, 47): `reorder_notes_in_folder`, `reclassify_note`, `suggest_folder`
- Editor Commands (A4, A5, C3, D1–D3): `scroll-to-block`, `open-in-split`, `get-app-version`, `create-note-ui`, `insert-at-cursor`, `replace-selection`

**Resumen de conteos:**
| Categoría | Implementadas | Propuestas |
|-----------|:---:|:---:|
| MCP Tools | ~85 | 2 |
| Plugin Commands | 16 | 1 |
| **Total** | **~101** | **3** |

---

## Tools Descartadas (❌ No implementar)

| Tool | Razón |
|------|-------|
| `answer_from_vault` | Responsabilidad del LLM (Claude, agy) — no del servidor MCP. El RAG lo hace el agente con `hybrid_search_notes` + `read_note` |
| `process_inbox_note` / `cortex_process_inbox` | Hardcodea reglas de un vault específico — rompe la genericidad de Kioku |
| `summarize_note` | Requiere LLM externo — el servidor C# no razona, solo lee y escribe |
| `sunday_hygiene` | Demasiado vault-specific. El agente + tools individuales cubren esto sin necesitar una tool propia |
| `cross_reference_notes` | Requiere comprensión semántica profunda que `hybrid_search` solo aproxima — responsabilidad del LLM |
| `create_theme` (CSS nivel 3) | Usa `app.customCss.setTheme()` (API privada) + 400 variables CSS → riesgo de regresiones visuales |
| `export_note(pdf)` | Requiere Pandoc (dependencia de sistema) o Obsidian abierto — demasiado frágil para un servidor genérico |
| `watch_vault_changes` | Solo útil en HTTP-SSE con cliente compatible — muy pocos clientes MCP lo soportan hoy |
| `toggle-snippet` (plugin) | Usa `app.customCss.setCssEnabledStatus()` (API privada) — puede romperse en actualizaciones |

---

## Consideraciones para Publicación en Community Store

Los MCP Tools son internos al servidor C# y no requieren aprobación de Obsidian. El plugin de TypeScript sí debe seguir los estándares:

1. **Solo usar APIs públicas de Obsidian** — `app.workspace`, `app.vault`, `app.metadataCache`, `app.commands`.
2. **No hacer peticiones de red externas** desde el plugin — toda comunicación es local (WebSocket a `localhost`).
3. **Manejar correctamente `onunload`** — cerrar el WebSocket server al desinstalar el plugin.
4. **No almacenar datos de usuario fuera de la bóveda** — respetar la privacidad del usuario.
5. **Documentar el puerto WebSocket como configurable** en la pantalla de configuración del plugin.
6. **Seguir las guías de `manifest.json`** con `minAppVersion` apropiado.
7. **No usar `app.customCss`** (API privada) — usar `app.commands.executeCommandById('app:reload-css-snippets')` para snippets y modificar `appearance.json` directamente para cambio de tema.
