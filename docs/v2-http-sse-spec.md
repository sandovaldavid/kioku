# v2 Specifications: HTTP-SSE Transport

> **Status:** ✅ Complete — HTTP-SSE transport, authentication, hybrid search, and VM deployment implemented.
> **Reference:** [planning.md](./planning.md) · [commands-reference.md](./commands-reference.md) · [auth-options.md](./deploy/auth-options.md)

---

## Current status vs v2

### What's already implemented (v1 — `feat/initial-setup`)

| Component | Status | Notes |
|---|---|---|
| stdio transport (MCP) | ✅ Implemented | `WithStdioServerTransport()` |
| VaultIndexService (keyword search) | ✅ Implemented | In-memory inverted index |
| FileSystemWatcher + debounce | ✅ Implemented | 500ms debounce |
| Dot-directory exclusion | ✅ Implemented | `.obsidian`, `.trash`, `.agents` excluded |
| EmbeddingService (Ollama) | ✅ Implemented | `nomic-embed-text`, 768-dim |
| EmbeddingPersistence (binary cache) | ✅ Implemented | `vault/.kioku/embeddings.bin`, format v3 (with per-entry content hash) |
| `search_notes_semantic` | ✅ Implemented | With `min_score`, snippets, frontmatter in embedding |
| Frontmatter in embeddings | ✅ Implemented | Tags, status, type, date, ExtraFields |
| Incremental background re-embedding | ✅ Implemented ([P3-03](tasks/P3-03-incremental-reembedding.md)) | The backlog of new/changed notes is processed in the background with limited parallelism (2 concurrent requests to Ollama); startup never waits for it to finish — keyword search is available immediately. Progress visible in `get_index_status` (`embedding_backlog`, `embedded this session`, `embedding rate`, `estimated remaining`). |
| KiokuLogger / TypeScript Logger | ✅ Implemented | No emojis, ILogger<T> extensions |
| MCP tools | ✅ Implemented | Currently 102 tools across 17 classes — see [commands-reference.md](./commands-reference.md) |

### What's already implemented (v2 complete)

| Component | Status | Notes |
|---|---|---|
| HTTP-SSE transport | ✅ Implemented | `WithHttpTransport()` in `Program.cs` |
| Bearer Token auth (API Key) | ✅ Implemented | `Middleware/ApiKeyMiddleware.cs` |
| nginx reverse proxy config | ✅ Implemented | `docs/deploy/nginx.conf` |
| systemd service | ✅ Implemented | `docs/deploy/kioku.service` |
| Hybrid search (keyword + semantic) | ✅ Implemented | `HybridSearchService` with RRF |
| `find_similar_notes` (by note) | ✅ Implemented | In `NoteQueryTools` |
| Advanced commands (`normalize_tags`, `suggest_tags`) | ✅ Implemented | In `VaultOrganizationTools` |
| Asset support (Excalidraw, images) | ✅ Implemented | In `AssetTools` |

---

## 1. HTTP-SSE Transport (Streamable HTTP)

### Motivation

The v1 stdio transport limits to a single connected AI agent at a time.
HTTP-SSE enables:
- Multiple simultaneous agents (Claude Code + CI + mobile).
- Persistent server on a VM, independent of the agent's lifecycle.
- Compatible with proxies, firewalls, and standard debugging tools.

### Dual Transport Architecture

```
[Claude Code (laptop)]
        │ stdio (v1 — always available, on-demand startup)
        ▼
┌──────────────────────────────────────────────────────────┐
│                  Kioku.Mcp.Server v2                     │
│                                                          │
│  ┌─────────────────────┐   ┌──────────────────────────┐  │
│  │  Stdio Transport    │   │  HTTP-SSE Transport       │  │
│  │  (v1, always ON)    │   │  (v2, :5173 configurable) │  │
│  └─────────────────────┘   └──────────────────────────┘  │
└───────────────────────────────────┬──────────────────────┘
                                    │ HTTP POST / GET (SSE)
                 ┌──────────────────┴──────────────────┐
                 │   Agent B / CI / mobile              │
                 └─────────────────────────────────────┘
```

### Implementation in `Program.cs`

```csharp
// v2: conditional startup based on CLI arg or env var
var useHttp = args.Contains("--http")
    || Environment.GetEnvironmentVariable("KIOKU_TRANSPORT") == "http";

if (useHttp)
{
    // HTTP-SSE transport (v2)
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
    webApp.UseMiddleware<ApiKeyMiddleware>();   // see Auth section
    webApp.MapGet("/health", () => Results.Ok(new { status = "ok", transport = "http" }));
    webApp.MapMcp("/mcp");

    var vaultIndex = webApp.Services.GetRequiredService<VaultIndexService>();
    await vaultIndex.InitializeAsync();
    await webApp.RunAsync($"http://localhost:{config.HttpPort}");
}
else
{
    // stdio transport (v1 — unchanged)
    // ... current Program.cs code
}
```

