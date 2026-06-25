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

## Inicio Rápido

### Pre-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Obsidian](https://obsidian.md) con tu bóveda de notas

### Configuración

```bash
# 1. Clonar el repositorio
git clone https://github.com/sandovaldavid/kioku
cd kioku

# 2. Configurar la ruta de tu bóveda
export KIOKU_VAULT_PATH="/ruta/a/tu/boveda"

# 3. Verificar que compila
dotnet build src/Kioku.Mcp.Server/

# 4. Registrar en tu agente de IA (añadir al .mcp.json del agente)
```

### Registro en Claude Code / agy

Añade al `.mcp.json` del directorio raíz donde trabaja tu agente:

```json
{
  "servers": {
    "kioku": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "/ruta/a/kioku/src/Kioku.Mcp.Server/"],
      "env": {
        "KIOKU_VAULT_PATH": "/ruta/a/tu/boveda"
      }
    }
  }
}
```

### Variables de entorno

| Variable | Requerida | Descripción | Default |
|---|---|---|---|
| `KIOKU_VAULT_PATH` | ✅ | Ruta absoluta a la bóveda de Obsidian | — |
| `KIOKU_MAX_RESULTS` | ❌ | Máximo de resultados de búsqueda | `20` |
| `KIOKU_OBSIDIAN_PORT` | ❌ | Puerto del WebSocket bridge con Obsidian | `7765` |

## MCP Tools Disponibles (v1)

### Consulta (Read-Only)
| Tool | Descripción |
|---|---|
| `ping` | Health check del servidor |
| `read_note` | Lee el contenido completo de una nota |
| `list_notes` | Lista todas las notas (o de una carpeta) |
| `search_notes` | Búsqueda full-text en toda la bóveda |
| `filter_notes` | Filtra notas por tags, status, tipo, fecha |
| `get_note_metadata` | Lee solo el frontmatter YAML |
| `get_backlinks` | Notas que enlazan a una nota dada |
| `get_vault_stats` | Estadísticas de la bóveda |
| `get_index_status` | Estado del índice en memoria |
| `rebuild_index` | Re-indexar toda la bóveda |

### Escritura
| Tool | Descripción |
|---|---|
| `create_note` | Crea una nota nueva con frontmatter |
| `append_to_note` | Añade texto al final de una nota |
| `update_frontmatter` | Actualiza campos del frontmatter YAML |
| `add_tag` / `remove_tag` | Gestiona tags de una nota |
| `move_note` | Mueve una nota a otra carpeta |

## Hoja de Ruta

- **v1** (actual): Transporte stdio, búsqueda full-text, lectura/escritura básica
- **v2**: HTTP-SSE (múltiples agentes), búsqueda semántica con Ollama, assets (Excalidraw, imágenes)
- **v3**: Native AOT, publicación en Obsidian Community Plugin Store

Ver [`docs/planning.md`](docs/planning.md) para el plan arquitectural completo.

## Licencia

MIT — ver [LICENSE](LICENSE)
