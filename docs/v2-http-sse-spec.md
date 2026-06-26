# Especificaciones v2: Transporte HTTP-SSE

> **Estado:** ✅ Completado — Transporte HTTP-SSE, autenticación, búsqueda híbrida y despliegue en VM implementados.
> **Referencia:** [planning.md](./planning.md) · [commands-reference.md](./commands-reference.md) · [auth-options.md](./deploy/auth-options.md)

---

## Estado actual vs v2

### Lo que ya está implementado (v1 — `feat/initial-setup`)

| Componente | Estado | Notas |
|---|---|---|
| Transporte stdio (MCP) | ✅ Implementado | `WithStdioServerTransport()` |
| VaultIndexService (keyword search) | ✅ Implementado | Índice invertido en memoria |
| FileSystemWatcher + debounce | ✅ Implementado | 500ms debounce |
| Dot-directory exclusion | ✅ Implementado | `.obsidian`, `.trash`, `.agents` excluidos |
| EmbeddingService (Ollama) | ✅ Implementado | `nomic-embed-text`, 768-dim |
| EmbeddingPersistence (binary cache) | ✅ Implementado | `vault/.kioku/embeddings.bin`, formato v2 |
| `search_notes_semantic` | ✅ Implementado | Con `min_score`, snippets, frontmatter en embedding |
| Frontmatter en embeddings | ✅ Implementado | Tags, status, type, date, ExtraFields |
| KiokuLogger / Logger TypeScript | ✅ Implementado | Sin emojis, extensiones ILogger<T> |
| 18 MCP tools | ✅ Implementado | Query + Command + Bridge + Utility |

### Lo que ya está implementado (v2 completo)

| Componente | Estado | Notas |
|---|---|---|
| Transporte HTTP-SSE | ✅ Implementado | `WithHttpTransport()` en `Program.cs` |
| Bearer Token auth (API Key) | ✅ Implementado | `Middleware/ApiKeyMiddleware.cs` |
| nginx reverse proxy config | ✅ Implementado | `docs/deploy/nginx.conf` |
| systemd service | ✅ Implementado | `docs/deploy/kioku.service` |
| Búsqueda híbrida (keyword + semántica) | ✅ Implementado | `HybridSearchService` con RRF |
| `find_similar_notes` (por nota) | ✅ Implementado | En `NoteQueryTools` |
| Comandos avanzados (`normalize_tags`, `suggest_tags`) | ✅ Implementado | En `VaultOrganizationTools` |
| Soporte assets (Excalidraw, imágenes) | ✅ Implementado | En `AssetTools` |

---

## 1. Transporte HTTP-SSE (Streamable HTTP)

### Motivación

El transporte stdio de v1 limita a un único agente de IA conectado a la vez.
HTTP-SSE permite:
- Múltiples agentes simultáneos (Claude Code + CI + móvil).
- Servidor persistente en VM sin depender del ciclo de vida del agente.
- Compatible con proxies, firewalls y herramientas de debug estándar.

### Arquitectura de Transporte Dual

```
[Claude Code (laptop)]
        │ stdio (v1 — siempre disponible, arranque bajo demanda)
        ▼
┌──────────────────────────────────────────────────────────┐
│                  Kioku.Mcp.Server v2                     │
│                                                          │
│  ┌─────────────────────┐   ┌──────────────────────────┐  │
│  │  Stdio Transport    │   │  HTTP-SSE Transport       │  │
│  │  (v1, siempre ON)   │   │  (v2, :5173 configurable) │  │
│  └─────────────────────┘   └──────────────────────────┘  │
└───────────────────────────────────┬──────────────────────┘
                                    │ HTTP POST / GET (SSE)
                 ┌──────────────────┴──────────────────┐
                 │   Agente B / CI / móvil              │
                 └─────────────────────────────────────┘
```

### Implementación en `Program.cs`

```csharp
// v2: arranque condicional según arg de CLI o env var
var useHttp = args.Contains("--http")
    || Environment.GetEnvironmentVariable("KIOKU_TRANSPORT") == "http";

if (useHttp)
{
    // Transporte HTTP-SSE (v2)
    var webBuilder = WebApplication.CreateBuilder(args);

    webBuilder.Services.AddSingleton(config);
    webBuilder.Services.AddSingleton<EmbeddingService>();
    webBuilder.Services.AddSingleton<VaultIndexService>();
    webBuilder.Services.AddSingleton<ObsidianBridgeService>();

    webBuilder.Services.AddCors(options =>
        options.AddDefaultPolicy(p =>
            p.WithOrigins("http://localhost", "app://obsidian.md")
             .AllowAnyHeader().AllowAnyMethod()));

    webBuilder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<NoteQueryTools>()
        .WithTools<NoteCommandTools>()
        .WithTools<ObsidianBridgeTools>()
        .WithTools<UtilityTools>();

    var webApp = webBuilder.Build();
    webApp.UseCors();
    webApp.UseMiddleware<ApiKeyMiddleware>();   // ver sección Auth
    webApp.MapGet("/health", () => Results.Ok(new { status = "ok", transport = "http" }));
    webApp.MapMcp("/mcp");

    var vaultIndex = webApp.Services.GetRequiredService<VaultIndexService>();
    await vaultIndex.InitializeAsync();
    await webApp.RunAsync($"http://localhost:{config.HttpPort}");
}
else
{
    // Transporte stdio (v1 — sin cambios)
    // ... código actual de Program.cs
}
```

