# Plan Maestro de Arquitectura: Kioku MCP Ecosystem

> **Última revisión:** 2026-06-24 — Decisiones de dependencias AOT resueltas: Self-Contained como modelo de compilación, Ollama para embeddings, parsers manuales para YAML/Markdown.

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

## 2.1 Estado del Entorno de Desarrollo

Registro del estado actual de las herramientas y pre-requisitos en cada plataforma.

### Fedora 43 (Laptop Acer Nitro — Linux)

| Herramienta | Estado | Notas |
|---|---|---|
| .NET 10 SDK | ✅ Instalado | Verificado por el usuario |
| .NET 8 SDK | ✅ Instalado | Disponible como fallback |
| Ollama | ✅ Instalado | Corriendo en `localhost:11434` |
| `nomic-embed-text` | 🔄 Descargando | `ollama pull nomic-embed-text` en curso |
| Obsidian | ✅ Asumido | Bóveda con ~500 notas |
| Git | ✅ Asumido | Monorepo `kioku` iniciado |

### Windows 11 (PC Intel i7)

| Herramienta | Estado | Comando de instalación |
|---|---|---|
| .NET 10 SDK | ⬜ Pendiente verificar | `winget install Microsoft.DotNet.SDK.10` |
| Ollama | ⬜ Pendiente | `winget install Ollama.Ollama` |
| `nomic-embed-text` | ⬜ Pendiente | `ollama pull nomic-embed-text` |
| Obsidian | ✅ Asumido | Misma bóveda sincronizada |

> **Siguiente acción inmediata en Fedora:** Esperar a que termine `ollama pull nomic-embed-text` (~274 MB) y verificar con `ollama list` que el modelo aparece disponible. Luego ejecutar `dotnet new install Microsoft.McpServer.ProjectTemplates` para iniciar el proyecto.

---

## 3. Estructura de Proyectos (Monorepo Kioku)

Aunque son dos proyectos con tecnologías totalmente distintas, se organizan en un único repositorio Git llamado `kioku` para simplificar el despliegue, control de versiones y pruebas locales.

### Estructura de Carpetas

```
kioku/                              ← Carpeta raíz del repositorio (Monorepo)
├── .git/
├── README.md
├── .gitignore
├── docs/
│   ├── planning.md                 ← Este archivo
│   ├── v2-http-sse-spec.md         ← Especificaciones HTTP-SSE para v2
│   └── commands-reference.md       ← Inventario de comandos (MCP Tools + Plugin)
└── src/
    ├── Kioku.Mcp.Server/           ← Proyecto C# (.NET 10)
    │   ├── Kioku.Mcp.Server.csproj
    │   ├── Program.cs              ← Punto de entrada (stdio transport v1)
    │   ├── Tools/                  ← Clases marcadas con [McpServerToolType]
    │   │   ├── NoteQueryTools.cs   ← search, read, list, filter
    │   │   ├── NoteCommandTools.cs ← create, update, append, reorder
    │   │   └── ObsidianBridgeTools.cs ← open-file, get-active-note, etc.
    │   ├── Services/               ← Lógica interna (no expuesta como MCP tools)
    │   │   ├── VaultIndexService.cs   ← FileSystemWatcher + índice en memoria
    │   │   ├── MarkdownParser.cs      ← Parseo de frontmatter YAML
    │   │   ├── SemanticSearchService.cs ← Embeddings locales (Microsoft.ML)
    │   │   └── ObsidianBridgeService.cs ← WebSocket client hacia plugin
    │   └── Domain/
    │       ├── Note.cs
    │       ├── NoteMetadata.cs
    │       └── VaultIndex.cs
    │
    └── obsidian-kioku-mcp/         ← Proyecto TypeScript (Obsidian Plugin)
        ├── package.json
        ├── tsconfig.json
        ├── manifest.json           ← Metadatos del plugin para Obsidian
        ├── main.ts                 ← Código del plugin (WebSocket server local)
        └── styles.css
```

---

## 4. Arquitectura del Sistema (Estrategia del Puente IPC)

El sistema opera bajo un modelo descentralizado de comunicación local:

