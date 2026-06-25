> **📜 Archivo histórico (Junio 2026).** Este documento fue generado por una IA externa (Perplexity) como propuesta. La mayoría de estas tools ya están implementadas en `develop`. Ver [`commands-reference.md`](./commands-reference.md) para el inventario actualizado.

Analicé la estructura del repo [sandovaldavid/kioku](https://github.com/sandovaldavid/kioku/tree/develop) y tu documento de planning. Aquí tienes un análisis profundo con **ideas de tools nuevas** organizadas por valor real que aportarían al flujo de trabajo, más allá de las 7 ramas que ya tienes propuestas.

***

## Lo que ya tienes es sólido

Tu arquitectura con el [motor C# (`Kioku.Mcp.Server`) + plugin TypeScript (`obsidian-kioku-mcp`)](https://github.com/sandovaldavid/kioku/tree/56085ada949881dc84bcd65075d5bb9da8e795e1/src) es exactamente el patrón correcto: el MCP server como cerebro inteligente y el plugin de Obsidian como brazo executor vía WebSocket. Las 40 tools + comandos de plugin que tienes documentados cubren bien el CRUD básico y la organización. Lo que falta es cubrir **flujos de trabajo completos** que hoy requieren encadenar 3-5 tools manualmente.

***

## Rama H — Flujos Compuestos (Workflow Chains)

Esta es la oportunidad más poderosa que veo: tools que encapsulan *workflows*, no solo operaciones atómicas. El agente llama una sola tool y ejecuta un pipeline completo.

| Tool | Qué hace | Por qué importa |
|---|---|---|
| `process_inbox_note` | Lee una nota de `Inbox/`, sugiere carpeta + tags + wikilinks, mueve y actualiza frontmatter en un solo paso | El flujo más frecuente en PKM: la nota entra al inbox y el agente la "procesa" completamente |
| `create_note_from_template` | Recibe un template name + variables, genera frontmatter relleno + estructura inicial | Evita que el agente tenga que leer el template, interpolarlo y luego llamar `create_note` por separado |
| `summarize_note` | Lee la nota, genera un TL;DR y lo hace `prepend_to_note` automáticamente | El agente resume notas largas con un solo comando |
| `extract_action_items` | Escanea la nota en busca de checkboxes/patrones de tarea y los consolida en una nota de tareas | Puente entre Obsidian y flujos de GTD sin necesitar el plugin Tasks |
| `link_related_notes` | Usa embeddings para encontrar las 5 notas más relacionadas y agrega wikilinks al final de la nota | Construye el grafo de conocimiento automáticamente |

***

## Rama I — Sesión de Trabajo y Contexto

Estas tools hacen que el agente "recuerde" lo que el usuario estaba haciendo, crucial para sesiones largas de investigación o escritura de tesis.

| Tool | Descripción |
|---|---|
| `start_work_session` | Crea una nota de sesión con timestamp, registra las notas abiertas actualmente (`get-open-notes`), y guarda en `Sessions/YYYY-MM-DD.md` |
| `end_work_session` | Cierra la sesión: agrega un resumen de qué notas se crearon/modificaron durante la sesión y lo appendea a la nota de sesión |
| `get_recent_activity` | Devuelve las N notas modificadas más recientemente (por `mtime`), con los diffs de frontmatter si cambiaron |
| `get_work_context` | Resume el "estado de la bóveda" relevante para el agente: notas en inbox, notas con `status: draft`, sesión activa si existe |

Esto es especialmente útil para tu tesis de MSR: el agente puede retomar exactamente donde dejaste la sesión anterior.

***

## Rama J — Conocimiento Académico / Research

Dado que usas Obsidian para gestión de tesis y Zotero, estas tools tienen alto impacto en tu caso de uso específico.

| Tool | Descripción |
|---|---|
| `import_zotero_item` | Dado un Zotero Key o DOI, crea una nota de literatura con el frontmatter estándar (authors, year, doi, citekey, abstract) usando la API local de Zotero |
| `get_literature_gap` | Analiza las notas con tipo `literature` y busca papers citados que no tienen su propia nota (gaps de lectura) |
| `create_research_note` | Crea el trío atómico de notas Zettelkasten: Fleeting + Literature + Permanent vinculadas entre sí |
| `export_citations` | Exporta todas las notas con `citekey` en un formato `.bib` o lista Markdown con autor-año |
| `cross_reference_notes` | Dado un claim o argumento textual, busca en la bóveda qué notas lo corroboran, contradicen o amplían |

La tool `cross_reference_notes` es particularmente poderosa para escritura académica: el agente actúa como un co-autor que verifica si el claim que estás escribiendo está respaldado por tus propias notas de lectura.

***

## Rama K — Bóveda como Base de Conocimiento Consultable

Estas tools convierten la bóveda en algo parecido a una base de datos con semántica.

| Tool | Descripción |
|---|---|
| `answer_from_vault` | Dado un query en lenguaje natural, hace `hybrid_search_notes`, recupera los fragmentos más relevantes, y los devuelve como contexto estructurado (RAG sobre la bóveda) |
| `get_knowledge_timeline` | Devuelve notas ordenadas por `date-created` en un rango de fechas — ver cómo evolucionó el conocimiento sobre un tema |
| `get_concept_map` | Para una nota o tag dado, devuelve el grafo de notas relacionadas (backlinks + outgoing links + similitud semántica) como un JSON de nodos/aristas |
| `watch_vault_changes` | Tool de streaming/SSE: emite eventos cuando una nota es creada o modificada en tiempo real (requiere v2 HTTP-SSE) |

`get_concept_map` + `watch_vault_changes` abren la puerta a visualizaciones del grafo en tiempo real que complementan la vista de grafo nativa de Obsidian.

***

## Comandos Plugin que faltan (Rama E extendida)

Hay dos comandos de plugin que noto como ausencias críticas del lado de UI:

| Comando | Descripción |
|---|---|
| `toggle-reading-mode` | Alterna entre modo edición y modo lectura en la nota activa — esencial para flujos de review |
| `get-selection` | Devuelve el texto actualmente seleccionado en el editor activo, para que el agente opere sobre él sin que el usuario lo copie y pegue |
| `create-canvas` | Crea un nuevo Canvas (`.canvas`) con nodos predefinidos — complementa `create_excalidraw` con la herramienta nativa de Obsidian |
| `fold-all-headings` / `unfold-all-headings` | Colapsa/expande todos los headings — útil cuando el agente quiere que el usuario vea la estructura macro de una nota larga |

`get-selection` en particular eliminaría mucha fricción: el usuario selecciona un párrafo, le pide al agente que lo mejore, y el agente puede leer exactamente qué texto quiere refactorizar.

***

## Ideas Transversales de Diseño

**Patrón "dry-run"** — Para todas las tools destructivas o de modificación masiva (`normalize_tags`, `rename_tag_globally`, `find_duplicate_notes`), agregar un parámetro `dryRun: bool`. Cuando es `true`, la tool devuelve un preview de qué cambiaría sin ejecutar nada. Así el agente puede confirmar con el usuario antes de actuar.

**Patrón "batch + report"** — Tools como `audit_vault` o `find_orphan_assets` deberían devolver un objeto estructurado que el agente pueda serializar en una nota Markdown de reporte (llamando después a `create_note`). Definir un schema consistente: `{ summary: string, items: Array<{path, issue, severity}> }`.

**Tool `get_vault_snapshot`** — Devuelve un JSON compacto con el árbol de carpetas, lista de tags y stats de frontmatter. Permite que el agente tenga un "mapa mental" de la bóveda en un solo llamado, en lugar de llamar `list_notes` + `get_vault_stats` + varios `get_note_metadata` por separado. Reduce drásticamente el número de tool calls para tareas de organización.



Perfecto, ya leí tu `AGENTS.md`, `GEMINI.md` y el `README` de **Cortex-L7**. Tu vault sigue un framework de 7 módulos con semántica muy clara (L1 Cache → HQ → CPU → Storage → Staging → R&D → Cold Storage), lo cual es una base excelente para que Kioku opere con contexto real. Te organizo todo lo que pediste en secciones concretas. [ppl-ai-file-upload.s3.amazonaws](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/110558520/d28f973d-fd53-411a-8c28-3c1c843c52f8/agents.md?AWSAccessKeyId=ASIA2F3EMEYEV5KJYPR7&Signature=ACKgS7xQJ8A3d4pAemXEaweCnp8%3D&x-amz-security-token=IQoJb3JpZ2luX2VjEH4aCXVzLWVhc3QtMSJIMEYCIQCvmAuRopJ4wzTjLfP1uuvmb3dx1WZ0PIbULXLWIEUk1AIhALggCdR3%2FMGcO0bZygQzB0Ks%2FSD8HKoVYZVC7OlfrHgWKvMECEcQARoMNjk5NzUzMzA5NzA1IgyGZUe0%2BWHj2u49254q0ASuDkSe67uq97ZSetBhIiZOm9MstKKakmHLN4STwVflU2zOott9imywwX%2BGrvB5kirmjp21y%2BnetpAfJ8PnulGyoddOtRlGcPGF6ApTJCm2KpI0SSivXpqpiE%2F%2BlT6Fq4XU1CkJR%2BQY%2FlKj6deghTjDFKi20OHUDn47bwXIVMcVsTC2pdikz5dwXIjtwO%2FKQHNM0CFX17CFUyBRPDRIWnzv1jkaBFxs%2BWJHl2D%2BJjeanngKZlV9iNRiTQZXMYZBvjWMryrSI8LDSlBBqB6XIr5lpgCOBkMkXj5h2c3HUZo9EG1MpSAHu%2FLhsCI7uo7qq43iDlwP6WY3pZIsJunBCyKA4Yrgbw21vYY0TnCyCGdQpRJMWBuQp2YV1tGTQhSH8ipR%2B54jwd9pHZFV36OSU3bKF1I%2Fg24LoJ%2BSBuFbO%2BUocg%2BMaW%2Bjnl6snn3LKaCgE7soB8R2IlkPwdogv2Qs0A%2BE3UpkueR5LC1pNhm1kBMWH7B30kJFowaTJMofT5L1zkemgyT5q2cOxGGGr%2BYVpLuMfauwnWyYLwIESzgJY%2FMKmP9go%2FxqthOt619o59dN4q4LbfS6bfJDXYQPwR8KsP8vCqD1p8KFG3j13Nwbl8wm6Do%2B%2BHXUw226PYxRd0JH%2Fl6hy3cp8F%2By6rSar24K9ybj3JhIaMv40vFTxXdXDhzz4UF7rjfAI1whk5h%2BpgKfUfrY3XrD6OWRP5qqsKR6F6P4w92p8NOgxmvtcq3aMtCW1FTBNHH%2FRgZybkOBIaIZiddko5tw3h1H7ZTWybCHrUFWMKaH89EGOpcB9zfh2fHOLH9qklzdwj0X9bjar%2BW3ioKbQ2c8gEDeCD7Ala6H3hFCOI4TMoFCiTU%2BQBjxSqJsulwhSy1ufzXOj%2FwXZP5CRGRnj1Zbzpz9XyyWMiqBjNcTdDE9X67q7LxGIjXB5S8qxPMJPKJJmIFF47j2I2yifB2Aj9SI46wy%2FcM0%2FU3K2e8BL%2F%2BoUQeaXMSzRKOUapOazQ%3D%3D&Expires=1782370681)

***

## Export y Compartir Notas

### `export_note` — Tool MCP v2

Esta es una sola tool con parámetro `format` para no fragmentar el API:

```csharp
[McpServerTool]
async Task<ExportResult> export_note(
    string path,
    string format,        // "pdf" | "html" | "markdown-clean" | "docx"
    ExportOptions? options
)
```

La implementación depende de si Obsidian está abierto o no:

- **Obsidian abierto:** el motor C# envía el comando de plugin `trigger-command` con `export-to-pdf` (comando nativo de Obsidian). Es la vía más limpia porque Obsidian renderiza CSS themes, callouts y embeds correctamente.
- **Obsidian cerrado:** el servidor C# usa **Pandoc** como fallback — lee el `.md`, ejecuta `pandoc input.md -o output.pdf` con un template personalizable en `99_System/Templates/export-template.html`. Esto permite exportar incluso sin la UI abierta.

Para HTML, el motor puede generar un HTML standalone con el CSS de Obsidian incrustado, útil para compartir renders exactos de la nota.

### `share_note_url` — Plugin Command v2

Compartir una nota públicamente requiere integración con un servicio externo. Las opciones viables para el plugin:

| Opción | Mecánica | Limitación |
|---|---|---|
| **Obsidian Publish** | `trigger-command` con `publish:publish-file` | Requiere suscripción de pago |
| **Gist de GitHub** | El servidor C# llama la GitHub API con el contenido Markdown | Gratuito, pero URL no personalizable |
| **note.ms / HedgeDoc** | HTTP POST al API del servicio | Depende de servicio de terceros |

La implementación más pragmática para Kioku es un comando `share_as_gist` en el servidor C# (no el plugin), que lee la nota con `read_note`, sube el contenido a la API de GitHub Gist, y devuelve la URL pública. No requiere Obsidian abierto, es gratuito, y el contenido queda versionado.

***

## Template Engine — La Pieza Central

### `create_note_from_template` — Tool MCP v1 (alta prioridad)

Esta tool merece diseño cuidadoso porque es el punto de entrada más frecuente del agente. La lógica es:

1. El agente (Ollama/Claude) recibe una solicitud del usuario
2. Llama `list_templates` para ver qué templates existen en `99_System/Templates/`
3. Decide qué template usar (o `null` si ninguno aplica)
4. Llama `create_note_from_template` con el template elegido y las variables

```csharp
[McpServerTool]
async Task<NoteResult> create_note_from_template(
    string templateName,        // "brain-concept", "research-paper", "star-story"...
    string targetPath,          // ruta de destino dentro del vault
    Dictionary<string, string> variables,  // {{ title }}, {{ date }}, {{ domain }}...
    bool openInObsidian = true
)

[McpServerTool]
async Task<TemplateList> list_templates()
// Escanea 99_System/Templates/ y devuelve nombre + frontmatter del template

[McpServerTool]
async Task<NoteResult> create_template(
    string name,
    string content,    // el agente genera el template desde cero
    string category    // "note" | "research" | "project" | "daily"
)
// Guarda en 99_System/Templates/{name}.md
```

La interpolación de variables usa la sintaxis de Handlebars/Mustache (`{{ variable }}`), que ya usa Templater de Obsidian. Esto mantiene compatibilidad con los templates que ya tengas en `99_System/Templates/`.

### Templates sugeridos para Cortex-L7

Basándome en tu estructura, estos son los templates que el agente debería poder autodetectar según la carpeta de destino: [ppl-ai-file-upload.s3.amazonaws](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/110558520/d28f973d-fd53-411a-8c28-3c1c843c52f8/agents.md?AWSAccessKeyId=ASIA2F3EMEYEV5KJYPR7&Signature=ACKgS7xQJ8A3d4pAemXEaweCnp8%3D&x-amz-security-token=IQoJb3JpZ2luX2VjEH4aCXVzLWVhc3QtMSJIMEYCIQCvmAuRopJ4wzTjLfP1uuvmb3dx1WZ0PIbULXLWIEUk1AIhALggCdR3%2FMGcO0bZygQzB0Ks%2FSD8HKoVYZVC7OlfrHgWKvMECEcQARoMNjk5NzUzMzA5NzA1IgyGZUe0%2BWHj2u49254q0ASuDkSe67uq97ZSetBhIiZOm9MstKKakmHLN4STwVflU2zOott9imywwX%2BGrvB5kirmjp21y%2BnetpAfJ8PnulGyoddOtRlGcPGF6ApTJCm2KpI0SSivXpqpiE%2F%2BlT6Fq4XU1CkJR%2BQY%2FlKj6deghTjDFKi20OHUDn47bwXIVMcVsTC2pdikz5dwXIjtwO%2FKQHNM0CFX17CFUyBRPDRIWnzv1jkaBFxs%2BWJHl2D%2BJjeanngKZlV9iNRiTQZXMYZBvjWMryrSI8LDSlBBqB6XIr5lpgCOBkMkXj5h2c3HUZo9EG1MpSAHu%2FLhsCI7uo7qq43iDlwP6WY3pZIsJunBCyKA4Yrgbw21vYY0TnCyCGdQpRJMWBuQp2YV1tGTQhSH8ipR%2B54jwd9pHZFV36OSU3bKF1I%2Fg24LoJ%2BSBuFbO%2BUocg%2BMaW%2Bjnl6snn3LKaCgE7soB8R2IlkPwdogv2Qs0A%2BE3UpkueR5LC1pNhm1kBMWH7B30kJFowaTJMofT5L1zkemgyT5q2cOxGGGr%2BYVpLuMfauwnWyYLwIESzgJY%2FMKmP9go%2FxqthOt619o59dN4q4LbfS6bfJDXYQPwR8KsP8vCqD1p8KFG3j13Nwbl8wm6Do%2B%2BHXUw226PYxRd0JH%2Fl6hy3cp8F%2By6rSar24K9ybj3JhIaMv40vFTxXdXDhzz4UF7rjfAI1whk5h%2BpgKfUfrY3XrD6OWRP5qqsKR6F6P4w92p8NOgxmvtcq3aMtCW1FTBNHH%2FRgZybkOBIaIZiddko5tw3h1H7ZTWybCHrUFWMKaH89EGOpcB9zfh2fHOLH9qklzdwj0X9bjar%2BW3ioKbQ2c8gEDeCD7Ala6H3hFCOI4TMoFCiTU%2BQBjxSqJsulwhSy1ufzXOj%2FwXZP5CRGRnj1Zbzpz9XyyWMiqBjNcTdDE9X67q7LxGIjXB5S8qxPMJPKJJmIFF47j2I2yifB2Aj9SI46wy%2FcM0%2FU3K2e8BL%2F%2BoUQeaXMSzRKOUapOazQ%3D%3D&Expires=1782370681)

| Carpeta destino | Template automático | Variables clave |
|---|---|---|
| `00_Inbox/Quick_Captures` | `quick-capture` | `source`, `date`, `tags` |
| `30_Brain/` | `brain-concept` | `title`, `domain`, `feynman_summary` |
| `50_Research/Papers` | `literature-note` | `citekey`, `doi`, `authors`, `year` |
| `10_Nexus/BigTech/STAR_Story` | `star-story` | `situation`, `task`, `action`, `result` |
| `20_Execution/` | `project-ticket` | `project`, `priority`, `status` |
| `40_Laboratory/` | `learning-note` | `source_url`, `domain`, `status` |

El agente puede inferir el template correcto simplemente analizando la `targetPath`. Si la ruta empieza con `30_Brain/`, usa `brain-concept`. Si el usuario pide algo para lo que no existe template, llama `create_template` generando uno nuevo.

***

## Theming de Obsidian desde el MCP

Esta es la parte más técnica y tiene tres niveles de complejidad:

### Nivel 1 — CSS Snippets (Implementable ahora)

Obsidian soporta CSS snippets en `.obsidian/snippets/*.css` y se activan desde Settings. El motor C# puede:

```csharp
[McpServerTool]
async Task apply_css_snippet(
    string snippetName,
    string cssContent    // el agente genera el CSS
)
// Escribe en {vaultPath}/.obsidian/snippets/{snippetName}.css
// Luego envía comando plugin: trigger-command "snippets:reload"

[McpServerTool]
async Task<SnippetList> list_css_snippets()

[McpServerTool]
async Task toggle_css_snippet(string snippetName, bool enabled)
// Modifica .obsidian/app.json → enabledCssSnippets[]
```

Esto permite al agente cambiar variables CSS (colores, fuentes, espaciado) sin tocar el tema base. Por ejemplo: *"pon el fondo del editor en modo sepia"* → el agente genera un snippet CSS y lo aplica.

### Nivel 2 — Cambio de Tema (Implementable con cuidado)

El archivo `.obsidian/appearance.json` contiene el tema activo. El motor C# puede leerlo y modificarlo:

```csharp
[McpServerTool]
async Task set_obsidian_theme(
    string themeName    // debe ser un tema ya instalado en .obsidian/themes/
)
// Modifica .obsidian/appearance.json → "cssTheme"
// Requiere reinicio de Obsidian o reload del plugin para aplicar
```

El plugin puede enviar `trigger-command "app:reload"` para aplicar el cambio sin reinicio manual.

### Nivel 3 — Crear un Tema Completo (Avanzado, v3)

Crear un tema de Obsidian desde cero es básicamente generar un archivo `theme.css` con las variables CSS que Obsidian expone (`--color-base-00` a `--color-base-100`, `--text-normal`, etc.). El agente puede generar el CSS completo, guardarlo en `.obsidian/themes/kioku-generated/theme.css` junto con un `manifest.json`, y activarlo con `set_obsidian_theme`. No requiere aprobación del Community Store porque es un tema local.

***

## Vault Cortex-L7 — Tools con Conciencia de Estructura

Con la arquitectura de tu vault bien documentada, el agente puede operar con reglas específicas para Cortex-L7. Propongo una tool especial: [ppl-ai-file-upload.s3.amazonaws](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/110558520/57d0c658-9e3e-479f-9def-9544eed69bc3/readme-3.md?AWSAccessKeyId=ASIA2F3EMEYEV5KJYPR7&Signature=mLEnpUFkjYKv7WXgjSjqQ1k8SgM%3D&x-amz-security-token=IQoJb3JpZ2luX2VjEH4aCXVzLWVhc3QtMSJIMEYCIQCvmAuRopJ4wzTjLfP1uuvmb3dx1WZ0PIbULXLWIEUk1AIhALggCdR3%2FMGcO0bZygQzB0Ks%2FSD8HKoVYZVC7OlfrHgWKvMECEcQARoMNjk5NzUzMzA5NzA1IgyGZUe0%2BWHj2u49254q0ASuDkSe67uq97ZSetBhIiZOm9MstKKakmHLN4STwVflU2zOott9imywwX%2BGrvB5kirmjp21y%2BnetpAfJ8PnulGyoddOtRlGcPGF6ApTJCm2KpI0SSivXpqpiE%2F%2BlT6Fq4XU1CkJR%2BQY%2FlKj6deghTjDFKi20OHUDn47bwXIVMcVsTC2pdikz5dwXIjtwO%2FKQHNM0CFX17CFUyBRPDRIWnzv1jkaBFxs%2BWJHl2D%2BJjeanngKZlV9iNRiTQZXMYZBvjWMryrSI8LDSlBBqB6XIr5lpgCOBkMkXj5h2c3HUZo9EG1MpSAHu%2FLhsCI7uo7qq43iDlwP6WY3pZIsJunBCyKA4Yrgbw21vYY0TnCyCGdQpRJMWBuQp2YV1tGTQhSH8ipR%2B54jwd9pHZFV36OSU3bKF1I%2Fg24LoJ%2BSBuFbO%2BUocg%2BMaW%2Bjnl6snn3LKaCgE7soB8R2IlkPwdogv2Qs0A%2BE3UpkueR5LC1pNhm1kBMWH7B30kJFowaTJMofT5L1zkemgyT5q2cOxGGGr%2BYVpLuMfauwnWyYLwIESzgJY%2FMKmP9go%2FxqthOt619o59dN4q4LbfS6bfJDXYQPwR8KsP8vCqD1p8KFG3j13Nwbl8wm6Do%2B%2BHXUw226PYxRd0JH%2Fl6hy3cp8F%2By6rSar24K9ybj3JhIaMv40vFTxXdXDhzz4UF7rjfAI1whk5h%2BpgKfUfrY3XrD6OWRP5qqsKR6F6P4w92p8NOgxmvtcq3aMtCW1FTBNHH%2FRgZybkOBIaIZiddko5tw3h1H7ZTWybCHrUFWMKaH89EGOpcB9zfh2fHOLH9qklzdwj0X9bjar%2BW3ioKbQ2c8gEDeCD7Ala6H3hFCOI4TMoFCiTU%2BQBjxSqJsulwhSy1ufzXOj%2FwXZP5CRGRnj1Zbzpz9XyyWMiqBjNcTdDE9X67q7LxGIjXB5S8qxPMJPKJJmIFF47j2I2yifB2Aj9SI46wy%2FcM0%2FU3K2e8BL%2F%2BoUQeaXMSzRKOUapOazQ%3D%3D&Expires=1782370681)

### `cortex_process_inbox`

Esta sería la implementación concreta del `process_inbox_note` que mencioné antes, pero con las reglas de tu `AGENTS.md`  embebidas: [ppl-ai-file-upload.s3.amazonaws](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/110558520/d28f973d-fd53-411a-8c28-3c1c843c52f8/agents.md?AWSAccessKeyId=ASIA2F3EMEYEV5KJYPR7&Signature=ACKgS7xQJ8A3d4pAemXEaweCnp8%3D&x-amz-security-token=IQoJb3JpZ2luX2VjEH4aCXVzLWVhc3QtMSJIMEYCIQCvmAuRopJ4wzTjLfP1uuvmb3dx1WZ0PIbULXLWIEUk1AIhALggCdR3%2FMGcO0bZygQzB0Ks%2FSD8HKoVYZVC7OlfrHgWKvMECEcQARoMNjk5NzUzMzA5NzA1IgyGZUe0%2BWHj2u49254q0ASuDkSe67uq97ZSetBhIiZOm9MstKKakmHLN4STwVflU2zOott9imywwX%2BGrvB5kirmjp21y%2BnetpAfJ8PnulGyoddOtRlGcPGF6ApTJCm2KpI0SSivXpqpiE%2F%2BlT6Fq4XU1CkJR%2BQY%2FlKj6deghTjDFKi20OHUDn47bwXIVMcVsTC2pdikz5dwXIjtwO%2FKQHNM0CFX17CFUyBRPDRIWnzv1jkaBFxs%2BWJHl2D%2BJjeanngKZlV9iNRiTQZXMYZBvjWMryrSI8LDSlBBqB6XIr5lpgCOBkMkXj5h2c3HUZo9EG1MpSAHu%2FLhsCI7uo7qq43iDlwP6WY3pZIsJunBCyKA4Yrgbw21vYY0TnCyCGdQpRJMWBuQp2YV1tGTQhSH8ipR%2B54jwd9pHZFV36OSU3bKF1I%2Fg24LoJ%2BSBuFbO%2BUocg%2BMaW%2Bjnl6snn3LKaCgE7soB8R2IlkPwdogv2Qs0A%2BE3UpkueR5LC1pNhm1kBMWH7B30kJFowaTJMofT5L1zkemgyT5q2cOxGGGr%2BYVpLuMfauwnWyYLwIESzgJY%2FMKmP9go%2FxqthOt619o59dN4q4LbfS6bfJDXYQPwR8KsP8vCqD1p8KFG3j13Nwbl8wm6Do%2B%2BHXUw226PYxRd0JH%2Fl6hy3cp8F%2By6rSar24K9ybj3JhIaMv40vFTxXdXDhzz4UF7rjfAI1whk5h%2BpgKfUfrY3XrD6OWRP5qqsKR6F6P4w92p8NOgxmvtcq3aMtCW1FTBNHH%2FRgZybkOBIaIZiddko5tw3h1H7ZTWybCHrUFWMKaH89EGOpcB9zfh2fHOLH9qklzdwj0X9bjar%2BW3ioKbQ2c8gEDeCD7Ala6H3hFCOI4TMoFCiTU%2BQBjxSqJsulwhSy1ufzXOj%2FwXZP5CRGRnj1Zbzpz9XyyWMiqBjNcTdDE9X67q7LxGIjXB5S8qxPMJPKJJmIFF47j2I2yifB2Aj9SI46wy%2FcM0%2FU3K2e8BL%2F%2BoUQeaXMSzRKOUapOazQ%3D%3D&Expires=1782370681)

```
1. Lee la nota de 00_Inbox/Quick_Captures/
2. Verifica si el contenido es distilable (no ruido de <3 meses)
3. Clasifica: ¿va a 30_Brain? (debe estar en palabras propias)
   → Si contiene referencia externa directa → 40_Laboratory primero
   → Si es insight + source → crear par (literature-note + brain-concept)
4. Sugiere carpeta según el 7-Module Framework
5. Verifica que NO se rompa la regla de atomicidad
6. Mueve + actualiza wikilinks + agrega backlink al MOC de la carpeta destino
```

El `AGENTS.md` prohíbe explícitamente mover notas a `30_Brain` si no están destiladas. El agente debería respetar esa regla verificando si la nota tiene `type: distilled` en el frontmatter antes de moverla. [ppl-ai-file-upload.s3.amazonaws](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/110558520/d28f973d-fd53-411a-8c28-3c1c843c52f8/agents.md?AWSAccessKeyId=ASIA2F3EMEYEV5KJYPR7&Signature=ACKgS7xQJ8A3d4pAemXEaweCnp8%3D&x-amz-security-token=IQoJb3JpZ2luX2VjEH4aCXVzLWVhc3QtMSJIMEYCIQCvmAuRopJ4wzTjLfP1uuvmb3dx1WZ0PIbULXLWIEUk1AIhALggCdR3%2FMGcO0bZygQzB0Ks%2FSD8HKoVYZVC7OlfrHgWKvMECEcQARoMNjk5NzUzMzA5NzA1IgyGZUe0%2BWHj2u49254q0ASuDkSe67uq97ZSetBhIiZOm9MstKKakmHLN4STwVflU2zOott9imywwX%2BGrvB5kirmjp21y%2BnetpAfJ8PnulGyoddOtRlGcPGF6ApTJCm2KpI0SSivXpqpiE%2F%2BlT6Fq4XU1CkJR%2BQY%2FlKj6deghTjDFKi20OHUDn47bwXIVMcVsTC2pdikz5dwXIjtwO%2FKQHNM0CFX17CFUyBRPDRIWnzv1jkaBFxs%2BWJHl2D%2BJjeanngKZlV9iNRiTQZXMYZBvjWMryrSI8LDSlBBqB6XIr5lpgCOBkMkXj5h2c3HUZo9EG1MpSAHu%2FLhsCI7uo7qq43iDlwP6WY3pZIsJunBCyKA4Yrgbw21vYY0TnCyCGdQpRJMWBuQp2YV1tGTQhSH8ipR%2B54jwd9pHZFV36OSU3bKF1I%2Fg24LoJ%2BSBuFbO%2BUocg%2BMaW%2Bjnl6snn3LKaCgE7soB8R2IlkPwdogv2Qs0A%2BE3UpkueR5LC1pNhm1kBMWH7B30kJFowaTJMofT5L1zkemgyT5q2cOxGGGr%2BYVpLuMfauwnWyYLwIESzgJY%2FMKmP9go%2FxqthOt619o59dN4q4LbfS6bfJDXYQPwR8KsP8vCqD1p8KFG3j13Nwbl8wm6Do%2B%2BHXUw226PYxRd0JH%2Fl6hy3cp8F%2By6rSar24K9ybj3JhIaMv40vFTxXdXDhzz4UF7rjfAI1whk5h%2BpgKfUfrY3XrD6OWRP5qqsKR6F6P4w92p8NOgxmvtcq3aMtCW1FTBNHH%2FRgZybkOBIaIZiddko5tw3h1H7ZTWybCHrUFWMKaH89EGOpcB9zfh2fHOLH9qklzdwj0X9bjar%2BW3ioKbQ2c8gEDeCD7Ala6H3hFCOI4TMoFCiTU%2BQBjxSqJsulwhSy1ufzXOj%2FwXZP5CRGRnj1Zbzpz9XyyWMiqBjNcTdDE9X67q7LxGIjXB5S8qxPMJPKJJmIFF47j2I2yifB2Aj9SI46wy%2FcM0%2FU3K2e8BL%2F%2BoUQeaXMSzRKOUapOazQ%3D%3D&Expires=1782370681)

### `sunday_hygiene` — Ritual Semanal Automatizado

Basado en la regla *"Sunday Hygiene"* de tu vault: [ppl-ai-file-upload.s3.amazonaws](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/110558520/1216bcf4-d92f-44d5-9729-26344841146f/gemini-2.md?AWSAccessKeyId=ASIA2F3EMEYEV5KJYPR7&Signature=pjsFaZykis6lKNll1b7XSUDdrhA%3D&x-amz-security-token=IQoJb3JpZ2luX2VjEH4aCXVzLWVhc3QtMSJIMEYCIQCvmAuRopJ4wzTjLfP1uuvmb3dx1WZ0PIbULXLWIEUk1AIhALggCdR3%2FMGcO0bZygQzB0Ks%2FSD8HKoVYZVC7OlfrHgWKvMECEcQARoMNjk5NzUzMzA5NzA1IgyGZUe0%2BWHj2u49254q0ASuDkSe67uq97ZSetBhIiZOm9MstKKakmHLN4STwVflU2zOott9imywwX%2BGrvB5kirmjp21y%2BnetpAfJ8PnulGyoddOtRlGcPGF6ApTJCm2KpI0SSivXpqpiE%2F%2BlT6Fq4XU1CkJR%2BQY%2FlKj6deghTjDFKi20OHUDn47bwXIVMcVsTC2pdikz5dwXIjtwO%2FKQHNM0CFX17CFUyBRPDRIWnzv1jkaBFxs%2BWJHl2D%2BJjeanngKZlV9iNRiTQZXMYZBvjWMryrSI8LDSlBBqB6XIr5lpgCOBkMkXj5h2c3HUZo9EG1MpSAHu%2FLhsCI7uo7qq43iDlwP6WY3pZIsJunBCyKA4Yrgbw21vYY0TnCyCGdQpRJMWBuQp2YV1tGTQhSH8ipR%2B54jwd9pHZFV36OSU3bKF1I%2Fg24LoJ%2BSBuFbO%2BUocg%2BMaW%2Bjnl6snn3LKaCgE7soB8R2IlkPwdogv2Qs0A%2BE3UpkueR5LC1pNhm1kBMWH7B30kJFowaTJMofT5L1zkemgyT5q2cOxGGGr%2BYVpLuMfauwnWyYLwIESzgJY%2FMKmP9go%2FxqthOt619o59dN4q4LbfS6bfJDXYQPwR8KsP8vCqD1p8KFG3j13Nwbl8wm6Do%2B%2BHXUw226PYxRd0JH%2Fl6hy3cp8F%2By6rSar24K9ybj3JhIaMv40vFTxXdXDhzz4UF7rjfAI1whk5h%2BpgKfUfrY3XrD6OWRP5qqsKR6F6P4w92p8NOgxmvtcq3aMtCW1FTBNHH%2FRgZybkOBIaIZiddko5tw3h1H7ZTWybCHrUFWMKaH89EGOpcB9zfh2fHOLH9qklzdwj0X9bjar%2BW3ioKbQ2c8gEDeCD7Ala6H3hFCOI4TMoFCiTU%2BQBjxSqJsulwhSy1ufzXOj%2FwXZP5CRGRnj1Zbzpz9XyyWMiqBjNcTdDE9X67q7LxGIjXB5S8qxPMJPKJJmIFF47j2I2yifB2Aj9SI46wy%2FcM0%2FU3K2e8BL%2F%2BoUQeaXMSzRKOUapOazQ%3D%3D&Expires=1782370681)

```
1. audit_vault() → lista notas sin tags, sin updated, con broken links
2. Busca notas en 00_Inbox con más de 7 días de antigüedad → lista para revisar
3. Detecta notas en 30_Brain que contengan referencias directas (copiado, no destilado)
4. Genera reporte en 99_System/ con timestamp
5. Devuelve resumen al agente para que lo presente al usuario
```

Esta tool sería el *cron job* inteligente de Cortex-L7, ejecutable con un solo comando el domingo.




Ahora tengo toda la documentación oficial. Te doy un diseño completo y fundamentado.

***

## Cómo funciona el sistema de theming en Obsidian

Un tema de Obsidian se compone de exactamente **dos archivos** dentro de `.obsidian/themes/{NombreTema}/`: [github](https://github.com/obsidianmd/obsidian-sample-theme/blob/master/manifest.json)

- **`theme.css`** — el cuerpo completo del tema, con variables CSS que sobrescriben los defaults de Obsidian
- **`manifest.json`** — metadatos del tema (nombre, versión, autor, `minAppVersion`)

La mecánica interna funciona así: Obsidian expone más de **400 CSS variables** organizadas en 5 capas. Un tema no toca el HTML ni el JavaScript — solo sobreescribe esas variables bajo los selectores `.theme-light`, `.theme-dark`, `body` y `:root`. Eso es todo lo que necesita tu tool. [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

***

## Las 5 Capas de Variables que puede tocar tu Tool

Basándome en la documentación oficial: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

### Capa 1 — Foundations (El núcleo visual)

**Colores base** — paleta neutra de 12 tonos, de blanco a negro: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
.theme-dark {
  --color-base-00: #1e1e1e;   /* Fondo más oscuro (editor) */
  --color-base-10: #242424;   /* Fondo secundario */
  --color-base-20: #262626;
  --color-base-25: #2a2a2a;
  --color-base-30: #363636;   /* Borders sutiles */
  --color-base-100: #dadada;  /* Texto principal */
}
```

**Accent color** — el color de los links, botones primarios e interactivos. Obsidian lo define como HSL separado para permitir cálculos: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
body {
  --accent-h: 254;    /* hue */
  --accent-s: 80%;    /* saturation */
  --accent-l: 68%;    /* lightness */
}
```

**Extended colors** — usados en callouts, syntax highlighting, grafo, Canvas: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
body {
  --color-red: #fb464c;
  --color-green: #44cf6e;
  --color-blue: #027aff;
  --color-purple: #a882ff;
  /* + orange, yellow, cyan, pink */
}
```

**Typography** — tres fuentes principales + tamaños + pesos: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
body {
  --font-interface-theme: "Inter", sans-serif;  /* UI */
  --font-text-theme: "Georgia", serif;           /* Editor */
  --font-monospace-theme: "JetBrains Mono";      /* Código */
  --font-text-size: 16px;
  --bold-modifier: 200;
}
```

### Capa 2 — Semantic Colors (Superficies y estados)

Son las variables derivadas de la base, las más importantes para cambiar el "feel" general: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
.theme-dark {
  --background-primary: #18004F;        /* Editor background */
  --background-primary-alt: #1a0060;    /* Encima del primary */
  --background-secondary: #220070;      /* Sidebar background */
  --background-modifier-border: rgba(255,255,255,0.1);
  --text-normal: #dadada;
  --text-muted: #999999;
  --text-faint: #666666;
  --interactive-accent: hsl(var(--accent-h), var(--accent-s), var(--accent-l));
}
```

### Capa 3 — Components (UI Elements)

Botones, inputs, checkboxes, modales, tabs, toggles — cada uno tiene sus propias variables: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
body {
  --button-radius: 4px;
  --checkbox-radius: 2px;
  --input-radius: 4px;
  --tab-radius-active: 6px;
  --modal-border-width: 1px;
  --slider-thumb-radius: 50%;
}
```

### Capa 4 — Editor Content (Lo que ves al escribir)

Headings, blockquotes, callouts, código, links, tablas, tags, listas: [docs.obsidian](https://docs.obsidian.md/Reference/CSS+variables/CSS+variables)

```css
body {
  --heading-spacing: 1.5em;
  --p-spacing: 1rem;
  --bold-color: inherit;
  --italic-color: inherit;
  --link-color: var(--text-accent);
  --tag-background: rgba(var(--color-blue-rgb), 0.15);
  --code-background: var(--background-primary-alt);
}
```

### Capa 5 — Window Chrome (Sidebar, Ribbon, Status Bar, Graph)

```css
body {
  --ribbon-background: var(--background-secondary);
  --status-bar-background: var(--background-secondary);
  --graph-node-unresolved: var(--color-base-40);
  --graph-node-tag: var(--color-green);
  --graph-node-attachment: var(--color-orange);
}
```

***

## Diseño de las Tools para Kioku

Con ese conocimiento, aquí tienes el diseño preciso de **3 tools MCP** y **1 plugin command**:

***

### Tool 1 — `apply_css_snippet` (v1, implementable ahora)

La más simple. Un snippet es un solo archivo `.css` en `.obsidian/snippets/`. No requiere `manifest.json` ni reinicio de Obsidian. [obsidian](https://obsidian.md/help/snippets)

```csharp
[McpServerTool, Description("Creates or updates a CSS snippet in the Obsidian vault")]
async Task<SnippetResult> apply_css_snippet(
    [Description("Filename without .css extension")] string name,
    [Description("Valid CSS content. Use Obsidian CSS variables.")] string cssContent,
    [Description("Auto-enable after creating")] bool enable = true
)
```

**Flujo de implementación en C#:**

1. Escribe `{vaultPath}/.obsidian/snippets/{name}.css` con el `cssContent`
2. Si `enable = true`: lee `.obsidian/app.json`, agrega `name` al array `enabledCssSnippets`, guarda
3. Envía plugin command `reload-snippets` para que Obsidian detecte el nuevo archivo

**Lo que el agente puede hacer con esta tool:**

- Modo sepia para el editor: sobreescribe `--background-primary` con `#f4ecd8`
- Fuente personalizada: reemplaza `--font-text-theme`
- Aumentar tamaño del grafo de nodos
- Colorizar tags por categoría (`a[href^="#domain/tech"]`)
- Ocultar elementos de UI (ribbon, status bar)
- Custom callout types con colores específicos para Cortex-L7

***

### Tool 2 — `list_css_snippets` (v1)

```csharp
[McpServerTool]
async Task<List<SnippetInfo>> list_css_snippets()
// Lee .obsidian/snippets/*.css
// Lee .obsidian/app.json → enabledCssSnippets
// Devuelve: [ { name, enabled, sizeBytes, preview: primeras 3 líneas } ]
```

***

### Tool 3 — `create_theme` (v2, el más poderoso)

Esta tool genera un tema completo de Obsidian desde cero. Un tema es local al vault — no requiere publicación ni aprobación. [zenn](https://zenn.dev/kinnkinn/articles/fcdb1ef1732619?locale=en)

```csharp
[McpServerTool, Description("Creates a complete Obsidian theme with light and dark variants")]
async Task<ThemeResult> create_theme(
    string themeName,
    ThemeConfig config
)

record ThemeConfig(
    // Paleta base
    string PrimaryBgDark,       // --background-primary dark
    string SecondaryBgDark,     // --background-secondary dark
    string PrimaryBgLight,
    string SecondaryBgLight,
    
    // Accent (HSL)
    int AccentHue,              // 0-360
    int AccentSaturation,       // 0-100
    int AccentLightness,        // 0-100
    
    // Typography
    string? FontInterface,      // null = Obsidian default
    string? FontText,
    string? FontMonospace,
    float FontSize = 16,
    
    // Personalidad
    ThemeMode ColorScheme,      // Dark | Light | Both
    bool RoundedUI = true,      // false = sharp corners
    
    // Preset base (opcional)
    string? BasePreset          // "minimal" | "cozy" | "terminal" | "academic"
);
```

**Flujo de implementación:**

```
1. Generar theme.css interpolando ThemeConfig en un template
   ├── :root { --accent-h, --accent-s, --accent-l }
   ├── body { --font-*-theme, --font-text-size, --radius-* }
   ├── .theme-light { --color-base-00..100, --background-*, --text-* }
   └── .theme-dark  { mismas variables, valores oscuros }

2. Generar manifest.json:
   {
     "name": "{themeName}",
     "version": "1.0.0",
     "minAppVersion": "1.0.0",
     "author": "Kioku MCP"
   }

3. Escribir ambos archivos en:
   {vaultPath}/.obsidian/themes/{themeName}/theme.css
   {vaultPath}/.obsidian/themes/{themeName}/manifest.json

4. Modificar .obsidian/appearance.json → "cssTheme": "{themeName}"

5. Enviar plugin command: reload-theme
```

**Presets para el agente:** El agente puede ofrecer presets predefinidos que mapean a valores concretos de las variables, por ejemplo un preset `"academic"` con fondo sepia, fuente serif para el editor, y accent azul — perfecto para las sesiones de tesis en `50_Research/`.

***

### Plugin Command — `reload-theme` / `reload-snippets` (Grupo B)

Estos son los comandos de plugin que los anteriores tools necesitan para aplicar cambios sin reiniciar Obsidian:

```typescript
// En el plugin TypeScript (obsidian-kioku-mcp)
case 'reload-snippets':
    // Obsidian expone este comando nativo
    this.app.commands.executeCommandById('app:reload-css-snippets');
    break;

case 'reload-theme':
    // Fuerza releer appearance.json y aplicar el tema
    this.app.customCss.setTheme(data.themeName);
    break;

case 'toggle-snippet':
    // Activa/desactiva un snippet por nombre
    const { snippetName, enabled } = data;
    this.app.customCss.setCssEnabledStatus(snippetName, enabled);
    break;
```

La API `app.customCss` es la interfaz pública de Obsidian para manejar themes y snippets — está documentada en la API del plugin y es la vía correcta, más estable que modificar `appearance.json` directamente desde el motor C#.

***

## Resumen de lo que puede hacer el agente con estas tools

| Petición del usuario | Tool que usa | Resultado |
|---|---|---|
| "Pon el editor en modo sepia" | `apply_css_snippet("sepia-editor", ...)` | Snippet activo en segundos |
| "Quiero un tema oscuro con accent verde para mis sesiones de código" | `create_theme("dev-dark", { AccentHue: 142, ... })` | Tema local completo instalado |
| "Coloriza los tags de `domain/research` en azul" | `apply_css_snippet("research-tags", ...)` | CSS específico para tus tags |
| "Muéstrame qué snippets tengo activos" | `list_css_snippets()` | Lista con estado enabled/disabled |
| "Desactiva el snippet de sepia" | `toggle-snippet` (plugin command) | Desactivado sin tocar archivos |
