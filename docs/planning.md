# Master Architecture Plan: Kioku MCP Ecosystem

> **Last revised:** 2026-07-02 — v2 complete, v3 in production. This document reflects the current state of the architecture after implementing the 17 tool classes (102 MCP tools) and the HTTP-SSE transport. Specs for upcoming features live in [`docs/features/`](./features/README.md) and the work breakdown in [`docs/tasks/`](./tasks/README.md).

This document describes the design strategy, technology selection, and key concepts for building a high-performance note access ecosystem, optimized for consumption by AI agents like Claude Code and Antigravity CLI in cross-platform environments (Windows 11 / Fedora 43).

---

## 1. Technology and Language Recommendation

To achieve maximum performance, efficiency, and maintainability in this hybrid system, a **"Dual Component"** architecture is proposed, using the most recent versions of the technology stack:

### A. For the MCP Server (Processing Engine): C# (.NET 10) — Self-Contained

- **Why .NET 10?:** .NET 10 introduces substantial performance improvements, official support for the MCP SDK (`Microsoft.McpServer.ProjectTemplates`), and **Self-Contained** mode, which publishes a portable executable without requiring .NET to be installed on the target machine — full portability without the restrictions of Native AOT. (.NET 10 already installed on Fedora 43 ✅)

- **Official Microsoft template:** The `Microsoft.McpServer.ProjectTemplates` template exists (preview for .NET 10) and generates the complete MCP server skeleton with stdio/HTTP transport support and automatic AOT configuration:
  ```bash
  dotnet new install Microsoft.McpServer.ProjectTemplates
  dotnet new mcpserver -n Kioku.Mcp.Server
  ```

- **Chosen build model — Self-Contained:** Publishes a single executable that includes the .NET runtime. It doesn't require .NET to be installed on the machine where it runs. Startup ~200ms — perfectly acceptable for a local on-demand tool. Native AOT is evaluated as an optional optimization in v3.

- **Portability:** A single source code base, two builds — `linux-x64` for Fedora 43 and `win-x64` for Windows 11.

- **On-demand startup:** The server starts only when an AI agent invokes it. It requires no system service or auto-start.

### B. For the Obsidian Plugin (Interface Bridge): TypeScript (Native JS)

- **Why?:** Obsidian is built on Electron (Chromium + Node.js), so **TypeScript** is the only natively supported language for interacting with its internal API.

- **Publishing standards:** The plugin will follow the **official standards of the Obsidian Community Plugin Store** from the start, allowing future publication without refactoring.

- **Plugin role:** It will be kept to an absolute minimum weight (_Thin Client_). It won't perform heavy processing or file indexing; it will only act as a receiver of visual commands that communicates with the C# server.

---

## 2. Vault Context

| Characteristic | Detail |
|---|---|
| **Markdown notes** | ~500 `.md` files |
| **Visual assets** | Images, Excalidraw diagrams (`.excalidraw`) |
| **Databases** | Native Obsidian tables (Dataview/DB Folder) |
| **Search required** | Plain text **+** Semantic (vectors) |
| **Audience** | Personal, with potential for community publication |

---

## 2.1 Development Environment

Both environments (Fedora 43 and Windows 11) are operational: .NET 10 SDK, Ollama with `nomic-embed-text` (768-dim) on `localhost:11434`, Obsidian, and the `kioku` monorepo. Installation prerequisites for end users are documented in [`docs/install.md`](./install.md).

---

## 3. Project Structure (Kioku Monorepo)

Although these are two projects with completely different technologies, they are organized into a single Git repository called `kioku` to simplify deployment, version control, and local testing.

### Folder Structure

