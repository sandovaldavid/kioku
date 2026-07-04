# Kioku MCP Plugin

Connects your Obsidian vault to the [Kioku MCP Server](https://github.com/sandovaldavid/kioku), enabling AI agents like Claude Code and Antigravity CLI to search, read, and modify your notes via the Model Context Protocol.

## What is Kioku?

Kioku (記憶, "memory" in Japanese) is a local-first MCP server that gives AI agents direct access to your Obsidian vault. This plugin is the bridge between Obsidian and the server, providing:

- **Real-time note opening** — agents can open notes in Obsidian
- **UI commands** — trigger Obsidian commands from agents
- **Selection access** — agents can read your current selection
- **Plugin integration** — access Dataview, Templater, and Linter from agents

## Requirements

1. **Kioku MCP Server** — install from [GitHub releases](https://github.com/sandovaldavid/kioku/releases) or via:
   ```bash
   # Docker
   docker-compose up -d
   
   # dotnet tool
   dotnet tool install -g kioku-mcp-server
   
   # Or download binary from releases
   ```

2. **Configure the server** with your vault path:
   ```bash
   export KIOKU_VAULT_PATH=/path/to/your/vault
   kioku
   ```

3. **Configure your MCP client** (Claude Code, etc.):
   ```json
   {
     "mcpServers": {
       "kioku": {
         "type": "stdio",
         "command": "kioku"
       }
     }
   }
   ```

## Features

This plugin provides a WebSocket bridge that allows the Kioku server to:

- **Open notes** in Obsidian when agents edit them
- **Execute commands** like toggling reading mode, folding headings
- **Access current selection** for context-aware operations
- **Integrate with plugins** like Dataview, Templater, and Linter
- **Show notifications** when agents interact with your vault

## Settings

- **Bridge Port** (default: 7765) — port for WebSocket connections
- **Show Notifications** — display notices when agents open notes

## Privacy & Security

- **Local-only** — the plugin only listens on `127.0.0.1` (localhost)
- **No telemetry** — no data is sent to external services
- **No cloud dependency** — works completely offline (except for Ollama if using semantic search)
- **Your data stays yours** — the server reads/writes directly to your vault files

## Troubleshooting

### Bridge won't start

Check if port 7765 is already in use:
```bash
lsof -i :7765
```

Change the port in plugin settings if needed.

### Agent can't open notes

Ensure:
1. Obsidian is open
2. The plugin is enabled
3. The server is running
4. The port matches in both plugin settings and server config

### Semantic search not working

Semantic search requires Ollama running locally:
```bash
ollama pull nomic-embed-text
```

The server will gracefully degrade to keyword search if Ollama is unavailable.

## Documentation

- [Full documentation](https://github.com/sandovaldavid/kioku/tree/main/docs)
- [Docker deployment guide](https://github.com/sandovaldavid/kioku/blob/main/docs/docker.md)
- [Architecture overview](https://github.com/sandovaldavid/kioku#architecture)

## Support

- **GitHub Issues** — report bugs and request features
- **GitHub Sponsors** — support development: https://github.com/sponsors/sandovaldavid

## License

MIT © sandovaldavid
