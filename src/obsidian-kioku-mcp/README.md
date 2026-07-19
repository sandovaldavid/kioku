# Kioku MCP Plugin

Kioku MCP Plugin is the desktop-only Obsidian bridge for the Kioku MCP server. It lets Claude Code, Codex, OpenCode, Antigravity, and other compatible MCP clients coordinate with the open Obsidian application while the server remains responsible for vault indexing and filesystem access.

## Requirements

- Obsidian 1.13.0 or newer on desktop.
- Kioku MCP Server configured with the same vault and bridge settings.
- A shared bridge token is strongly recommended.

## Installation

Until the plugin has its independent repository and Community listing, install the `main.js`, `manifest.json`, and `styles.css` release assets in `<vault>/.obsidian/plugins/kioku-mcp/`, then enable **Kioku MCP** in Obsidian.

Configure the server with matching values:

```bash
export KIOKU_VAULT_PATH=/path/to/vault
export KIOKU_OBSIDIAN_PORT=7765
export KIOKU_BRIDGE_TOKEN=<same token configured in Obsidian>
```

## Settings

- **Bridge status** shows running/stopped state, loopback address, authentication state, connected clients, protocol range, and plugin version.
- **Bridge port** defaults to `7765`; a valid change restarts the bridge automatically.
- **Auth token** is the shared handshake secret. Generate or replace it in settings and update `KIOKU_BRIDGE_TOKEN` on the server. Token changes restart the bridge automatically.
- **Show notifications** controls bridge and security notices.
- **Show status bar** displays bridge state and client count; clicking it restarts the bridge.
- **Allow editor mutations** permits cursor, selection, note creation, and snippet reload commands.
- **Allow third-party integrations** permits guarded Dataview, Templater, and per-file Linter operations.
- **Allow vault-wide operations** permits explicitly supported whole-vault operations and requires third-party integrations.
- **Allow unsafe custom commands** permits only the additional command IDs explicitly listed by the user.

Configuration changes that affect the listener, authentication, or command policy are persisted and applied through a serialized bridge restart.

## Lifecycle and compatibility

`onload()` only loads settings and registers UI/commands. The WebSocket listener starts after `workspace.onLayoutReady()` and logs measured deferred startup time. Production builds are minified, omit source maps, and must remain below the reviewed 512 KiB bundle budget.

Undocumented Obsidian command and plugin registries are isolated in `src/obsidian-compat.ts` with capability detection and graceful fallback. Vault path access uses the public `FileSystemAdapter` type and an `instanceof` guard. The bridge protocol currently supports version 3.

## Security limitations

- The bridge binds only to `127.0.0.1`; it is not a remote-access service.
- Without an auth token, any local process can attempt a handshake.
- Third-party and vault-wide operations are disabled by default.
- Custom command execution is allowlisted; arbitrary discovery is not exposed.
- The plugin has no telemetry and does not send note contents to a cloud service.

## Troubleshooting

### Bridge does not start

Confirm the configured port is free and inspect the Obsidian developer console. The settings page reports stopped state and provides a **Start** action.

### Server cannot connect

Confirm Obsidian is open, the plugin is enabled, port and token match the server, and the server supports bridge protocol version 3.

### Configuration changed but behavior is stale

Listener and permission changes restart automatically. Use **Restart bridge** from the command palette or click the status bar item to force a refresh.

### Third-party command is unavailable

Enable the relevant plugin, enable **Allow third-party integrations**, and confirm the installed plugin version still exposes the integration capability. Kioku returns a dependency-unavailable response instead of assuming internal APIs exist.

## Development

```bash
pnpm install --frozen-lockfile
pnpm --filter obsidian-kioku-mcp run check:compatibility
pnpm --filter obsidian-kioku-mcp run test
pnpm --filter obsidian-kioku-mcp run build
pnpm --filter obsidian-kioku-mcp run validate:release
```

For manual testing, create a disposable vault, copy/link this plugin directory to `.obsidian/plugins/kioku-mcp`, run `pnpm --filter obsidian-kioku-mcp run dev`, enable the plugin, and use Obsidian's startup stopwatch plus developer console to record load behavior.

See [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) and [CHANGELOG.md](CHANGELOG.md).

## License

MIT © sandovaldavid