```
kioku/                              ← Repository root folder (Monorepo)
├── .git/
├── README.md
├── AGENTS.md                       ← Context for AI agents (Kioku itself)
├── .gitignore
├── docs/
│   ├── planning.md                 ← This file
│   ├── commands-reference.md       ← Command inventory (MCP Tools + Plugin)
│   ├── v2-http-sse-spec.md         ← HTTP-SSE specs for v2
│   └── deploy/
│       ├── auth-options.md         ← Authentication options for deployment
│       ├── kioku.service           ← systemd unit for VM
│       └── nginx.conf              ← nginx reverse proxy
└── src/
    ├── Kioku.Mcp.Server/           ← C# project (.NET 10)
    │   ├── Kioku.Mcp.Server.csproj
    │   ├── Program.cs              ← Entry point (stdio and HTTP-SSE)
    │   ├── KiokuConfiguration.cs   ← Environment variables
    │   ├── Middleware/
    │   │   └── ApiKeyMiddleware.cs ← Bearer token auth
    │   ├── Tools/                  ← 17 registered tool classes
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
    │   ├── Services/               ← Internal logic (not exposed as MCP tools)
    │   │   ├── VaultIndexService.cs   ← FileSystemWatcher + inverted index
    │   │   ├── EmbeddingService.cs    ← Ollama embeddings via HTTP
    │   │   ├── EmbeddingPersistence.cs ← Binary cache (.kioku/embeddings.bin, format v3)
    │   │   ├── HybridSearchService.cs ← Combined search (keyword + semantic)
    │   │   ├── TaskService.cs         ← Native checkbox parsing
    │   │   ├── ObsidianBridgeService.cs ← WebSocket client to the plugin
    │   │   ├── VaultConfigService.cs  ← .kioku/config.yml + capability groups
    │   │   ├── FrontmatterParser.cs   ← Manual YAML parser (Span<char>)
    │   │   ├── MarkdownTextExtractor.cs ← Markdown → plain text + wikilinks
    │   │   ├── MetricsService.cs      ← Tool counters (opt-in)
    │   │   └── FolderRanker.cs        ← Folder ranking (suggest_folder)
    │   └── Domain/
    │       ├── Note.cs
    │       ├── NoteMetadata.cs
    │       ├── SearchResult.cs
    │       ├── TaskItem.cs
    │       ├── KiokuError.cs
    │       └── EmbeddingModelRegistry.cs
    │
    └── obsidian-kioku-mcp/         ← TypeScript project (Obsidian Plugin)
        ├── package.json
        ├── tsconfig.json
        ├── manifest.json           ← Plugin metadata for Obsidian
        ├── styles.css
        └── src/
            ├── main.ts             ← Entry point (KiokuPlugin + settings tab)
            ├── bridge.ts           ← Local WebSocket server (BridgeServer)
            ├── handlers.ts         ← 22 bridge commands
            ├── types.ts            ← Shared protocol (PROTOCOL_VERSION)
            ├── logger.ts           ← Typed logger
            └── protocol-schema.json ← JSON-Schema of the wire format
```

---

## 4. System Architecture (IPC Bridge Strategy)

The system operates under a decentralized local communication model:

```
┌───────────────────────────────────────────────────────────────┐
│              AI AGENT (Claude Code / agy)                     │
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
│      Obsidian Plugin (TypeScript — Thin Client)                │
│         Local WebSocket Server (KIOKU_OBSIDIAN_PORT)           │
└───────────────────────────┬───────────────────────────────────┘
                            │ Obsidian Plugin API
                            ▼
┌───────────────────────────────────────────────────────────────┐
│                    Obsidian App                               │
│            (Electron / Chromium + Node.js)                    │
└───────────────────────────────────────────────────────────────┘
```

- **Full decoupling:** If Obsidian is closed, the AI agent can still search, read, and write notes because the C# engine processes Markdown files directly on disk.

- **Live synchronization:** If Obsidian is open, the C# engine sends notifications via WebSockets to the plugin to immediately reflect visual changes on screen.

- **On-demand startup:** The server doesn't run as a system service. It starts when the AI agent invokes it via stdio and terminates when the agent's session ends.

---

## 5. Key Points for the MCP Server (Kioku.Mcp.Server)

### Low-Cost Processing (Zero-Allocation) with .NET 10

- Use `System.Text.Json` optimizations to read and write the MCP protocol's JSON-RPC messages directly over memory buffers (`Span<T>` / `ReadOnlySpan<T>`), avoiding unnecessary string instantiation on the heap.

- To parse YAML frontmatter: prefer manual parsing with `Span<char>` over libraries that use reflection internally (incompatible with AOT).

### Serialization Code Generators (AOT Safe)

- Since Native AOT disables dynamic reflection, `JsonSerializableAttribute` and `JsonSourceGenerationOptions` must be used to generate serializers at compile time.

- **Don't use MediatR** (it uses reflection internally). Use the native `[McpServerToolType]` + `[McpServerTool]` pattern from the official SDK.

### Tools Pattern (Simplified CQRS with MCP SDK)

- **Queries** → `NoteQueryTools.cs`: search, read, list, filter notes
- **Commands** → `NoteCommandTools.cs`: create, update, append, reorder
- **Bridge** → `ObsidianBridgeTools.cs`: interaction with the Obsidian UI

### Real-Time Indexer with Debouncing

```csharp
watcher.Filter = "*.md";                          // Only Markdown files
watcher.NotifyFilter = NotifyFilters.LastWrite
                     | NotifyFilters.FileName;
watcher.IncludeSubdirectories = true;
watcher.InternalBufferSize = 65536;               // 64KB max recommended
watcher.EnableRaisingEvents = true;
// On Fedora if the FS doesn't notify: DOTNET_USE_POLLING_FILE_WATCHER=1
```

> **Note:** FileSystemWatcher can miss events if the buffer overflows on active vaults. Implement ~500ms debouncing to group bursts of changes.

### Dual Search (Text + Semantic)

