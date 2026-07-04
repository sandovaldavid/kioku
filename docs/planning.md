# Plan Maestro de Arquitectura: Kioku MCP Ecosystem

> **Última revisión:** 2026-07-02 — v2 completado, v3 en producción. Este documento refleja el estado actual de la arquitectura tras la implementación de las 17 tool classes (102 herramientas MCP) y el transporte HTTP-SSE. Los specs de las próximas features viven en [`docs/features/`](./features/README.md) y el desglose de trabajo en [`docs/tasks/`](./tasks/README.md).

Este documento describe la estrategia de diseño, la selección tecnológica y los conceptos clave para construir un ecosistema de acceso a notas de alto rendimiento, optimizado para ser consumido por agentes de IA como Claude Code y Antigravity CLI en entornos multiplataforma (Windows 11 / Fedora 43).

---

## 1. Recomendación Tecnológica y Lenguajes

Para lograr el máximo rendimiento, eficiencia y mantenibilidad en este sistema híbrido, se propone una arquitectura de **"Doble Componente"** utilizando las versiones más recientes del stack tecnológico:

### A. Para el Servidor MCP (Motor de Procesamiento): C# (.NET 10) — Self-Contained

- **¿Por qué .NET 10?:** .NET 10 introduce mejoras sustanciales en rendimiento, soporte oficial para el SDK MCP (`Microsoft.McpServer.ProjectTemplates`), y el modo **Self-Contained** que publica un ejecutable portátil sin requerir .NET instalado en la máquina destino — portabilidad total sin las restricciones de Native AOT. (.NET 10 ya instalado en Fedora 43 ✅)

- **Template oficial de Microsoft:** Existe el template `Microsoft.McpServer.ProjectTemplates` (preview para .NET 10) que genera el esqueleto completo del servidor MCP con soporte de transporte stdio/HTTP y configuración AOT automática:
  ```bash
  dotnet new install Microsoft.McpServer.ProjectTemplates
  dotnet new mcpserver -n Kioku.Mcp.Server
  ```

- **Modelo de compilación elegido — Self-Contained:** Publica un ejecutable único que incluye el runtime de .NET. No requiere que .NET esté instalado en la máquina donde corra. Startup ~200ms — perfectamente aceptable para una herramienta local bajo demanda. Se evalúa Native AOT como optimización opcional en v3.

- **Portabilidad:** Un único código fuente, dos compilaciones — `linux-x64` para Fedora 43 y `win-x64` para Windows 11.

- **Inicio bajo demanda:** El servidor arranca únicamente cuando un agente de IA lo invoca. No requiere servicio de sistema ni autoarranque.

### B. Para el Plugin de Obsidian (Puente de Interfaz): TypeScript (JS Nativo)

- **¿Por qué?:** Obsidian está construido sobre Electron (Chromium + Node.js), por lo que **TypeScript** es el único lenguaje soportado nativamente para interactuar con su API interna.

- **Estándares de publicación:** El plugin seguirá los **estándares oficiales del Community Plugin Store de Obsidian** desde el inicio, permitiendo su publicación futura sin refactorización.

- **Rol del Plugin:** Se mantendrá al mínimo absoluto de su peso (_Thin Client_). No realizará procesamiento pesado ni indexación de archivos; solo actuará como un receptor de comandos visuales que se comunica con el servidor de C#.

---

## 2. Contexto de la Bóveda

| Característica | Detalle |
|---|---|
| **Notas Markdown** | ~500 archivos `.md` |
| **Assets visuales** | Imágenes, diagramas Excalidraw (`.excalidraw`) |
| **Bases de datos** | Tablas nativas de Obsidian (Dataview/DB Folder) |
| **Búsqueda requerida** | Texto plano **+** Semántica (vectores) |
| **Audiencia** | Personal, con posibilidad de publicación comunitaria |

---

## 2.1 Entorno de Desarrollo

Ambos entornos (Fedora 43 y Windows 11) están operativos: .NET 10 SDK, Ollama con `nomic-embed-text` (768-dim) en `localhost:11434`, Obsidian y el monorepo `kioku`. Los pre-requisitos de instalación para usuarios finales están documentados en [`docs/install.md`](./install.md).

---

## 3. Estructura de Proyectos (Monorepo Kioku)

Aunque son dos proyectos con tecnologías totalmente distintas, se organizan en un único repositorio Git llamado `kioku` para simplificar el despliegue, control de versiones y pruebas locales.

