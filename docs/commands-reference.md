# Kioku — Referencia de Comandos

> **Versiones:** v1 (stdio) · v2 (HTTP-SSE + Semántica)
> **Referencia:** [planning.md](./planning.md) · [v2-http-sse-spec.md](./v2-http-sse-spec.md)

Este documento es el inventario oficial de todos los comandos del ecosistema Kioku:
- **MCP Tools** → expuestos al agente de IA a través del protocolo MCP.
- **Plugin Commands** → comandos que el motor C# envía al plugin de Obsidian vía WebSocket.

Cada comando incluye su estado de implementación, versión objetivo, y justificación de por qué existe.

---

## Leyenda

| Símbolo | Significado |
|---|---|
| ✅ Implementado | Disponible y funcional |
| 🔨 En desarrollo | En construcción para la versión actual |
| 📋 Planificado | En backlog, confirmado |
| 💡 Propuesto | Evaluando viabilidad e impacto |
| ❌ Descartado | Fuera del alcance |

---

## Parte 1: MCP Tools (Servidor C#)

Comandos que el agente de IA (Claude Code, agy) puede invocar directamente a través del protocolo MCP. Cada tool está decorada con `[McpServerTool]` en C#.

---

### 📖 Grupo 1: Lectura y Consulta de Notas

Estas herramientas son de **solo lectura** — no modifican la bóveda.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 1 | `read_note` | v1 | 🔨 | Lee el contenido completo de una nota por ruta o nombre |
| 2 | `list_notes` | v1 | 🔨 | Lista todas las notas de la bóveda (o de una carpeta) |
| 3 | `search_notes` | v1 | 🔨 | Búsqueda de texto en título, contenido y tags |
| 4 | `filter_notes` | v1 | 📋 | Filtra notas por frontmatter (tags, fecha, estado, tipo) |
| 5 | `get_note_metadata` | v1 | 📋 | Lee solo el frontmatter YAML de una nota |
| 6 | `get_backlinks` | v1 | 📋 | Devuelve todas las notas que enlazan a una nota dada |
| 7 | `get_outgoing_links` | v1 | 📋 | Devuelve todos los wikilinks que salen de una nota |
| 8 | `semantic_search_notes` | v2 | 📋 | Búsqueda por similitud conceptual (embeddings) |
| 9 | `hybrid_search_notes` | v2 | 📋 | Búsqueda combinada: texto + semántica (RRF) |
| 10 | `find_similar_notes` | v2 | 📋 | Notas conceptualmente similares a una nota dada |
| 11 | `get_note_embedding` | v2 | 💡 | Devuelve el embedding de una nota (debug/diagnóstico) |

#### Justificaciones

- **`read_note`**: Operación fundamental. El agente necesita leer el contenido completo antes de razonar sobre él.
- **`list_notes`**: Permite al agente tener una vista de la bóveda completa para planear acciones.
- **`search_notes`**: Búsqueda rápida por texto. Esencial para localizar notas por tema sin iterar toda la bóveda.
- **`filter_notes`**: Permite al agente filtrar subconjuntos específicos (ej. "todas las notas con status: draft del mes pasado").
- **`get_note_metadata`**: Más eficiente que `read_note` cuando solo se necesitan los metadatos YAML.
- **`get_backlinks` / `get_outgoing_links`**: Permiten al agente navegar el grafo de conocimiento de la bóveda — clave para encontrar conexiones entre ideas.
- **`semantic_search_notes`**: Para bóvedas donde los términos exactos no coinciden pero el concepto sí (ej. buscar "redes neuronales recurrentes" y encontrar notas sobre "LSTM").
- **`hybrid_search_notes`**: Combina la precisión del texto con la flexibilidad semántica. Mejor resultado general para la mayoría de queries.
- **`find_similar_notes`**: El agente puede sugerir conexiones no obvias entre notas al encontrar las más similares a la que el usuario está editando.

---

### ✏️ Grupo 2: Escritura y Modificación de Notas

