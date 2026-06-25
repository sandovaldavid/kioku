# Especificaciones v2: Transporte HTTP-SSE y Búsqueda Semántica

> **Estado:** Planificación — Para implementar tras estabilizar v1 (stdio).
> **Referencia:** [planning.md](./planning.md) · [commands-reference.md](./commands-reference.md)

---

## Motivación para v2

La v1 del servidor Kioku MCP opera exclusivamente con transporte **stdio**, lo que significa:
- Un único agente de IA puede conectarse a la vez.
- El servidor vive durante la sesión del agente y termina cuando este lo hace.
- No existe persistencia del índice entre sesiones (se re-indexa en cada arranque).

La v2 resuelve estos tres problemas añadiendo:
1. **Transporte HTTP-SSE** para conexiones simultáneas de múltiples agentes.
2. **Búsqueda semántica** con embeddings locales persistidos entre sesiones.
3. **Soporte mejorado de assets** no-Markdown (Excalidraw, imágenes, bases de datos Obsidian).

---

## 1. Transporte HTTP-SSE (Streamable HTTP)

### ¿Por qué HTTP-SSE sobre SSE puro?

El protocolo MCP 2025 define **Streamable HTTP** como el transporte recomendado para servidores remotos o multi-cliente. Este reemplaza al SSE unidireccional previo y permite:

- Mensajes bidireccionales sobre HTTP estándar.
- Conexiones múltiples desde distintos agentes (Claude Code + agy simultáneamente).
- Compatibilidad con proxies, firewalls y herramientas de debug estándar.

### Arquitectura de Transporte Dual

```
                 ┌─────────────────────────────┐
                 │   Agente A (Claude Code)     │
                 └──────────────┬──────────────┘
                                │ stdio (v1 — siempre disponible)
                                ▼
┌──────────────────────────────────────────────────────────┐
│                  Kioku.Mcp.Server v2                     │
│                                                          │
│  ┌─────────────────────┐   ┌──────────────────────────┐  │
│  │  Stdio Transport    │   │  HTTP-SSE Transport       │  │
│  │  (v1, siempre ON)   │   │  (v2, localhost:5173)     │  │
│  └─────────────────────┘   └──────────────────────────┘  │
│                                       ▲                  │
│                                       │                  │
└───────────────────────────────────────┼──────────────────┘
                                        │ HTTP POST / GET (SSE)
                 ┌──────────────────────┴──────────────────┐
                 │   Agente B (agy CLI / otro Claude)       │
                 └─────────────────────────────────────────┘
```

### Implementación en C# (.NET 10)

```csharp
// Program.cs — v2 con transporte dual
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()          // HTTP-SSE (v2)
    .WithToolsFromAssembly();     // Registra todos los [McpServerToolType]

// Solo CORS local: el servidor no es público
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost", "app://obsidian.md")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Servicios internos
builder.Services.AddSingleton<VaultIndexService>();
builder.Services.AddSingleton<SemanticSearchService>();
builder.Services.AddSingleton<ObsidianBridgeService>();

var app = builder.Build();

app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "2.0" }));
app.MapMcp("/mcp");   // Endpoint MCP principal

app.Run("http://localhost:5173");
```

### Configuración para Agentes

Para registrar el servidor HTTP en Claude Code o agy:

```json
// .mcp.json (versión HTTP)
{
  "servers": {
    "kioku": {
      "type": "http",
      "url": "http://localhost:5173/mcp"
    }
  }
}
```

```json
// .mcp.json (versión stdio — v1, sigue funcionando)
{
  "servers": {
    "kioku": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "./src/Kioku.Mcp.Server"]
    }
  }
}
```

### Consideraciones de Seguridad

- El servidor HTTP escucha **solo en `localhost`** — nunca en `0.0.0.0`.
- No se implementa autenticación en v2 (uso personal local).
- Si se publica el plugin de Obsidian para la comunidad, documentar que el servidor escucha en un puerto local predeterminado (configurable).
- Puerto por defecto: **5173** (elegido para no colisionar con puertos comunes: 3000, 8080, etc.).

### Gestión del Ciclo de Vida en v2

En v2 el servidor tiene dos modos de arranque:

| Modo | Cómo arranca | Cómo termina |
|---|---|---|
| **Bajo demanda** (v1 heredado) | `dotnet run` invocado por el agente vía stdio | Cuando el agente cierra la sesión |
| **Persistente** (v2 nuevo) | Lanzado manualmente por el usuario | Manual (`Ctrl+C`) o cierre del terminal |

> El modo persistente es opcional y conveniente cuando se trabaja con múltiples agentes simultáneos o sesiones largas de trabajo.

---

## 2. Búsqueda Semántica Local

### Diseño General

Para una bóveda de ~500 notas, la búsqueda semántica con embeddings vía Ollama es completamente viable en CPU, sin GPU y sin APIs externas de pago:

```
Nota .md
   │
   ▼
MarkdownTextExtractor      ← Parser manual (Span<char>) — extrae texto limpio
   │
   ▼
OllamaEmbeddingService     ← POST http://localhost:11434/api/embed
   │                          modelo: nomic-embed-text (local, Apache 2.0)
   ▼
float[] vector[768]        ← Embedding de 768 dimensiones, L2-normalizado
   │
   ▼
VectorStore (SQLite)       ← Persistencia entre sesiones del agente
```

> **Pre-requisito:** Ollama debe estar instalado y corriendo en `localhost:11434`.
> El servidor Kioku detecta Ollama al arrancar y degrada a búsqueda solo-texto si no está disponible.

### Modelo de Embeddings Elegido

**`nomic-embed-text`** (vía Ollama):

| Característica | Valor |
|---|---|
| Dimensiones | 768 |
| Tamaño del modelo | ~274 MB (descarga única) |
| Velocidad (CPU Intel i7) | ~60ms por nota |
| Licencia | Apache 2.0 (compatible con publicación) |
| Distribución | `ollama pull nomic-embed-text` |
| Privacidad | 100% local — ningún dato sale del equipo |

Para 500 notas, la indexación inicial toma ~30 segundos. Luego solo se re-indexan las notas modificadas (detectadas por hash MD5 del contenido).

**Instalación de Ollama:**
```bash
# Windows 11
winget install Ollama.Ollama

# Fedora 43
flatpak install flathub io.github.ollama
# o vía script oficial:
curl -fsSL https://ollama.com/install.sh | sh

# Descargar el modelo de embeddings
ollama pull nomic-embed-text
```

### Implementación: OllamaEmbeddingService

```csharp
// Services/OllamaEmbeddingService.cs — solo HttpClient + System.Text.Json (AOT-safe)
public class OllamaEmbeddingService(HttpClient http)
{
    private const string Model = "nomic-embed-text";
    private const string OllamaEndpoint = "http://localhost:11434";

    // Verifica si Ollama está disponible al arrancar
    public async Task<bool> IsAvailableAsync()
    {
        try {
            var response = await http.GetAsync($"{OllamaEndpoint}/api/tags");
            return response.IsSuccessStatusCode;
        } catch { return false; }
    }

    // Genera embedding para una nota o query
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var request = new OllamaEmbedRequest(Model, text);
        var response = await http.PostAsJsonAsync(
            $"{OllamaEndpoint}/api/embed",
            request,
            OllamaJsonContext.Default.OllamaEmbedRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<OllamaEmbedResponse>(
                OllamaJsonContext.Default.OllamaEmbedResponse);
        return result!.Embeddings[0]; // Ya normalizado (L2)
    }

    // Batch: indexa múltiples notas en una sola llamada a Ollama
    public async Task<float[][]> GetEmbeddingsBatchAsync(IEnumerable<string> texts)
    {
        var request = new OllamaEmbedBatchRequest(Model, texts.ToArray());
        var response = await http.PostAsJsonAsync(
            $"{OllamaEndpoint}/api/embed",
            request,
            OllamaJsonContext.Default.OllamaEmbedBatchRequest);
        var result = await response.Content
            .ReadFromJsonAsync<OllamaEmbedResponse>(
                OllamaJsonContext.Default.OllamaEmbedResponse);
        return result!.Embeddings;
    }
}

// Modelos de request/response — source-generated, AOT-safe
public record OllamaEmbedRequest(string Model, string Input);
public record OllamaEmbedBatchRequest(string Model, string[] Input);
public record OllamaEmbedResponse(float[][] Embeddings);

[JsonSerializable(typeof(OllamaEmbedRequest))]
[JsonSerializable(typeof(OllamaEmbedBatchRequest))]
[JsonSerializable(typeof(OllamaEmbedResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext { }
```

### Esquema SQLite para Vectores

```sql
-- Tabla de embeddings persistidos
CREATE TABLE IF NOT EXISTS note_embeddings (
    note_path       TEXT PRIMARY KEY,
    note_hash       TEXT NOT NULL,        -- MD5 del contenido, para detectar cambios
    embedding       BLOB NOT NULL,        -- float[] serializado como REAL[]
    indexed_at      INTEGER NOT NULL,     -- Unix timestamp
    token_count     INTEGER              -- Longitud del texto en tokens
);

CREATE INDEX IF NOT EXISTS idx_note_embeddings_hash
    ON note_embeddings(note_hash);
```

### Búsqueda Híbrida (Texto + Semántica)

El endpoint `search_notes` en v2 combina ambos tipos de búsqueda:

```
query: "notas sobre redes LSTM con backpropagation"
  │
  ├─► Búsqueda por texto     → resultados exactos con "LSTM", "backpropagation"
  │   (índice invertido)       puntuación por frecuencia de términos (TF-IDF simple)
  │
  └─► Búsqueda semántica     → resultados conceptualmente similares
      (embeddings + cosine)    "redes neuronales recurrentes", "gradient descent"
  │
  ▼
RankFusion (Reciprocal Rank Fusion)
  │
  ▼
Top-K resultados unificados, ordenados por relevancia
```

---

## 3. Soporte de Assets No-Markdown

En v2 se añade indexación y referenciabilidad de activos de la bóveda:

### Tipos de Assets Soportados