```
┌─────────────────────────────────────────────────────────┐
│              AGENTE DE IA (Claude Code / agy)            │
└─────────────────────┬───────────────────────────────────┘
                      │ Stdio (JSON-RPC / MCP Protocol)
                      ▼
┌─────────────────────────────────────────────────────────┐
│         Kioku.Mcp.Server  (C# .NET 10 Native AOT)       │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────┐   │
│  │ NoteQuery    │  │ NoteCommand  │  │ ObsidianBr. │   │
│  │ Tools        │  │ Tools        │  │ Tools       │   │
│  └──────┬───────┘  └──────┬───────┘  └──────┬──────┘   │
│         └─────────────────┴──────────────────┘          │
│                           │                             │
│              ┌────────────┴──────────────┐              │
│              │      VaultIndexService    │              │
│              │  (FileSystemWatcher +     │              │
│              │   In-Memory Index +       │              │
│              │   Semantic Embeddings)    │              │
│              └────────────┬─────────────┘               │
│                           │ WebSocket Client             │
└───────────────────────────┼────────────────────────────┘
                            │ WebSocket (localhost)
                            ▼
┌─────────────────────────────────────────────────────────┐
│      Plugin Obsidian (TypeScript — Thin Client)         │
│         WebSocket Server Local (puerto configurable)    │
└─────────────────────────┬───────────────────────────────┘
                          │ Obsidian Plugin API
                          ▼
┌─────────────────────────────────────────────────────────┐
│                    Obsidian App                         │
│            (Electron / Chromium + Node.js)              │
└─────────────────────────────────────────────────────────┘
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
| v1 | Texto plano | Índice invertido en memoria (`Dictionary<string, HashSet<string>>`) | 📋 Planificado |
| v1 | Texto plano | Parser manual con `Span<char>` para frontmatter YAML | 📋 Planificado |
| v2 | Semántica | **Ollama** (`nomic-embed-text`) vía HTTP `localhost:11434` | ✅ Motor listo en Fedora |
| v2 | Semántica | Embeddings persistidos en SQLite (`Microsoft.Data.Sqlite`) | 📋 Planificado |

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

### v1 — MVP (Transporte Stdio)

**Objetivo:** Servidor MCP funcional que el agente de IA puede usar sin Obsidian.

- [x] Plan arquitectural
- [ ] Inicializar proyecto con `dotnet new mcpserver`
- [ ] `NoteQueryTools`: `search_notes`, `read_note`, `list_notes`, `filter_notes`
- [ ] `VaultIndexService` con FileSystemWatcher + debouncing
- [ ] Parseo de frontmatter YAML con `Span<char>`
- [ ] Índice invertido en memoria para búsqueda por texto
- [ ] Compilación y prueba en Fedora 43 y Windows 11
- [ ] Plugin TypeScript mínimo (WebSocket server + comandos básicos)

### v2 — HTTP-SSE + Búsqueda Semántica

Ver especificaciones completas en [`docs/v2-http-sse-spec.md`](./v2-http-sse-spec.md).

**Resumen:**
- Transporte HTTP-SSE adicional al stdio (múltiples agentes simultáneos)
- Búsqueda semántica con embeddings locales (Microsoft.ML + ONNX)
- Cache de vectores en SQLite
- Soporte mejorado para assets (Excalidraw, imágenes, tablas)
- Comandos avanzados de organización (reordenar, clasificar, reclasificar tags)

### v3 — Native AOT Optimization + Publicación

- Publicar binarios AOT para Windows 11 (x64) y Fedora 43 (x64)
- Validar compatibilidad AOT de todas las dependencias
- Benchmarking de RAM y tiempo de arranque
- Candidato a publicación en Obsidian Community Plugin Store

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

### Tabla de Dependencias NuGet — Estado Definitivo

| Paquete | Propósito | AOT Compat. | Decisión Final |
|---|---|---|---|
| `ModelContextProtocol` | SDK MCP oficial (stdio) | ✅ | ✅ **Usar** |
| `ModelContextProtocol.AspNetCore` | Transporte HTTP-SSE (v2) | ✅ | ✅ **Usar en v2** |
| `Microsoft.Data.Sqlite` | Persistencia de vectores | ✅ | ✅ **Usar en v2** |
| `System.Numerics.Tensors` | Cosine similarity (vectores) | ✅ .NET 9+ | ✅ **Usar en v2** |
| `YamlDotNet` | Parseo YAML | ❌ Reflexión | 🔄 **Reemplazar** — ver alternativa abajo |
| `Markdig` | Parseo Markdown | ⚠️ Advertencias | 🔄 **Reemplazar** — ver alternativa abajo |
| `Microsoft.ML.OnnxRuntime` | Embeddings locales ONNX | ❌ Wrapper nativo | ❌ **Descartar** — reemplazado por Ollama |
| `OllamaSharp` _(opcional)_ | Cliente Ollama tipado | ✅ (solo HTTP) | 💡 **Opcional** — `HttpClient` nativo es suficiente |

---

### Alternativas Implementadas

#### 🔄 YAML → Parser Manual con `Span<char>`

El frontmatter de Obsidian es **extremadamente predecible**. Siempre sigue el formato:
```
---
clave: valor
tags: [tag1, tag2]
fecha: 2024-01-15
---
```
No se necesita una librería completa como `YamlDotNet`. Un parser de 100-150 líneas con `ReadOnlySpan<char>` es suficiente, más rápido y totalmente AOT-safe:

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

#### 🔄 Markdig → Extractor de Texto Manual

Para la indexación de búsqueda, solo necesitamos **texto limpio** (sin sintaxis Markdown). No necesitamos renderizar HTML. Un extractor de ~80 líneas con `Span<char>` elimina:
- `#` de encabezados
- `**bold**`, `_italic_` de énfasis
- `[[wikilinks]]` y `[text](url)` de enlaces
- ` ```code``` ` de bloques de código
- Bloques de frontmatter YAML

Si en el futuro se necesita renderizado HTML completo (para previsualización), añadir `Markdig` como dependencia **opcional y no-AOT** en una capa separada.

#### ❌ ONNX Runtime → Ollama (Embeddings vía HTTP local)

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
