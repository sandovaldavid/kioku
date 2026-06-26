# Kioku MCP — Obsidian Plugin

> Versión: **1.6.2** — [Release notes](https://github.com/sandovaldavid/kioku/releases)

Plugin puente (thin client) que conecta Obsidian con el servidor MCP Kioku mediante WebSocket local.

## Rol

El plugin actúa como un **servidor WebSocket** dentro de Obsidian. El servidor C# (Kioku.Mcp.Server) se conecta como cliente para ejecutar comandos de UI: abrir notas, consultar la nota activa, ejecutar comandos de Obsidian, etc.

Toda la lógica pesada (búsqueda, indexación, embeddings) corre en el servidor C# — el plugin solo traduce comandos a la API de Obsidian.

## Comandos del Plugin

| Comando | Descripción |
|---|---|
| `open-file` | Abre y enfoca una nota |
| `get-active-note` | Nota actualmente activa |
| `get-open-notes` | Todas las pestañas abiertas |
| `trigger-command` | Ejecuta cualquier comando de Obsidian por ID |
| `toggle-reading-mode` | Alterna edición/lectura |
| `get-selection` | Texto seleccionado en el editor |
| `fold-all-headings` / `unfold-all-headings` | Colapsa/expande headings |
| `reload-snippets` | Recarga snippets CSS |
| `scroll-to-block` | Desplaza a un bloque específico |
| `open-in-split` | Abre en panel dividido |
| `get-vault-path` | Ruta de la bóveda activa |
| `is-obsidian-ready` | Estado de carga de Obsidian |
| `get-app-version` | Versión de Obsidian y del plugin |
| `create-note-ui` | Crea nota y la abre en Obsidian |
| `insert-at-cursor` | Inserta texto en el cursor |
| `replace-selection` | Reemplaza texto seleccionado |

## Instalación

1. Asegúrate de tener el servidor Kioku.Mcp.Server configurado.
2. Copia los archivos compilados a tu bóveda:
   ```bash
   mkdir -p /ruta/a/tu/boveda/.obsidian/plugins/kioku
   cp {main.js,manifest.json,styles.css} /ruta/a/tu/boveda/.obsidian/plugins/kioku/
   ```
3. En Obsidian: **Ajustes → Complementos de la comunidad → Recargar → Activar Kioku MCP Bridge**.

## Configuración

El puerto WebSocket se configura en el servidor C# mediante `KIOKU_OBSIDIAN_PORT` (default: `7765`).

## Desarrollo

```bash
# Instalar dependencias
pnpm install

# Desarrollo con hot-reload
pnpm dev

# Build de producción
pnpm build

# Lint
pnpm lint
pnpm format

# Output
# - main.js   (plugin bundle)
# - manifest.json
# - styles.css
```

## APIs de Obsidian Utilizadas

- `app.workspace` — navegación y pestañas
- `app.vault` — lectura/escritura de archivos
- `app.metadataCache` — caché de metadatos
- `app.commands` — ejecución de comandos
- `app.customCss` — snippets y temas

Todas son APIs públicas documentadas. Sin dependencias externas no aprobadas.