| Tipo | Extensión | Indexado | Referenciable por AI |
|---|---|---|---|
| Notas Markdown | `.md` | ✅ Completo | ✅ Contenido + metadatos |
| Diagramas Excalidraw | `.excalidraw` | ✅ Parcial | ✅ Nombre, tags, fecha |
| Imágenes | `.png`, `.jpg`, `.svg` | ✅ Metadatos | ⚠️ Solo nombre y ubicación |
| Bases de datos Obsidian | `.csv` (DB Folder) | ✅ Parcial | ✅ Esquema y filas |
| Canvas | `.canvas` | ✅ Parcial | ✅ Nodos y conexiones |

> En una fase futura (v3+), las imágenes podrían indexarse con un modelo de visión local.

### Estructura del Índice v2

```
VaultIndex (en memoria)
├── notes: Dictionary<string, Note>               ← .md files
├── excalidraw: Dictionary<string, ExcalidrawMeta>  ← .excalidraw files
├── images: Dictionary<string, ImageMeta>          ← images
├── databases: Dictionary<string, DatabaseMeta>   ← DB Folder tables
└── canvas: Dictionary<string, CanvasMeta>         ← .canvas files
```

---

## 4. Comandos Nuevos en v2

Los siguientes comandos se añaden en v2 (detalle completo en [`commands-reference.md`](./commands-reference.md)):

### Comandos de Búsqueda Semántica

| Comando MCP Tool | Descripción |
|---|---|
| `semantic_search_notes` | Búsqueda por similitud conceptual (embeddings) |
| `hybrid_search_notes` | Búsqueda combinada texto + semántica (RRF) |
| `find_similar_notes` | Encuentra notas similares a una nota dada |
| `get_note_embedding` | Devuelve el embedding de una nota (para debugging) |

### Comandos de Organización Avanzada

| Comando MCP Tool | Descripción |
|---|---|
| `reorder_notes_in_folder` | Renombra notas para reordenar por prefijo numérico |
| `normalize_tags` | Estandariza tags de una nota o toda la bóveda |
| `reclassify_note` | Mueve una nota a la carpeta más apropiada según su contenido |
| `suggest_tags` | Sugiere tags para una nota basándose en el contenido (IA local) |

### Comandos de Assets

| Comando MCP Tool | Descripción |
|---|---|
| `list_excalidraw_files` | Lista todos los diagramas Excalidraw de la bóveda |
| `get_asset_metadata` | Metadatos de cualquier asset (imagen, diagrama, base de datos) |
| `find_orphan_assets` | Encuentra assets no referenciados por ninguna nota |
| `find_broken_links` | Encuentra wikilinks rotos en toda la bóveda |

---

## 5. Dependencias Adicionales en v2

| Paquete NuGet | Propósito | AOT Compat. |
|---|---|---|
| `ModelContextProtocol.AspNetCore` | Transporte HTTP-SSE | ✅ |
| `Microsoft.Data.Sqlite` | Persistencia de vectores y caché | ✅ |
| `System.Numerics.Tensors` | Operaciones vectoriales (cosine similarity) | ✅ .NET 9+ |

**Dependencia de sistema (no NuGet):**

| Servicio | Instalación | Propósito |
|---|---|---|
| **Ollama** | `winget install Ollama.Ollama` / `curl ollama.com/install.sh` | Motor de embeddings local |
| **`nomic-embed-text`** | `ollama pull nomic-embed-text` | Modelo de embeddings 768d |

---

## 6. Plan de Migración v1 → v2

1. **Sin breaking changes:** El transporte stdio de v1 sigue funcionando en v2. La configuración del agente no cambia.
2. **Activar HTTP-SSE:** Cambiar `WithStdioServerTransport()` por `WithHttpTransport()` en `Program.cs`, o mantener ambos (bifurcación por argumento de CLI).
3. **Migración del índice:** La primera vez que v2 arranque, genera los embeddings para todas las notas y los persiste en SQLite. Las sesiones siguientes solo actualizan las notas modificadas.
4. **SQLite no es requerido en v1:** El archivo de base de datos (`kioku.db`) solo se crea cuando v2 está activo.

---

## 7. Métricas de Rendimiento Esperadas (v2)

| Operación | Estimado (Intel i7, CPU) |
|---|---|
| Arranque del servidor HTTP | < 500ms |
| Indexación inicial (500 notas, con Ollama) | ~30 segundos |
| Re-indexación (1 nota modificada) | ~60ms |
| Búsqueda por texto (solo-texto, índice en memoria) | < 5ms |
| Búsqueda semántica (cosine sobre 500 vectores en SQLite) | < 20ms |
| Búsqueda híbrida (texto + semántica + RRF) | < 30ms |
| RAM total del servidor en reposo (sin Ollama) | < 80MB |
| RAM total del servidor + Ollama corriendo | ~330MB (Ollama usa ~250MB con modelo cargado) |

> **Nota:** Ollama solo carga el modelo en RAM cuando recibe una solicitud. Si no hay solicitudes durante un tiempo, descarga el modelo automáticamente. El servidor Kioku en sí mismo usa menos de 80MB.