### Estructura de Carpetas

```
kioku/                              ← Carpeta raíz del repositorio (Monorepo)
├── .git/
├── README.md
├── AGENTS.md                       ← Contexto para agentes de IA (Kioku mismo)
├── .gitignore
├── docs/
│   ├── planning.md                 ← Este archivo
│   ├── commands-reference.md       ← Inventario de comandos (MCP Tools + Plugin)
│   ├── v2-http-sse-spec.md         ← Especificaciones HTTP-SSE para v2
│   └── deploy/
│       ├── auth-options.md         ← Opciones de autenticación para despliegue
│       ├── kioku.service           ← systemd unit para VM
│       └── nginx.conf              ← Reverse proxy nginx
└── src/
    ├── Kioku.Mcp.Server/           ← Proyecto C# (.NET 10)
    │   ├── Kioku.Mcp.Server.csproj
    │   ├── Program.cs              ← Punto de entrada (stdio y HTTP-SSE)
    │   ├── KiokuConfiguration.cs   ← Variables de entorno
    │   ├── Middleware/
    │   │   └── ApiKeyMiddleware.cs ← Bearer token auth
    │   ├── Tools/                  ← 17 tool classes registradas
    │   │   ├── NoteQueryTools.cs   ← search, read, list, filter
    │   │   ├── NoteCommandTools.cs ← create, update, append, delete
    │   │   ├── ObsidianBridgeTools.cs ← open-file, get-active-note, etc.
    │   │   ├── TaskManagementTools.cs  ← list_tasks, complete_task, etc.
    │   │   ├── ZettelkastenTools.cs    ← create_zettel, create_moc, etc.
    │   │   ├── VaultOrganizationTools.cs ← normalize_tags, merge_tags, etc.
    │   │   ├── SessionContextTools.cs  ← start/end_work_session, etc.
    │   │   ├── WorkflowTools.cs        ← create_note_from_template, etc.
    │   │   ├── CssThemingTools.cs      ← apply_css_snippet, etc.
    │   │   ├── KnowledgeGraphTools.cs  ← get_concept_map, etc.
    │   │   ├── ResearchTools.cs        ← export_citations, etc.
    │   │   ├── PluginIntegrationTools.cs ← Dataview, Templater, Linter
    │   │   ├── GraphAnalysisTools.cs   ← find_unlinked_notes, etc.
    │   │   ├── GitTools.cs            ← get_git_status, stage/commit, etc.
    │   │   ├── RestoreTools.cs        ← revert_note, restore_note_from_trash, etc.
    │   │   ├── AssetTools.cs          ← list_excalidraw_files, etc.
    │   │   └── UtilityTools.cs        ← ping, rebuild_index, etc.
    │   ├── Services/               ← Lógica interna (no expuesta como MCP tools)
    │   │   ├── VaultIndexService.cs   ← FileSystemWatcher + índice invertido
    │   │   ├── EmbeddingService.cs    ← Ollama embeddings vía HTTP
    │   │   ├── EmbeddingPersistence.cs ← Caché binaria (.kioku/embeddings.bin, formato v3)
    │   │   ├── HybridSearchService.cs ← Búsqueda combinada (keyword + semántica)
    │   │   ├── TaskService.cs         ← Parseo de checkboxes nativos
    │   │   ├── ObsidianBridgeService.cs ← WebSocket client hacia plugin
    │   │   ├── VaultConfigService.cs  ← .kioku/config.yml + capability groups
    │   │   ├── FrontmatterParser.cs   ← Parser YAML manual (Span<char>)
    │   │   ├── MarkdownTextExtractor.cs ← Markdown → texto plano + wikilinks
    │   │   ├── MetricsService.cs      ← Contadores de tools (opt-in)
    │   │   └── FolderRanker.cs        ← Ranking de carpetas (suggest_folder)
    │   └── Domain/
    │       ├── Note.cs
    │       ├── NoteMetadata.cs
    │       ├── SearchResult.cs
    │       ├── TaskItem.cs
    │       ├── KiokuError.cs
    │       └── EmbeddingModelRegistry.cs
    │
    └── obsidian-kioku-mcp/         ← Proyecto TypeScript (Obsidian Plugin)
        ├── package.json
        ├── tsconfig.json
        ├── manifest.json           ← Metadatos del plugin para Obsidian
        ├── styles.css
        └── src/
            ├── main.ts             ← Entry point (KiokuPlugin + settings tab)
            ├── bridge.ts           ← WebSocket server local (BridgeServer)
            ├── handlers.ts         ← 22 comandos del bridge
            ├── types.ts            ← Protocolo compartido (PROTOCOL_VERSION)
            ├── logger.ts           ← Logger tipado
            └── protocol-schema.json ← JSON-Schema del wire format
```