> **Note:** The example above is illustrative. The actual `Program.cs` code registers the
> 17 tool classes via `ConfigureKiokuTools()`, filtering by the capability groups
> enabled in `.kioku/config.yml`.

### New Env Var and Configuration

```csharp
// KiokuConfiguration.cs — add:
public string? ApiKey { get; init; }       // KIOKU_API_KEY
public int HttpPort { get; init; } = 5173; // KIOKU_HTTP_PORT
public string Transport { get; init; } = "stdio"; // KIOKU_TRANSPORT: "stdio" | "http"
```

| Variable | Required | Default | Description |
|---|---|---|---|
| `KIOKU_TRANSPORT` | no | `stdio` | `stdio` or `http` |
| `KIOKU_HTTP_PORT` | no | `5173` | HTTP server port |
| `KIOKU_API_KEY` | no* | — | Bearer token for auth (*required if `transport=http` and the server is public) |

### MCP Client Configuration

> The root key depends on the client: **`"mcpServers"`** in Claude Code/Claude Desktop/Cursor
> (`.mcp.json`), **`"servers"`** in VS Code (`.vscode/mcp.json`). The examples below use
> the Claude Code format.

```json
// .mcp.json — HTTP version (v2)
{
  "mcpServers": {
    "kioku": {
      "type": "sse",
      "url": "http://100.x.x.x:5173/mcp",
      "headers": {
        "Authorization": "Bearer <KIOKU_API_KEY>"
      }
    }
  }
}
```

```json
// .mcp.json — stdio version (v1, still works unchanged)
{
  "mcpServers": {
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

## 2. Authentication (Bearer Token)

See the full analysis in [`auth-options.md`](./deploy/auth-options.md).

### API Key Middleware

```csharp
// Middleware/ApiKeyMiddleware.cs
public sealed class ApiKeyMiddleware(RequestDelegate next, KiokuConfiguration config)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // No key configured: no protection (local development only)
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

**Generate token:**
```bash
openssl rand -hex 32
```

---

## 3. VM Deployment

See the full guide in [`auth-options.md`](./deploy/auth-options.md). Summary:

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
Environment=KIOKU_API_KEY=<token-generated-with-openssl>
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

### nginx reverse proxy (HTTPS with Tailscale)

```nginx
# /etc/nginx/sites-available/kioku
server {
    listen 443 ssl;
    server_name kioku.internal; # or Tailscale IP

    ssl_certificate     /etc/ssl/kioku.crt;
    ssl_certificate_key /etc/ssl/kioku.key;

    location / {
        proxy_pass http://localhost:5173;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        # SSE: disable buffering
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 3600s;
    }
}
```

---

## 4. Hybrid Search ✅ Implemented

`search_notes_hybrid` combines `search_notes` (keyword) + `search_notes_semantic` (embeddings)
with Reciprocal Rank Fusion (`HybridSearchService`). If Ollama is unavailable, it degrades to
keyword-only:

```
query: "atena tickets resolved in january"
  │
  ├─► Keyword search (inverted index)  → notes with "tickets", "atena", "january"
  │
  └─► Semantic search (embeddings)       → conceptually related notes
  │
  ▼
RRF: score = Σ 1 / (k + rank_i)    (k=60 is the standard value)
  │
  ▼
Unified Top-K, no duplicates
```

Signature: `search_notes_hybrid(query, max_results, min_score, keyword_weight, semantic_weight)`

---

## 5. NuGet Dependencies for v2

| Package | Purpose | Status |
|---|---|---|
| `ModelContextProtocol` | MCP SDK (stdio) | ✅ In use |
| `ModelContextProtocol.AspNetCore` | HTTP-SSE transport | ✅ In use |

> **Note:** The embeddings cache uses a proprietary binary format (`embeddings.bin`), not SQLite.
> This reduces dependencies and is faster for sequential reads of 5000 vectors (~15MB).

---

## 6. Reference Commits

The following commits implemented v2 on `develop`:
- `feat(server): add HTTP-SSE transport with dual-mode startup`
- `feat(server): add API key authentication middleware`
- `docs: add systemd service and nginx config examples`
- `feat(server): add search_notes_hybrid tool with RRF`

---

## 7. Expected Metrics

| Operation | Local (RTX 5060) | CPU-only VM |
|---|---|---|
| HTTP server startup | < 500ms | < 800ms |
| Embeddings cache load (5000 notes) | < 100ms | < 200ms |
| Incremental re-indexing (1 note) | ~60ms | ~2-5s |
| Keyword search | < 5ms | < 5ms |
| Semantic search (cosine, 5000 vectors) | < 10ms | < 15ms |
| Embedding query (Ollama) | ~60ms | ~2-5s (CPU) |