Estas herramientas **modifican la bóveda**. Requieren confirmación implícita del agente.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 12 | `create_note` | v1 | 📋 | Crea una nueva nota con frontmatter y contenido |
| 13 | `update_note_content` | v1 | 📋 | Reemplaza o edita el contenido de una nota |
| 14 | `append_to_note` | v1 | 📋 | Añade texto al final de una nota (útil para bitácoras) |
| 15 | `prepend_to_note` | v1 | 📋 | Añade texto al inicio del cuerpo (después del frontmatter) |
| 16 | `update_frontmatter` | v1 | 📋 | Actualiza o añade campos en el frontmatter YAML |
| 17 | `add_tag` | v1 | 📋 | Añade uno o más tags a una nota |
| 18 | `remove_tag` | v1 | 📋 | Elimina un tag de una nota |
| 19 | `delete_note` | v1 | 💡 | Elimina una nota (requiere confirmación explícita) |
| 20 | `move_note` | v1 | 📋 | Mueve una nota a otra carpeta (actualiza wikilinks) |
| 21 | `rename_note` | v1 | 📋 | Renombra una nota (actualiza wikilinks en toda la bóveda) |

#### Justificaciones

- **`create_note`**: Permite al agente crear notas de resumen, notas de sesión de trabajo, o nuevas entradas en la bóveda.
- **`update_note_content`**: Para que el agente pueda editar notas existentes directamente.
- **`append_to_note`**: Patrón muy común en flujos de bitácora. El agente añade un registro de la sesión sin tocar el contenido previo.
- **`prepend_to_note`**: Útil para añadir un TL;DR o resumen ejecutivo al inicio de notas largas.
- **`update_frontmatter`**: Permite clasificar o cambiar el status de una nota sin editar su cuerpo.
- **`add_tag` / `remove_tag`**: Operaciones frecuentes de clasificación y reorganización de la taxonomía de tags.
- **`delete_note`**: Marcado como 💡 porque es destructivo. Evaluar si se mueve a la papelera de Obsidian en lugar de eliminar.
- **`move_note` / `rename_note`**: Cruciales para reorganización. Deben actualizar los wikilinks que apunten a la nota movida/renombrada en toda la bóveda.

---

### 🗂️ Grupo 3: Organización y Taxonomía

Herramientas para mantener la bóveda ordenada y bien clasificada.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 22 | `reorder_notes_in_folder` | v2 | 📋 | Renombra notas con prefijo numérico para definir orden |
| 23 | `normalize_tags` | v2 | 📋 | Estandariza capitalización y formato de tags en toda la bóveda |
| 24 | `rename_tag_globally` | v2 | 📋 | Renombra un tag en todas las notas de la bóveda |
| 25 | `merge_tags` | v2 | 📋 | Fusiona dos tags en uno (ej. `AI` + `artificial-intelligence` → `ia`) |
| 26 | `reclassify_note` | v2 | 📋 | Mueve una nota a la carpeta más apropiada según su contenido |
| 27 | `suggest_tags` | v2 | 📋 | Sugiere tags relevantes para una nota basándose en el contenido |
| 28 | `suggest_folder` | v2 | 💡 | Sugiere la carpeta más adecuada para una nota nueva |
| 29 | `find_duplicate_notes` | v2 | 💡 | Detecta notas con contenido similar (posibles duplicados) |
| 30 | `audit_vault` | v2 | 💡 | Reporte de salud de la bóveda: notas sin tags, sin fecha, etc. |

#### Justificaciones

- **`reorder_notes_in_folder`**: Obsidian no tiene un orden nativo de archivos. Para bóvedas de conocimiento secuencial (cursos, tutoriales), los prefijos numéricos (`01-introduccion.md`, `02-instalacion.md`) son el patrón estándar. El agente puede reorganizar el orden de los capítulos sin que el usuario lo haga manualmente.
- **`normalize_tags`**: Con 500 notas, es común acumular variaciones del mismo tag (`machine-learning`, `MachineLearning`, `ML`). Esta herramienta consolida la taxonomía.
- **`rename_tag_globally`**: Operación de refactoring de taxonomía. Cambiar un tag en cientos de notas manualmente es inviable.
- **`merge_tags`**: Elimina redundancias en la taxonomía fusionando tags semánticamente equivalentes.
- **`reclassify_note`**: El agente analiza el contenido de una nota y la mueve a la carpeta más apropiada según las reglas de la bóveda. Útil para notas "inbox" que aún no han sido clasificadas.
- **`suggest_tags`**: El agente lee la nota y propone tags usando búsqueda semántica inversa: "¿qué tags ya existentes en la bóveda son relevantes para este contenido?".
- **`find_duplicate_notes`**: Con 500 notas, es probable tener duplicados o notas muy similares. El agente puede detectarlos y proponer fusionarlos.
- **`audit_vault`**: Dashboard de calidad de la bóveda. El agente devuelve: notas sin tags, notas sin actualizar en X días, assets huérfanos, wikilinks rotos.