---

## 4. Arquitectura del Sistema (Estrategia del Puente IPC)

El sistema opera bajo un modelo descentralizado de comunicación local:

```
┌───────────────────────────────────────────────────────────────┐
│              AGENTE DE IA (Claude Code / agy)                  │
└───┬───────────────────────────────────────────────────┬───────┘
    │ Stdio (v1 — JSON-RPC / MCP)                        │ HTTP-SSE (v2)
    ▼                                                     ▼
┌───────────────────────────────────────────────────────────────┐
│              Kioku.Mcp.Server  (C# .NET 10)                   │
│                                                               │
│  ┌───────────────────────────────────────────────────────┐    │
│  │              17 Tool Classes (McpServerTool)           │    │
│  │  Query · Command · Bridge · Tasks · Zettelkasten      │    │
│  │  Org · Sessions · Workflows · CSS · KnowledgeGraph    │    │
│  │  Research · PluginInt · GraphAnalysis · Git · Restore │    │
│  │  Assets · Utility                                     │    │
│  └───────────────────────┬───────────────────────────────┘    │
│                          │                                    │
│  ┌───────────────────────┴───────────────────────────────┐    │
│  │  Services: VaultIndex · Embedding(Ollama) · Hybrid    │    │
│  │           TaskService · ObsidianBridge · Persistence  │    │
│  └───────────────────────┬───────────────────────────────┘    │
│                          │ WebSocket Client                   │
└──────────────────────────┼──────────────────────────────────┘
                           │ WebSocket :7765 (localhost)
                           ▼
┌───────────────────────────────────────────────────────────────┐
│      Plugin Obsidian (TypeScript — Thin Client)               │
│         WebSocket Server Local (KIOKU_OBSIDIAN_PORT)           │
└───────────────────────────┬───────────────────────────────────┘
                            │ Obsidian Plugin API
                            ▼
┌───────────────────────────────────────────────────────────────┐
│                    Obsidian App                               │
│            (Electron / Chromium + Node.js)                    │
└───────────────────────────────────────────────────────────────┘
```

- **Desacoplamiento Total:** Si Obsidian está cerrado, el Agente de IA aún puede buscar, leer y escribir notas porque el Motor de C# procesa los archivos Markdown directamente en disco.

- **Sincronización en Vivo:** Si Obsidian está abierto, el motor de C# envía notificaciones vía WebSockets al plugin para reflejar inmediatamente los cambios visuales en la pantalla.

- **Inicio bajo demanda:** El servidor no corre como servicio de sistema. Arranca cuando el agente de IA lo invoca vía stdio y termina cuando la sesión del agente finaliza.

---

## 5. Puntos Clave para el Servidor MCP (Kioku.Mcp.Server)

### Procesamiento de Bajo Costo (Zero-Allocation) con .NET 10

- Utilizar las optimizaciones de `System.Text.Json` para leer y escribir los mensajes JSON-RPC del protocolo MCP directamente sobre buffers de memoria (`Span<T>` / `ReadOnlySpan<T>`), evitando la instanciación de strings innecesarias en el Heap.

- Para parsear frontmatter YAML: preferir parseo manual con `Span<char>` en lugar de librerías que usen reflexión internamente (incompatibles con AOT).

### Generadores de Código de Serialización (AOT Safe)

- Dado que Native AOT deshabilita la reflexión dinámica, usar obligatoriamente `JsonSerializableAttribute` y `JsonSourceGenerationOptions` para generar los serializadores en tiempo de compilación.

- **No usar MediatR** (usa reflexión internamente). Usar el patrón nativo `[McpServerToolType]` + `[McpServerTool]` del SDK oficial.

### Patrón de Tools (CQRS Simplificado con SDK MCP)

- **Queries** → `NoteQueryTools.cs`: buscar, leer, listar, filtrar notas
- **Commands** → `NoteCommandTools.cs`: crear, actualizar, añadir, reordenar
- **Bridge** → `ObsidianBridgeTools.cs`: interacción con la UI de Obsidian

