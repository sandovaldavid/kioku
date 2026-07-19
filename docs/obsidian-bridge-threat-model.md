# Obsidian bridge threat model

The Kioku Obsidian plugin exposes a loopback WebSocket bridge so the Kioku MCP server can invoke a small set of Obsidian UI and plugin operations. This document describes the security boundary introduced by bridge protocol v3.

## Assets

- Notes and attachments in the active Obsidian vault.
- Editor state, selections, cursor position, open panes, and active note metadata.
- Commands registered by Obsidian and third-party plugins.
- The bridge authentication token.
- Availability of Obsidian and the plugin process.

## Trust boundaries

The bridge binds only to `127.0.0.1`. This prevents remote network clients from connecting directly, but **loopback is not an authentication mechanism**. Any process running as the same desktop user can attempt to connect.

The trusted deployment consists of:

1. the Kioku MCP server process;
2. the Kioku Obsidian plugin;
3. a shared bridge token configured in both components.

AI-generated tool arguments are untrusted. A prompt-injected document may attempt to send traversal paths, oversized payloads, arbitrary Obsidian command IDs, or repeated requests. The bridge validates and authorizes every message independently of the MCP model's intent.

## Protocol handshake

Every WebSocket connection must send `auth` as its first message. In protocol v3, this message is an authenticated capability handshake containing:

- minimum and maximum supported protocol versions;
- optional client identity metadata;
- requested capabilities;
- the shared token when configured.

The plugin rejects non-overlapping protocol ranges and closes the connection. Successful responses contain the negotiated protocol version and the capabilities enabled by plugin settings. Reconnecting creates a new authentication and replay-protection scope.

## Command capabilities

| Capability | Default | Examples |
| --- | --- | --- |
| `read` | Enabled | Active note, open notes, selection, app version |
| `ui-navigation` | Enabled | Open note, split pane, reading mode, fold headings |
| `editor-mutation` | Enabled | Insert text, replace selection, create note, reload snippets |
| Third-party integrations | Disabled | Dataview, Templater, per-file Linter |
| `vault-wide` | Disabled | Linter `lint-all-files` |
| `unsafe-command` | Disabled | Explicitly listed custom Obsidian command IDs |

The generic `trigger-command` endpoint does not discover or execute arbitrary commands. It accepts a small built-in UI allowlist. Additional command IDs require both unsafe mode and an explicit settings allowlist.

Enabling a capability means that an authenticated MCP client may invoke it without an additional Obsidian confirmation dialog. Vault-wide and custom command permissions should remain disabled unless the operator understands the registered command's effects.

## Transport controls

The plugin configures:

- loopback-only binding;
- a 256 KiB WebSocket payload limit;
- at most four concurrent clients;
- per-client request rate and concurrency limits;
- response backpressure limits;
- heartbeat ping/pong and stale-client termination;
- a ten-second command execution timeout;
- per-connection request ID replay detection;
- text-only JSON messages;
- sanitized error codes and messages.

Start, stop, and restart operations are serialized and idempotent. Stopping the bridge terminates clients, clears heartbeat timers, and waits for the listener to close.

## Authentication guidance

Generate a token from **Settings → Kioku MCP → Auth token**, copy it, and configure the same value as `KIOKU_BRIDGE_TOKEN` for the MCP server. Treat the token as a local secret:

- do not commit it to a repository;
- do not paste it into notes or prompts;
- do not log it;
- rotate it if another local process or user may have obtained it.

The plugin shows a security warning whenever the bridge starts without a token. Open mode exists for local development only.

## Threats mitigated

- Prompt-injected arbitrary Obsidian or third-party command execution.
- Payload type confusion and malformed paths.
- Unsupported protocol clients continuing after a warning.
- Oversized-message memory pressure.
- Connection flooding and request bursts.
- Stale or orphaned listeners after repeated starts/restarts.
- Request ID replay on an authenticated connection.
- Stack trace and host-detail disclosure in bridge responses.

## Residual risks

- A compromised process running as the desktop user can read Obsidian files directly, independently of Kioku.
- An explicitly allowlisted unsafe command may perform actions the plugin cannot classify.
- Third-party plugin APIs are undocumented and may change or have side effects beyond Kioku's control.
- A command that times out may continue inside Obsidian when the underlying API does not support cancellation; Kioku discards the late response.
- The token is stored in Obsidian plugin data. It protects the socket from unrelated local processes but is not a hardware-backed credential.

## Review checklist

Before enabling risky capabilities:

1. verify the MCP server is the expected local binary;
2. configure and test the shared token;
3. keep third-party and vault-wide capabilities disabled unless required;
4. inspect every additional custom command ID and its plugin source;
5. review bridge logs for repeated authorization, rate-limit, or protocol failures.