For a vault of ~500 notes with images and Excalidraw:

| Phase | Type | Technology | Status |
|---|---|---|---|
| v1 | Plain text | In-memory inverted index (`Dictionary<string, HashSet<string>>`) | ✅ Implemented (`VaultIndexService`) |
| v1 | Plain text | Manual parser with `Span<char>` for YAML frontmatter | ✅ Implemented (`FrontmatterParser`) |
| v2 | Semantic | **Ollama** (`nomic-embed-text`) via HTTP `localhost:11434` | ✅ Implemented (`EmbeddingService`) |
| v2 | Semantic | Embeddings persisted in binary cache `.kioku/embeddings.bin` (format v3) | ✅ Implemented (SQLite discarded — `EmbeddingPersistence`) |

**How to use Ollama for embeddings from C#:**
```bash
# Verify the model is available
ollama list
# Should show: nomic-embed-text

# Manual endpoint test (from terminal)
curl http://localhost:11434/api/embed -d '{"model":"nomic-embed-text","input":"hola mundo"}'
# Responds: {"embeddings": [[0.123, -0.456, ...]]}
```

---

## 6. Key Points for the Obsidian Plugin (obsidian-kioku-mcp)

### Community Store Publishing Standards

The plugin will follow Obsidian's official guidelines for community plugins from the start:
- `manifest.json` with `minAppVersion`, `version`, `author`, `authorUrl`
- No external dependencies not approved by the store
- Proper lifecycle handling (`onload` / `onunload`)
- No access to undocumented Obsidian APIs

### No Interference with the Main Thread

All network communication (WebSockets) must be asynchronous and non-blocking. Obsidian must not freeze or drop FPS on high refresh-rate displays (144Hz / 165Hz) while the agent performs searches.

### Plugin Commands

See [`docs/commands-reference.md`](./commands-reference.md) for the full inventory of plugin and MCP server commands.

---

## 7. Versions and Roadmap

### v1 — MVP (Stdio Transport) ✅ COMPLETE

**Goal:** Functional MCP server that the AI agent can use without Obsidian.

- 11 core tools (read, write, utilities)
- TypeScript plugin with WebSocket bridge
- FileSystemWatcher + in-memory inverted index

### v2 — HTTP-SSE + Semantic Search ✅ COMPLETE

See full specs in [`docs/v2-http-sse-spec.md`](./v2-http-sse-spec.md).

**Summary:**
- HTTP-SSE transport in addition to stdio (multiple simultaneous agents)
- Semantic search with Ollama (`nomic-embed-text`, 768-dim)
- Persistent binary cache in `vault/.kioku/embeddings.bin`
- Bearer Token auth (ApiKeyMiddleware)
- Hybrid search (keyword + semantic with RRF)
- VM deployment with systemd + nginx

### v3 — Ecosystem Tools ✅ COMPLETE

**102 tools implemented across 17 tool classes:**

| Category | Tools |
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

Classes outside the core are enabled by capability groups in `.kioku/config.yml`
(see [`docs/vault-config.md`](./vault-config.md)). Full inventory in
[`docs/commands-reference.md`](./commands-reference.md).

### v4 — Future (Proposed)

Detailed specs for the next wave of features live in [`docs/features/`](./features/README.md),
with their prioritized work breakdown in [`docs/tasks/`](./tasks/README.md). Main lines of work:

- Local generation with Ollama (`KIOKU_GEN_MODEL`) — enabler for digest, flashcards, and synthesis
- Link suggestions, smart inbox, and daily digest (strengthen the graph)
- MCP Prompts & Resources (packaged workflows for any MCP client)
- WebSocket bridge authentication and status UI in the plugin
- Zotero/BibTeX, flashcards/Anki, incremental re-embedding
- Native AOT optimization for faster startup
- Publication on the Obsidian Community Plugin Store
- Real-time change streaming (SSE server-sent events)

---

## 8. NuGet Dependencies — Final Decisions

### Build Decision Context

The Kioku server is **not a cloud microservice**. It's a local desktop tool that:
- Starts on demand when invoked by the AI agent.
- Runs exclusively on the developer's machines (Windows 11 + Fedora 43).
- Has .NET 10 installed in both environments.

For this reason, the optimal build strategy is **Self-Contained** (not strict AOT), at least for v1 and v2:

| Model | Startup | RAM | Restrictions | Ideal use |
|---|---|---|---|---|
| **Self-Contained** ✅ | ~200ms | Normal | None | v1, v2 — local tool |
| Native AOT | ~50ms | Very low | No reflection, no ONNX | v3 — optional if startup matters |
| Framework-Dependent | ~150ms | Normal | Requires .NET installed | Development only |

> **Decision:** Use **Self-Contained** for v1 and v2. Evaluate Native AOT in v3 only if startup time is a measurable problem.
> Self-Contained publishes an executable that does NOT require .NET installed on the target machine — portability without AOT restrictions.