### Indexador en Tiempo Real con Debouncing

```csharp
watcher.Filter = "*.md";                          // Solo archivos Markdown
watcher.NotifyFilter = NotifyFilters.LastWrite
                     | NotifyFilters.FileName;
watcher.IncludeSubdirectories = true;
watcher.InternalBufferSize = 65536;               // 64KB máximo recomendado
watcher.EnableRaisingEvents = true;
// En Fedora si el FS no notifica: DOTNET_USE_POLLING_FILE_WATCHER=1
```

> **Nota:** FileSystemWatcher puede perder eventos si el buffer se desborda en vaults activos. Implementar debouncing de ~500ms para agrupar ráfagas de cambios.

### Búsqueda Dual (Texto + Semántica)

Para una bóveda de ~500 notas con imágenes y Excalidraw:

| Fase | Tipo | Tecnología | Estado |
|---|---|---|---|
| v1 | Texto plano | Índice invertido en memoria (`Dictionary<string, HashSet<string>>`) | ✅ Implementado (`VaultIndexService`) |
| v1 | Texto plano | Parser manual con `Span<char>` para frontmatter YAML | ✅ Implementado (`FrontmatterParser`) |
| v2 | Semántica | **Ollama** (`nomic-embed-text`) vía HTTP `localhost:11434` | ✅ Implementado (`EmbeddingService`) |
| v2 | Semántica | Embeddings persistidos en caché binaria `.kioku/embeddings.bin` (formato v3) | ✅ Implementado (SQLite descartado — `EmbeddingPersistence`) |

**Cómo usar Ollama para embeddings desde C#:**
```bash
# Verificar que el modelo está disponible
ollama list
# Debe mostrar: nomic-embed-text

# Prueba manual del endpoint (desde terminal)
curl http://localhost:11434/api/embed -d '{"model":"nomic-embed-text","input":"hola mundo"}'
# Responde: {"embeddings": [[0.123, -0.456, ...]]}
```

---

## 6. Puntos Clave para el Plugin de Obsidian (obsidian-kioku-mcp)

### Estándares de Publicación en Community Store

El plugin seguirá desde el inicio las guías oficiales de Obsidian para plugins de comunidad:
- `manifest.json` con `minAppVersion`, `version`, `author`, `authorUrl`
- Sin dependencias externas no aprobadas por la tienda
- Manejo correcto del ciclo de vida (`onload` / `onunload`)
- Sin acceso a APIs no documentadas de Obsidian

### No Interferencia con el Hilo Principal

Toda la comunicación de red (WebSockets) debe ser asíncrona y no bloqueante. Obsidian no debe congelarse ni perder FPS en pantallas de alta tasa de refresco (144Hz / 165Hz) mientras el agente realiza búsquedas.

### Comandos del Plugin

Ver [`docs/commands-reference.md`](./commands-reference.md) para el inventario completo de comandos del plugin y del servidor MCP.

---

## 7. Versiones y Hoja de Ruta

### v1 — MVP (Transporte Stdio) ✅ COMPLETADO

**Objetivo:** Servidor MCP funcional que el agente de IA puede usar sin Obsidian.

- 11 herramientas core (lectura, escritura, utilidades)
- Plugin TypeScript con WebSocket bridge
- FileSystemWatcher + índice invertido en memoria

### v2 — HTTP-SSE + Búsqueda Semántica ✅ COMPLETADO

Ver especificaciones completas en [`docs/v2-http-sse-spec.md`](./v2-http-sse-spec.md).

**Resumen:**
- Transporte HTTP-SSE adicional al stdio (múltiples agentes simultáneos)
- Búsqueda semántica con Ollama (`nomic-embed-text`, 768-dim)
- Caché binaria persistente en `vault/.kioku/embeddings.bin`
- Bearer Token auth (ApiKeyMiddleware)
- Búsqueda híbrida (keyword + semántica con RRF)
- Despliegue en VM con systemd + nginx

### v3 — Ecosystem Tools ✅ COMPLETADO

**102 herramientas implementadas en 17 tool classes:**