---

### 🖼️ Grupo 4: Assets y Archivos No-Markdown

Herramientas para trabajar con los activos visuales y bases de datos de la bóveda.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 31 | `list_excalidraw_files` | v2 | 📋 | Lista todos los diagramas Excalidraw de la bóveda |
| 32 | `get_asset_metadata` | v2 | 📋 | Metadatos de un asset (nombre, ubicación, tamaño, fecha) |
| 33 | `find_orphan_assets` | v2 | 📋 | Assets no referenciados por ninguna nota |
| 34 | `find_broken_links` | v2 | 📋 | Wikilinks rotos en toda la bóveda |
| 35 | `list_database_tables` | v2 | 💡 | Lista las bases de datos de DB Folder / Dataview |
| 36 | `query_database_table` | v2 | 💡 | Consulta filas de una base de datos nativa de Obsidian |

#### Justificaciones

- **`list_excalidraw_files`**: El agente puede referenciar diagramas existentes al escribir notas o al crear nuevas notas que los necesiten.
- **`get_asset_metadata`**: Permite al agente saber qué assets existen y dónde están, sin necesidad de acceder a su contenido binario.
- **`find_orphan_assets`**: Con 500 notas, es común acumular imágenes y diagramas que nadie referencia. El agente ayuda a limpiar la bóveda.
- **`find_broken_links`**: Detecta wikilinks que apuntan a notas que ya no existen (fueron renombradas o eliminadas).
- **`list_database_tables` / `query_database_table`**: Para bóvedas que usan DB Folder o tablas nativas de Obsidian como base de datos de conocimiento.

---

### 🔧 Grupo 5: Utilidades del Sistema

Herramientas de diagnóstico e información del servidor.

| # | Tool Name | Versión | Estado | Descripción |
|---|---|---|---|---|
| 37 | `get_vault_stats` | v1 | 📋 | Estadísticas de la bóveda (total notas, tags, carpetas) |
| 38 | `get_index_status` | v1 | 📋 | Estado del índice en memoria y última actualización |
| 39 | `rebuild_index` | v1 | 📋 | Fuerza una re-indexación completa de la bóveda |
| 40 | `ping` | v1 | 📋 | Verificación de que el servidor está activo (health check) |

---

## Parte 2: Plugin Commands (Obsidian ↔ Motor C#)

Comandos que el motor C# envía al plugin de Obsidian vía WebSocket local. El plugin actúa como un **servidor WebSocket local** y el motor C# como cliente.

> Estos comandos solo funcionan cuando Obsidian está abierto. Si está cerrado, el motor C# ignora los comandos del plugin sin error.

---

### 🖥️ Grupo A: Navegación y Foco

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| A1 | `open-file` | v1 | 🔨 | Abre y enfoca una nota específica en Obsidian |
| A2 | `get-active-note` | v1 | 🔨 | Devuelve la nota que actualmente tiene el foco |
| A3 | `scroll-to-block` | v1 | 📋 | Desplaza la vista hasta un bloque específico (por ID de bloque) |
| A4 | `open-in-split` | v1 | 📋 | Abre una nota en un panel dividido sin cerrar la vista actual |
| A5 | `close-note` | v1 | 💡 | Cierra la pestaña de una nota específica |
| A6 | `get-open-notes` | v1 | 📋 | Lista todas las notas actualmente abiertas en pestañas |

#### Justificaciones

- **`open-file`**: Cuando el agente trabaja con una nota, el usuario puede querer verla. El agente la abre automáticamente.
- **`get-active-note`**: Permite al agente saber qué nota está leyendo el usuario en este momento para contextualizarse.
- **`scroll-to-block`**: Esencial cuando el agente referencia un fragmento específico de una nota larga. El usuario va directo al punto relevante.
- **`open-in-split`**: Flujo de trabajo común: el usuario tiene una nota abierta y el agente necesita mostrarle una referencia sin interrumpir su lectura.
- **`get-open-notes`**: El agente puede adaptar sus respuestas según qué notas tiene el usuario abiertas en este momento.

---