---

### NuGet Dependencies — Current Status

| Package | Purpose | Status |
|---|---|---|
| `ModelContextProtocol` | Official MCP SDK (stdio) | ✅ In use |
| `ModelContextProtocol.AspNetCore` | HTTP-SSE transport (v2) | ✅ In use |
| `System.Numerics.Tensors` | Cosine similarity (vectors) | ✅ In use |
| `YamlDotNet` | YAML parsing | ✅ In use (VaultConfigService for config.yml) |
| `Markdig` | Markdown parsing/rendering | ✅ In use (HTML export, text extraction) |
| `Microsoft.ML.OnnxRuntime` | Local ONNX embeddings | ❌ Replaced by Ollama |
| `OllamaSharp` _(optional)_ | Typed Ollama client | ❌ Native `HttpClient` is sufficient |

---

### Implemented Alternatives (Verified in Production)

#### ✅ YAML → Manual Parser for frontmatter + YamlDotNet for config

Obsidian's frontmatter is **extremely predictable**. It always follows the format:
```
---
key: value
tags: [tag1, tag2]
date: 2024-01-15
---
```
A full library isn't needed for frontmatter. A 100-150 line parser with `ReadOnlySpan<char>` is sufficient, faster, and fully AOT-safe:

```csharp
// FrontmatterParser.cs — Zero-allocation, no external dependencies
public static class FrontmatterParser
{
    public static NoteMetadata Parse(ReadOnlySpan<char> content)
    {
        if (!content.StartsWith("---")) return NoteMetadata.Empty;
        // Iterate line by line with MemoryExtensions.Split()
        // without creating intermediate strings on the heap
    }
}
```

**Note:** `YamlDotNet` was added later for `VaultConfigService` (config.yml),
which requires more complex YAML parsing. The manual parser is still used for frontmatter.

#### ✅ Markdig — Text Extractor + HTML Rendering

For search indexing, we only need **clean text** (without Markdown syntax). `MarkdownTextExtractor` removes:
- `#` from headings
- `**bold**`, `_italic_` emphasis
- `[[wikilinks]]` and `[text](url)` links
- ` ```code``` ` code blocks
- YAML frontmatter blocks

`Markdig` is also used for full HTML rendering (note export, research tools).

#### ✅ ONNX Runtime → Ollama (Embeddings via local HTTP)

`Microsoft.ML.OnnxRuntime` is a native wrapper over a C++ DLL. **It is not compatible with Native AOT** and is complex to distribute.

**Solution: Ollama** — a local AI model service that exposes an OpenAI-compatible HTTP API:

```csharp
// EmbeddingService.cs — AOT-safe: only HttpClient + System.Text.Json
public class OllamaEmbeddingService
{
    private readonly HttpClient _http;
    private const string Model = "nomic-embed-text"; // 274MB, Apache 2.0

    // POST http://localhost:11434/api/embed
    // { "model": "nomic-embed-text", "input": "text to vectorize" }
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        var request = new { model = Model, input = text };
        var response = await _http.PostAsJsonAsync("/api/embed", request);
        // Responds: { "embeddings": [[0.1, 0.2, ...]] } — L2-normalized vectors
        var result = await response.Content
            .ReadFromJsonAsync<OllamaEmbedResponse>(OllamaJsonContext.Default.OllamaEmbedResponse);
        return result!.Embeddings[0];
    }
}

// Source-generated JSON context — AOT-safe
[JsonSerializable(typeof(OllamaEmbedResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext { }
```

**Advantages of Ollama over ONNX Runtime:**

| Characteristic | ONNX Runtime | Ollama |
|---|---|---|
| AOT compatible | ❌ | ✅ (only HTTP calls) |
| Requires GPU | Optional | No |
| Privacy | Local | Local |
| Available models | Any ONNX | Llama, Mistral, nomic-embed-text, etc. |
| Model updates | Recompile | `ollama pull <model>` |
| Overhead when idle | 0 (doesn't run) | ~50MB RAM (background service) |
| Speed (500 notes, i7 CPU) | ~50ms/note | ~60ms/note |

> **Prerequisite for v2:** The user must have **Ollama installed** (`winget install Ollama.Ollama` on Windows, `flatpak install ollama` on Fedora).
> The Kioku server must check whether Ollama is available on startup for v2 and gracefully degrade to text-only if it isn't.

---

### Revised Golden Rule

> For v1/v2 (Self-Contained): avoid libraries with reflection **not because it breaks the build**, but because:
> 1. They increase the size of the executable.
> 2. They reduce runtime performance.
> 3. They complicate future migration to AOT in v3.
>
> For v3 (Native AOT): no dependency with dynamic reflection is allowed. Everything must use source generators or manual parsing.