| Categoría | Tools |
|---|---|
| Session & Context | `get_recent_activity`, `get_work_context`, `start/end_work_session`, `list_work_sessions`, `get_session_activity` |
| Task Management | `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`, `list_overdue_tasks` |
| Zettelkasten | `create_zettel`, `create_moc`, `create_folder_readme`, `link_related_notes`, `create_literature_note` |
| Workflows & Templates | `create_note_from_template`, `list_templates`, `create_template`, `extract_action_items` |
| Tag & Org | `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`, `find_duplicate_notes`, `audit_vault`, `find_broken_links`, `reclassify_note`, `suggest_folder` |
| CSS Theming | `apply_css_snippet`, `list_css_snippets`, `remove_css_snippet` |
| Knowledge Graph | `get_knowledge_timeline`, `get_concept_map`, `get_vault_snapshot` |
| Research | `export_citations`, `export_note`, `get_literature_gap`, `share_as_gist`, `validate_research_notes` |
| Graph Analysis | `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density` |
| Git Integration | `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`, `commit_staged` |
| Restore | `revert_note`, `list_deleted_notes`, `restore_note_from_trash`, `restore_note_version`, `revert_all_uncommitted` |
| Assets | `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`, `normalize_attachment_names`, `move_attachments_to_folder`, `reorder_notes_in_folder` |
| Plugin Integration | `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins`, `fix_merge_conflicts`, `resolve_merge_conflict` |

Las clases fuera del núcleo se activan por grupos de capacidades en `.kioku/config.yml`
(ver [`docs/vault-config.md`](./vault-config.md)). Inventario completo en
[`docs/commands-reference.md`](./commands-reference.md).

### v4 — Futuro (Propuesto)

Los specs detallados de la siguiente ola de features viven en [`docs/features/`](./features/README.md),
con su desglose de trabajo priorizado en [`docs/tasks/`](./tasks/README.md). Líneas principales:

- Generación local con Ollama (`KIOKU_GEN_MODEL`) — enabler de digest, flashcards y síntesis
- Sugerencias de enlaces, smart inbox y daily digest (fortalecer el grafo)
- MCP Prompts & Resources (workflows empaquetados para cualquier cliente MCP)
- Autenticación del bridge WebSocket y UI de estado en el plugin
- Zotero/BibTeX, flashcards/Anki, re-embedding incremental
- Native AOT optimization para startups más rápidos
- Publicación en Obsidian Community Plugin Store
- Streaming de cambios en tiempo real (SSE server-sent events)

---

## 8. Dependencias NuGet — Decisiones Finales

### Contexto de la Decisión de Compilación

El servidor Kioku **NO es un microservicio en la nube**. Es una herramienta local de escritorio que:
- Arranca bajo demanda cuando el agente de IA lo invoca.
- Corre exclusivamente en las máquinas del desarrollador (Windows 11 + Fedora 43).
- Tiene .NET 10 instalado en ambos entornos.

Por esto, la estrategia de compilación óptima es **Self-Contained** (no AOT estricto), al menos para v1 y v2:

| Modelo | Startup | RAM | Restricciones | Uso ideal |
|---|---|---|---|---|
| **Self-Contained** ✅ | ~200ms | Normal | Ninguna | v1, v2 — herramienta local |
| Native AOT | ~50ms | Muy bajo | Sin reflexión, sin ONNX | v3 — opcional si startup importa |
| Framework-Dependent | ~150ms | Normal | Requiere .NET instalado | Desarrollo solamente |

> **Decisión:** Usar **Self-Contained** para v1 y v2. Evaluar Native AOT en v3 solo si el tiempo de arranque es un problema medible.
> Self-Contained publica un ejecutable que NO requiere .NET instalado en la máquina destino — portabilidad sin las restricciones de AOT.

---

### Dependencias NuGet — Estado Actual

| Paquete | Propósito | Estado |
|---|---|---|
| `ModelContextProtocol` | SDK MCP oficial (stdio) | ✅ En uso |
| `ModelContextProtocol.AspNetCore` | Transporte HTTP-SSE (v2) | ✅ En uso |
| `System.Numerics.Tensors` | Cosine similarity (vectores) | ✅ En uso |
| `YamlDotNet` | Parseo YAML | ✅ En uso (VaultConfigService para config.yml) |
| `Markdig` | Parseo/Render Markdown | ✅ En uso (export HTML, text extraction) |
| `Microsoft.ML.OnnxRuntime` | Embeddings locales ONNX | ❌ Reemplazado por Ollama |
| `OllamaSharp` _(opcional)_ | Cliente Ollama tipado | ❌ `HttpClient` nativo es suficiente |

