# Kioku — MCP Server for Obsidian

> **Kioku** (記憶) significa "memoria" en japonés.

Kioku es un servidor MCP (Model Context Protocol) que permite a agentes de IA como **Claude Code** y **Antigravity CLI** leer, buscar y escribir en tu bóveda de Obsidian de manera nativa, rápida y privada.

---

## ¿Qué hace Kioku?

- 🔍 **Búsqueda full-text** en todas tus notas por contenido, tags y título
- 📖 **Lectura y escritura** de notas directamente desde el agente de IA
- 🏷️ **Gestión de tags** y metadatos (frontmatter YAML)
- 🔗 **Navegación de wikilinks** — backlinks y enlaces salientes
- 🖥️ **Bridge con Obsidian** — el agente puede abrir notas en la app (opcional)
- ⚡ **Inicio bajo demanda** — no consume recursos cuando no se usa

## Arquitectura

```
Agente de IA (Claude Code / agy)
        │ stdio (MCP Protocol)
        ▼
Kioku.Mcp.Server (C# .NET 10)
        │
        ├── VaultIndexService (FileSystemWatcher + índice invertido)
        ├── NoteQueryTools (read_note, search_notes, list_notes, ...)
        ├── NoteCommandTools (create_note, append_to_note, ...)
        └── UtilityTools (ping, rebuild_index, ...)
        │
        │ WebSocket (opcional, solo si Obsidian está abierto)
        ▼
Plugin Obsidian (TypeScript)
        │ Obsidian API
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

### 2. Registro en Clientes MCP (Claude Code, Cursor, VS Code, etc.)

Los clientes MCP ejecutarán este binario en segundo plano utilizando el protocolo `stdio`.

#### Para Claude Code (`.mcp.json`)
Añade lo siguiente al archivo `.mcp.json` en la raíz de tu espacio de trabajo o directorio raíz del agente:

```json
{
  "mcpServers": {
    "kioku": {
      "command": "/ruta/absoluta/a/kioku/src/Kioku.Mcp.Server/bin/Release/net10.0/<runtime>/publish/Kioku.Mcp.Server",
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/absoluta/a/tu/boveda",
        "KIOKU_OLLAMA_URL": "http://localhost:11434",
        "KIOKU_EMBEDDING_MODEL": "nomic-embed-text"
      }
    }
  }
}
```

#### Para Claude Desktop (`claude_desktop_config.json`)
Agrega la configuración en tu archivo de configuración de Claude Desktop (ubicado típicamente en `%APPDATA%/Claude/claude_desktop_config.json` en Windows o `~/Library/Application Support/Claude/claude_desktop_config.json` en macOS):

```json
{
  "mcpServers": {
    "kioku": {
      "command": "/ruta/absoluta/a/kioku/src/Kioku.Mcp.Server/bin/Release/net10.0/<runtime>/publish/Kioku.Mcp.Server",
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/absoluta/a/tu/boveda",
        "KIOKU_OLLAMA_URL": "http://localhost:11434",
        "KIOKU_EMBEDDING_MODEL": "nomic-embed-text"
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
| `KIOKU_OLLAMA_URL` | ❌ | URL base del cliente Ollama local | `http://localhost:11434` |
| `KIOKU_EMBEDDING_MODEL` | ❌ | Modelo de Ollama utilizado para embeddings | `nomic-embed-text` |
| `KIOKU_MAX_RESULTS` | ❌ | Máximo de resultados de búsqueda | `20` |
| `KIOKU_OBSIDIAN_PORT` | ❌ | Puerto del WebSocket bridge con Obsidian | `7765` |

## MCP Tools Disponibles (v2)

### Consulta (Read-Only)
| Tool | Parámetros | Descripción |
|---|---|---|
| `ping` | — | Health check del servidor |
| `read_note` | `note` | Lee el contenido completo de una nota |
| `list_notes` | `folder?` | Lista todas las notas (o de una carpeta específica) |
| `search_notes` | `query`, `max_results?` | Búsqueda full-text en toda la bóveda |
| `search_notes_semantic` | `query`, `max_results?`, `min_score?` | Búsqueda semántica usando Ollama embeddings (requiere GPU local/Ollama) |
| `filter_notes` | `tag?`, `status?`, `type?`, `date_from?`, `date_to?` | Filtra notas por tags, status, tipo, fecha |
| `get_note_metadata` | `note` | Lee solo el frontmatter YAML de la nota |
| `get_backlinks` | `note_name` | Notas que enlazan a una nota dada |
| `get_outgoing_links` | `note` | Enlaces salientes de una nota dada |
| `get_vault_stats` | — | Estadísticas de la bóveda (conteo de notas, tags, carpetas) |
| `get_index_status` | — | Estado del índice en memoria y tiempos de indexación |
| `rebuild_index` | — | Re-indexar toda la bóveda y regenerar caché de embeddings |

### Escritura
| Tool | Parámetros | Descripción |
|---|---|---|
| `create_note` | `name`, `content`, `tags?`, `type?`, `status?` | Crea una nota nueva con frontmatter estructurado |
| `update_note_content` | `note`, `content` | Reemplaza el cuerpo de la nota manteniendo frontmatter |
| `prepend_to_note` | `note`, `content` | Inserta texto al inicio de la nota después del frontmatter |
| `append_to_note` | `note`, `content`, `add_separator?` | Añade texto al final de una nota |
| `update_frontmatter` | `note`, `tags?`, `status?`, `type?` | Actualiza o añade campos al frontmatter YAML |
| `add_tag` | `note`, `tags` | Añade tags (separados por coma) |
| `remove_tag` | `note`, `tags` | Remueve tags (separados por coma) |
| `move_note` | `note`, `destination_folder` | Mueve una nota a otra carpeta |
| `rename_note` | `note`, `new_name` | Cambia el nombre o ruta de la nota |

### Obsidian UI Bridge
*(Requiere la aplicación de Obsidian abierta y el plugin Kioku activado)*

| Tool | Parámetros | Descripción |
|---|---|---|
| `open_note_in_obsidian` | `note` | Abre y enfoca una nota en el editor de Obsidian |
| `get_active_note_in_obsidian` | — | Obtiene metadatos de la nota actualmente activa en el editor |
| `get_open_notes_in_obsidian` | — | Obtiene las notas en todas las pestañas actualmente abiertas |
| `trigger_obsidian_command` | `command_id` | Ejecuta cualquier comando registrado en la paleta de Obsidian |

## Hoja de Ruta

- **v1** (actual): Transporte stdio, búsqueda full-text, lectura/escritura básica
- **v2**: HTTP-SSE (múltiples agentes), búsqueda semántica con Ollama, assets (Excalidraw, imágenes)
- **v3**: Native AOT, publicación en Obsidian Community Plugin Store

Ver [`docs/planning.md`](docs/planning.md) para el plan arquitectural completo.

## Licencia

MIT — ver [LICENSE](LICENSE)