### Nueva env var y configuración

```csharp
// KiokuConfiguration.cs — añadir:
public string? ApiKey { get; init; }       // KIOKU_API_KEY
public int HttpPort { get; init; } = 5173; // KIOKU_HTTP_PORT
public string Transport { get; init; } = "stdio"; // KIOKU_TRANSPORT: "stdio" | "http"
```

| Variable | Requerida | Default | Descripción |
|---|---|---|---|
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` o `http` |
| `KIOKU_HTTP_PORT` | no | `5173` | Puerto del servidor HTTP |
| `KIOKU_API_KEY` | no* | — | Bearer token para auth (*requerido si `transport=http` y servidor público) |

### Configuración del cliente MCP

```json
// .mcp.json — versión HTTP (v2)
{
  "servers": {
    "kioku": {
      "type": "http",
      "url": "http://100.x.x.x:5173/mcp",
      "headers": {
        "Authorization": "Bearer <KIOKU_API_KEY>"
      }
    }
  }
}
```

```json
// .mcp.json — versión stdio (v1, sigue funcionando sin cambios)
{
  "servers": {
    "kioku": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "./src/Kioku.Mcp.Server"],
      "env": {
        "KIOKU_VAULT_PATH": "/path/to/vault"
      }
    }
  }
}
```

---

## 2. Autenticación (Bearer Token)

Ver análisis completo en [`auth-options.md`](./deploy/auth-options.md).

### Middleware de API Key

```csharp
// Middleware/ApiKeyMiddleware.cs
public sealed class ApiKeyMiddleware(RequestDelegate next, KiokuConfiguration config)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Sin clave configurada: sin protección (solo para desarrollo local)
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            await next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var header)
            || !header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || header.ToString()["Bearer ".Length..].Trim() != config.ApiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("[error] Unauthorized — provide Authorization: Bearer <KIOKU_API_KEY>");
            return;
        }

        await next(context);
    }
}
```

**Generar token:**
```bash
openssl rand -hex 32
```

---

## 3. Despliegue en VM

Ver guía completa en [`auth-options.md`](./deploy/auth-options.md). Resumen:

### systemd service

```ini
# /etc/systemd/system/kioku.service
[Unit]
Description=Kioku MCP Server (HTTP)
After=network.target ollama.service

[Service]
Type=simple
User=kioku
WorkingDirectory=/opt/kioku
ExecStart=/opt/kioku/Kioku.Mcp.Server --http
Environment=KIOKU_VAULT_PATH=/vault/cortex
Environment=KIOKU_API_KEY=<token-generado-con-openssl>
Environment=KIOKU_OLLAMA_URL=http://localhost:11434
Environment=KIOKU_TRANSPORT=http
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now kioku
```

### nginx reverse proxy (HTTPS con Tailscale)

```nginx
# /etc/nginx/sites-available/kioku
server {
    listen 443 ssl;
    server_name kioku.internal; # o IP de Tailscale

    ssl_certificate     /etc/ssl/kioku.crt;
    ssl_certificate_key /etc/ssl/kioku.key;

    location / {
        proxy_pass http://localhost:5173;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        # SSE: desactivar buffering
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 3600s;
    }
}
```

---

## 4. Búsqueda Híbrida (futuro — v2.1)

Combinar `search_notes` (keyword) + `search_notes_semantic` (embeddings) con Reciprocal Rank Fusion:

```
query: "tickets de atena resueltos en enero"
  │
  ├─► Keyword search (índice invertido)  → notas con "tickets", "atena", "enero"
  │
  └─► Semantic search (embeddings)       → notas conceptualmente relacionadas
  │
  ▼
RRF: score = Σ 1 / (k + rank_i)    (k=60 es el valor estándar)
  │
  ▼
Top-K unificados, sin duplicados
```

Nuevo tool: `search_notes_hybrid(query, max_results, min_score, keyword_weight, semantic_weight)`

---

## 5. Dependencias NuGet para v2

| Paquete | Propósito | Estado |
|---|---|---|
| `ModelContextProtocol` | SDK MCP (stdio) | ✅ En uso |
| `ModelContextProtocol.AspNetCore` | Transporte HTTP-SSE | ✅ En uso |

> **Nota:** El caché de embeddings usa formato binario propio (`embeddings.bin`), no SQLite.
> Esto reduce dependencias y es más rápido para lectura secuencial de 5000 vectores (~15MB).

---

## 6. Commits de Referencia

Los siguientes commits implementaron v2 en `develop`:
- `feat(server): add HTTP-SSE transport with dual-mode startup`
- `feat(server): add API key authentication middleware`
- `docs: add systemd service and nginx config examples`
- `feat(server): add search_notes_hybrid tool with RRF`

---

## 7. Métricas Esperadas

| Operación | Local (RTX 5060) | VM CPU-only |
|---|---|---|
| Arranque servidor HTTP | < 500ms | < 800ms |
| Carga cache embeddings (5000 notas) | < 100ms | < 200ms |
| Re-indexado incremental (1 nota) | ~60ms | ~2-5s |
| Keyword search | < 5ms | < 5ms |
| Semantic search (cosine, 5000 vectores) | < 10ms | < 15ms |
| Embedding query (Ollama) | ~60ms | ~2-5s (CPU) |