### ⚡ Grupo B: Ejecución de Comandos

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| B1 | `trigger-command` | v1 | 🔨 | Ejecuta un comando nativo de Obsidian por su ID |
| B2 | `trigger-plugin-command` | v1 | 📋 | Ejecuta un comando de otro plugin instalado |
| B3 | `open-command-palette` | v1 | 💡 | Abre la paleta de comandos de Obsidian (modo asistido) |

#### Justificaciones

- **`trigger-command`**: Puente hacia el ecosistema completo de Obsidian. Permite al agente ejecutar cualquier acción disponible en la paleta de comandos (ej. "Toggle reading view", "Export to PDF").
- **`trigger-plugin-command`**: Interoperabilidad con otros plugins populares como Dataview, Templater, Tasks, etc.

---

### 🏛️ Grupo C: Información del Vault (desde Obsidian)

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| C1 | `get-vault-path` | v1 | 🔨 | Ruta raíz de la bóveda activa |
| C2 | `get-app-version` | v1 | 📋 | Versión de Obsidian y del plugin Kioku |
| C3 | `get-installed-plugins` | v1 | 💡 | Lista de plugins de comunidad instalados |
| C4 | `is-obsidian-ready` | v1 | 📋 | Indica si Obsidian está completamente cargado y listo |

#### Justificaciones

- **`get-vault-path`**: El motor C# puede inferir la ruta de la bóveda por configuración, pero verificar con Obsidian directamente elimina ambigüedad (ej. múltiples bóvedas).
- **`is-obsidian-ready`**: Permite al motor C# esperar a que Obsidian cargue completamente antes de enviar comandos de navegación.

---

### ✏️ Grupo D: Creación Asistida (Solo si Obsidian está abierto)

| # | Comando | Versión | Estado | Descripción |
|---|---|---|---|---|
| D1 | `create-note-ui` | v2 | 📋 | Crea una nueva nota y la abre en Obsidian inmediatamente |
| D2 | `insert-at-cursor` | v2 | 💡 | Inserta texto en la posición del cursor del editor activo |
| D3 | `replace-selection` | v2 | 💡 | Reemplaza el texto seleccionado en el editor activo |
| D4 | `highlight-block` | v2 | 💡 | Resalta visualmente un bloque de texto en el editor |

#### Justificaciones

- **`create-note-ui`**: Diferente a `create_note` del servidor MCP (que crea en disco). Este comando crea la nota Y la abre en Obsidian, dando feedback visual inmediato al usuario.
- **`insert-at-cursor`**: El agente puede ayudar al usuario a escribir insertar contenido exactamente donde el usuario tiene el cursor.
- **`replace-selection`**: Flujo de trabajo de refactoring asistido: el usuario selecciona un fragmento y el agente lo reescribe.

---

## Resumen de Estado por Versión

### v1 — MVP (Stdio Transport)

**Total tools MCP:** 17 herramientas
- Grupos implementados: Lectura (1-7), Escritura básica (12-18, 20-21), Utilidades (37-40), Plugin básico (A1-A2, B1, C1, C4)

**Criterio de MVP:** El agente debe poder leer, buscar, crear y modificar notas sin necesitar que Obsidian esté abierto.

### v2 — HTTP-SSE + Semántica

**Añade:** ~23 herramientas adicionales
- Búsqueda semántica e híbrida (8-11)
- Organización avanzada (22-30)
- Assets y no-Markdown (31-36)
- Comandos de UI avanzados (D1-D4)

**Criterio de v2:** El agente puede organizar, clasificar y reorganizar la bóveda de manera inteligente usando búsqueda semántica.

---

## Consideraciones para Publicación en Community Store

Los MCP Tools son internos al servidor C# y no requieren aprobación de Obsidian. El plugin de TypeScript sí debe seguir los estándares:

1. **Solo usar APIs públicas de Obsidian** — `app.workspace`, `app.vault`, `app.metadataCache`.
2. **No hacer peticiones de red externas** desde el plugin — toda comunicación es local (WebSocket a `localhost`).
3. **Manejar correctamente `onunload`** — cerrar el WebSocket server al desinstalar el plugin.
4. **No almacenar datos de usuario fuera de la bóveda** — respetar la privacidad del usuario.
5. **Documentar el puerto WebSocket como configurable** en la pantalla de configuración del plugin.
6. **Seguir las guías de `manifest.json`** con `minAppVersion` apropiado.