---

### Alternativas Implementadas (Verificadas en Producción)

#### ✅ YAML → Parser Manual para frontmatter + YamlDotNet para config

El frontmatter de Obsidian es **extremadamente predecible**. Siempre sigue el formato:
```
---
clave: valor
tags: [tag1, tag2]
fecha: 2024-01-15
---
```
No se necesita una librería completa para el frontmatter. Un parser de 100-150 líneas con `ReadOnlySpan<char>` es suficiente, más rápido y totalmente AOT-safe:

```csharp
// FrontmatterParser.cs — Zero-allocation, sin dependencias externas
public static class FrontmatterParser
{
    public static NoteMetadata Parse(ReadOnlySpan<char> content)
    {
        if (!content.StartsWith("---")) return NoteMetadata.Empty;
        // Iterar línea a línea con MemoryExtensions.Split()
        // sin crear strings intermedios en el Heap
    }
}
```

**Nota:** `YamlDotNet` se añadió posteriormente para `VaultConfigService` (config.yml),
que requiere parseo YAML más complejo. El parser manual sigue usándose para frontmatter.

#### ✅ Markdig — Extractor de Texto + Renderizado HTML

Para la indexación de búsqueda, solo necesitamos **texto limpio** (sin sintaxis Markdown). `MarkdownTextExtractor` elimina:
- `#` de encabezados
- `**bold**`, `_italic_` de énfasis
- `[[wikilinks]]` y `[text](url)` de enlaces
- ` ```code``` ` de bloques de código
- Bloques de frontmatter YAML

`Markdig` se usa además para renderizado HTML completo (export de notas, herramientas de investigación).

#### ✅ ONNX Runtime → Ollama (Embeddings vía HTTP local)

`Microsoft.ML.OnnxRuntime` es un wrapper nativo sobre una DLL de C++. **No es compatible con Native AOT** y es complejo de distribuir.

**Solución: Ollama** — servicio local de modelos de IA que expone una API HTTP compatible con OpenAI:

```csharp
// EmbeddingService.cs — AOT-safe: solo HttpClient + System.Text.Json
public class OllamaEmbeddingService
{
    private readonly HttpClient _http;
    private const string Model = "nomic-embed-text"; // 274MB, Apache 2.0

    // POST http://localhost:11434/api/embed
    // { "model": "nomic-embed-text", "input": "texto a vectorizar" }
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var request = new { model = Model, input = text };
        var response = await _http.PostAsJsonAsync("/api/embed", request);
        // Responde: { "embeddings": [[0.1, 0.2, ...]] } — vectores L2-normalizados
        var result = await response.Content
            .ReadFromJsonAsync<OllamaEmbedResponse>(OllamaJsonContext.Default.OllamaEmbedResponse);
        return result!.Embeddings[0];
    }
}

// Source-generated JSON context — AOT-safe
[JsonSerializable(typeof(OllamaEmbedResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext { }
```

**Ventajas de Ollama sobre ONNX Runtime:**

| Característica | ONNX Runtime | Ollama |
|---|---|---|
| AOT compatible | ❌ | ✅ (solo HTTP calls) |
| Requiere GPU | Opcional | No |
| Privacidad | Local | Local |
| Modelos disponibles | Cualquier ONNX | Llama, Mistral, nomic-embed-text, etc. |
| Actualizar modelo | Recompilar | `ollama pull <modelo>` |
| Overhead cuando inactivo | 0 (no corre) | ~50MB RAM (servicio en background) |
| Velocidad (500 notas, CPU i7) | ~50ms/nota | ~60ms/nota |

> **Pre-requisito para v2:** El usuario debe tener **Ollama instalado** (`winget install Ollama.Ollama` en Windows, `flatpak install ollama` en Fedora).
> El servidor Kioku debe verificar si Ollama está disponible al arrancar v2 y degradar graciosamente a solo-texto si no lo está.

---

### Regla de Oro Revisada

> Para v1/v2 (Self-Contained): evitar librerías con reflexión **no porque rompa la compilación**, sino porque:
> 1. Aumentan el tamaño del ejecutable.
> 2. Reducen el rendimiento en tiempo de ejecución.
> 3. Dificultan la migración futura a AOT en v3.
>
> Para v3 (Native AOT): ninguna dependencia con reflexión dinámica. Todo debe usar source generators o parseo manual.
