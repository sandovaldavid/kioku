# Kioku — MCP Server for Obsidian

> **Kioku** (記憶) significa "memoria" en japonés.
>
> Versión actual: **1.8.0-beta.8** (`develop` · beta) <!-- x-release-please-version --> · [Ver releases](https://github.com/sandovaldavid/kioku/releases)

Kioku es un servidor MCP (Model Context Protocol) que permite a agentes de IA como **Claude Code** y **Antigravity CLI** leer, buscar, escribir y organizar tu bóveda de Obsidian de manera nativa, rápida y privada — con 115 herramientas MCP en 18 clases y 22 comandos del bridge del plugin.

---

## Capacidades

- **Búsqueda híbrida** — full-text + semántica (embeddings con Ollama) + RRF
- **Lectura/escritura** de notas, frontmatter y metadatos
- **Gestión de tags** y organización taxonómica
- **Navegación de wikilinks** — backlinks, enlaces salientes, grafo de conocimiento
- **Gestión de tareas** — checkboxes nativos con filtros por tag, fecha, vencimiento
- **Zettelkasten** — creación atómica, MOCs, templates, literatura
- **CSS Theming** — snippets y temas completos desde el agente
- **Assets** — Excalidraw, imágenes, archivos huérfanos
- **Bridge con Obsidian** — abrir notas, ejecutar comandos, consultar estado (opcional)
- **Inicio bajo demanda** — no consume recursos cuando no se usa
- **Transporte dual** — stdio (v1, local) y HTTP-SSE (v2, múltiples agentes/VM)

## Arquitectura

```
Agente de IA (Claude Code / agy)
    │
    ├── stdio (v1 — local, bajo demanda)
    └── HTTP-SSE (v2 — VM, múltiples agentes)
    │
    ▼
Kioku.Mcp.Server (C# .NET 10)
    ├── 18 Tool Classes (115 herramientas MCP)
    ├── Services: VaultIndex · Embedding(Ollama) · HybridSearch
    │            TaskService · ObsidianBridge · Persistence
    └── Middleware: ApiKeyMiddleware
    │
    │ WebSocket (opcional, solo si Obsidian está abierto)
    ▼
Plugin Obsidian (TypeScript) — WebSocket Server :7765
    │
    ▼
Obsidian App
```

## Inicio Rápido (Uso Local)

### Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Obsidian](https://obsidian.md) instalado con tu bóveda de notas
- [Ollama](https://ollama.com) (opcional, necesario para búsqueda semántica con `nomic-embed-text`)

### 1. Compilación del Servidor

Para el mejor rendimiento, se recomienda compilar Kioku como un **único archivo ejecutable autónomo (Self-Contained)**. Así no dependerás de la ejecución mediante el SDK de dotnet.

Ejecuta el comando correspondiente a tu sistema operativo desde la raíz del proyecto:

* **Linux:**
  ```bash
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
  ```
* **Windows (PowerShell):**
  ```powershell
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
  ```
* **macOS (Intel):**
  ```bash
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
  ```
* **macOS (Apple Silicon):**
  ```bash
  dotnet publish src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
  ```

Esto generará el binario ejecutable `Kioku.Mcp.Server` (o `Kioku.Mcp.Server.exe` en Windows) en el directorio:
`src/Kioku.Mcp.Server/bin/Release/net10.0/<runtime>/publish/`

### 2. Registro en Clientes MCP

Compila el servidor (paso 1) y luego agrega Kioku a tu cliente MCP favorito:

> [!TIP]
> Usa `<RUTA_AL_BINARIO>` como `dotnet run --project /ruta/a/kioku/src/Kioku.Mcp.Server/` para desarrollo, o la ruta al binario compilado del paso 1.

#### OpenCode

Archivo: `~/.config/opencode/opencode.jsonc` o `./opencode.jsonc` (proyecto)

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "kioku": {
      "type": "local",
      "command": ["<RUTA_AL_BINARIO>"],
      "environment": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      },
      "enabled": true
    }
  }
}
```

#### Claude Code

Archivo: `.mcp.json` (raíz del proyecto) o `~/.claude.json` (global)

```json
{
  "mcpServers": {
    "kioku": {
      "type": "stdio",
      "command": "<RUTA_AL_BINARIO>",
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### Claude Desktop

Archivo:
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
- **Linux:** `~/.config/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### VS Code

Archivo: `.vscode/mcp.json` (workspace)

```json
{
  "servers": {
    "kioku": {
      "type": "stdio",
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### Cursor

Archivo: `.cursor/mcp.json` (raíz del proyecto)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### Zed

Archivo: `.zed/settings.json` (proyecto) o `~/.config/zed/settings.json` (global)

```json
{
  "context_servers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### JetBrains IDEs (IntelliJ, PyCharm, WebStorm, etc.)

Archivo:
- **macOS:** `~/Library/Application Support/JetBrains/AIAssistant/mcp.json`
- **Windows:** `%APPDATA%\JetBrains\AIAssistant\mcp.json`
- **Linux:** `~/.config/JetBrains/AIAssistant/mcp.json`

O desde Settings → Tools → AI Assistant → MCP.

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### Warp

Configuración gráfica desde Warp Settings → MCP Servers. Alternativamente, en el archivo de configuración del agente local:

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### GitHub Copilot CLI

Archivo: `.mcp.json` (raíz del proyecto) o `.vscode/mcp.json`.

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

#### Codex CLI (OpenAI)

Archivo: `config.toml` (raíz del proyecto o `~/.codex/`)

```toml
[mcp_servers.kioku]
command = "<RUTA_AL_BINARIO>"
args = []
env = { KIOKU_VAULT_PATH = "/ruta/a/tu/boveda" }
```

#### Antigravity CLI e IDE

Archivo: `.antigravity/mcp.json` (raíz del proyecto)

```json
{
  "mcpServers": {
    "kioku": {
      "command": "<RUTA_AL_BINARIO>",
      "args": [],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

### 3. Instalación del Plugin de Obsidian (Opcional)

> [!NOTE]
> El plugin de Obsidian **solo es necesario si deseas usar las herramientas del Bridge de la interfaz de usuario** (como abrir notas automáticamente en el editor, ver qué nota está activa o ejecutar comandos de Obsidian). Todas las demás funciones de lectura, escritura y búsqueda semántica funcionan directamente sobre los archivos, incluso con Obsidian cerrado.

Para instalar el plugin localmente en tu Obsidian:

1. **Instalar dependencias y compilar el plugin:**
   En la raíz del proyecto, ejecuta:
   ```bash
   pnpm install
   pnpm build:plugin
   ```
   Esto generará los archivos `main.js`, `manifest.json` y `styles.css` en la carpeta `src/obsidian-kioku-mcp/`.

2. **Copiar los archivos a tu bóveda:**
   Crea una carpeta llamada `kioku` dentro de la carpeta oculta de complementos de tu bóveda de Obsidian (`.obsidian/plugins/`):
   ```bash
   # Crear directorio del plugin
   mkdir -p /ruta/a/tu/boveda/.obsidian/plugins/kioku
   
   # Copiar archivos compilados
   cp src/obsidian-kioku-mcp/{main.js,manifest.json,styles.css} /ruta/a/tu/boveda/.obsidian/plugins/kioku/
   ```

3. **Habilitar el plugin en Obsidian:**
   * Abre Obsidian.
   * Ve a **Ajustes** -> **Complementos de la comunidad** (Community Plugins).
   * Haz clic en **Recargar** (Reload icon) para detectar el nuevo plugin.
   * Activa el interruptor junto a **Kioku MCP Bridge**.

---

### Variables de entorno

| Variable | Requerida | Descripción | Default |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | ✅ | Ruta absoluta a la bóveda de Obsidian | — |
| `KIOKU_TRANSPORT` | ❌ | Transporte MCP: `stdio` o `http` | `stdio` |
| `KIOKU_HTTP_PORT` | ❌ | Puerto del transporte HTTP-SSE | `5173` |
| `KIOKU_API_KEY` | ❌ | Bearer token para autenticar el transporte HTTP | — |
| `KIOKU_OLLAMA_URL` | ❌ | URL base del cliente Ollama local | `http://localhost:11434` |
| `KIOKU_EMBEDDING_MODEL` | ❌ | Modelo de Ollama utilizado para embeddings | `nomic-embed-text` |
| `KIOKU_GEN_MODEL` | ❌ | Modelo de Ollama para generación local (`summarize_note`), ej. `llama3.2` | — (deshabilitado) |
| `KIOKU_MAX_RESULTS` | ❌ | Máximo de resultados de búsqueda | `20` |
| `KIOKU_OBSIDIAN_PORT` | ❌ | Puerto del WebSocket bridge con Obsidian | `7765` |
| `KIOKU_BRIDGE_TOKEN` | ❌ | Token compartido del bridge WebSocket; debe coincidir con el setting "Auth token" del plugin | — |
| `KIOKU_GITHUB_TOKEN` | ❌ | Token de GitHub para `share_as_gist` | — |
| `KIOKU_ENABLE_METRICS` | ❌ | Contadores de uso de tools en memoria (opt-in) | `false` |
| `KIOKU_SENTRY_DSN` | ❌ | DSN de Sentry para reporte de crashes (opt-in) | — |

## MCP Tools Disponibles

115 herramientas organizadas en 18 clases. Para el inventario completo con parámetros, ver [`docs/commands-reference.md`](docs/commands-reference.md).

Las clases fuera del núcleo (consulta, escritura, utilidades) se activan o desactivan por **grupos de capacidades** en `{vault}/.kioku/config.yml` — ver [`docs/vault-config.md`](docs/vault-config.md).

| Categoría | Tools clave |
|---|---|
| **Consulta** | `read_note`, `search_notes`, `search_notes_semantic`, `search_notes_hybrid`, `filter_notes`, `get_note_metadata`, `get_backlinks`, `get_outgoing_links`, `find_similar_notes`, `list_notes` |
| **Escritura** | `create_note`, `update_note_content`, `prepend_to_note`, `append_to_note`, `update_frontmatter`, `add_tag`, `remove_tag`, `move_note`, `rename_note`, `delete_note` |
| **Tareas** | `list_tasks`, `complete_task`, `reopen_task`, `list_tasks_by_tag`, `list_overdue_tasks` |
| **Zettelkasten** | `create_zettel`, `create_moc`, `create_literature_note`, `link_related_notes`, `create_folder_readme` |
| **Workflows y Templates** | `create_note_from_template`, `list_templates`, `create_template`, `extract_action_items` |
| **Organización** | `normalize_tags`, `rename_tag_globally`, `merge_tags`, `suggest_tags`, `suggest_folder`, `reclassify_note`, `find_duplicate_notes`, `find_broken_links`, `audit_vault` |
| **Sesiones** | `start_work_session`, `end_work_session`, `get_recent_activity`, `get_work_context`, `list_work_sessions`, `get_session_activity` |
| **Grafo de conocimiento** | `get_concept_map`, `get_knowledge_timeline`, `get_vault_snapshot` |
| **Análisis de grafo** | `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density` |
| **Research** | `export_citations`, `export_note`, `get_literature_gap`, `import_bibtex`, `export_bibtex`, `share_as_gist`, `validate_research_notes` |
| **Restore** | `revert_note`, `list_deleted_notes`, `restore_note_from_trash`, `restore_note_version`, `revert_all_uncommitted` |
| **CSS Theming** | `apply_css_snippet`, `list_css_snippets`, `remove_css_snippet`, `reload_css_snippets` |
| **Assets** | `list_excalidraw_files`, `get_asset_metadata`, `find_orphan_assets`, `normalize_attachment_names`, `move_attachments_to_folder`, `reorder_notes_in_folder` |
| **Git** | `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`, `commit_staged`, `fix_merge_conflicts`, `resolve_merge_conflict` |
| **Plugin Bridge** | `query_dataview`, `apply_template`, `lint_note`, `lint_vault`, `get_installed_plugins` |
| **Obsidian UI** (requiere plugin) | `open_note_in_obsidian`, `get_active_note_in_obsidian`, `get_open_notes_in_obsidian`, `trigger_obsidian_command`, `insert_at_cursor`, `replace_selection`, `create_note_ui`, `scroll_to_block`, `open_in_split`, `get_selection_in_obsidian`, `toggle_reading_mode`, `fold_all_headings`, `unfold_all_headings`, `get_obsidian_status` |
| **Utilidades** | `ping`, `get_vault_stats`, `get_index_status`, `rebuild_index` |

## MCP Prompts & Resources

Además de las 115 tools, Kioku expone las otras dos primitivas de MCP (SDK `ModelContextProtocol` 1.4.0). El inventario completo vive junto al de tools en [`docs/commands-reference.md`](docs/commands-reference.md).

**Prompts** — workflows curados que aparecen como slash commands nativos en cualquier cliente MCP (Claude Code, Cursor, VS Code):

| Prompt | Argumentos | Descripción |
|---|---|---|
| `research_digest` | `folder?` | Resume actividad de lectura/investigación reciente y lista preguntas abiertas |
| `process_inbox` | `inbox?` | Guía el flujo propón → confirma → aplica de `process_inbox` |
| `weekly_review` | — | Revisión semanal: digest + tareas vencidas + huérfanas + sugerencias de enlaces |
| `literature_review` | `topic` | Recolecta evidencia existente sobre un tema y la sintetiza con citas `[[wikilink]]` |

**Resources** — permiten montar contenido del vault como contexto sin gastar una tool-call:

| Resource | Tipo | Descripción |
|---|---|---|
| `kioku://note/{path}` | Template | Contenido completo (con frontmatter) de una nota por ruta relativa al vault |
| `kioku://vault/stats` | Directo | Snapshot de estadísticas del vault (notas, tags, carpetas, estado del índice) |

`resources/list` devuelve solo las ~20 notas más recientes (no las 5000+ del vault) — usa el resource template `kioku://note/{path}` para leer cualquier nota por su ruta.

## Plugins de Obsidian Integrados (vía Plugin Bridge)

| Plugin | Comandos |
|---|---|
| **Dataview** | `query_dataview` — ejecuta queries DQL sobre la bóveda |
| **Templater** | `apply_template` — aplica templates con variables |
| **Linter** | `lint_note`, `lint_vault` — formatea y corrige notas |

## Estado del Proyecto

- **v1** (stdio): ✅ Completo — herramientas core + 22 comandos del bridge del plugin
- **v2** (HTTP-SSE): ✅ Completo — transporte dual, embeddings Ollama, auth Bearer Token, despliegue en VM
- **v3** (Ecosystem Tools): ✅ Completo — 102 herramientas en 17 clases: templates, tareas, Zettelkasten, CSS theming, assets, Git, restore, grafo

Ver [`docs/planning.md`](docs/planning.md) para el plan arquitectural completo, [`docs/features/`](docs/features/README.md) para los specs de las próximas features y [`docs/tasks/`](docs/tasks/README.md) para el desglose de trabajo priorizado.

## Licencia

MIT — ver [LICENSE](LICENSE)
