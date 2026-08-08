---
layout: default
title: ChatGPT Secure MCP Tunnel
sidebar: true
---

# ChatGPT Secure MCP Tunnel

> **Status:** Kioku's local `stdio` server is implemented and supported. The OpenAI Secure MCP Tunnel integration described here is an external developer-mode integration and remains **Unconfirmed end to end** until the ChatGPT validation matrix is completed.

Use this path when ChatGPT developer mode needs private access to a Kioku server running on the same trusted workstation as the vault. Secure MCP Tunnel keeps the MCP endpoint private: `tunnel-client` establishes outbound connectivity to OpenAI and reaches the local MCP process without requiring public ingress to Kioku or the configured vault.

## Recommended topology

```text
ChatGPT
  -> registered Kioku MCP connection
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client on the trusted workstation
  -> kioku (stdio)
  -> configured local Obsidian vault
```

For private workstation use, prefer Kioku `stdio` rather than exposing Streamable HTTP solely for ChatGPT. Kioku's HTTP transport remains available for deployments that already need a long-running private HTTP service, but it has its own bearer-token, origin, proxy, and listener security contract.

## Why stdio is the default tunnel target

Kioku already uses `stdio` as its default transport. With a Secure MCP Tunnel:

- no public Kioku listener is created;
- the configured vault remains a local filesystem path;
- Kioku's HTTP `KIOKU_API_KEY` contract does not need to be translated into an unrelated OAuth model;
- the same MCP tools, prompts, resources, capability profile, indexing, sessions, and filesystem services registered by the server remain authoritative;
- `tunnel-client` handles its own control-plane authentication separately from Kioku.

Do not disable or weaken Kioku HTTP authentication to make this integration work.

## Prerequisites

1. Install Kioku and verify that the command is on `PATH` without starting the MCP process:

   ```bash
   dotnet tool install --global kioku-mcp-server
   command -v kioku
   ```

   Kioku currently starts the MCP server directly and does not expose a documented `--help` command, so do not use `kioku --help` as a harmless presence check.

2. Configure the vault path in the environment that will start the tunnel:

   ```bash
   export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
   ```

3. Create or identify an OpenAI Secure MCP Tunnel for the ChatGPT workspace you will use.
4. Install the current OpenAI `tunnel-client` from the OpenAI Platform tunnel settings or official release.
5. Obtain a runtime API key with the tunnel permissions required by OpenAI.

Never commit the vault path, runtime API key, tunnel credentials, or other workstation-local secrets.

## Configure the tunnel locally

Substitute your real tunnel ID and keep all secrets in the local environment:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
export CONTROL_PLANE_API_KEY="<runtime-api-key>"

tunnel-client init \
  --sample sample_mcp_stdio_local \
  --profile kioku-local \
  --tunnel-id tunnel_REPLACE_WITH_REAL_ID \
  --mcp-command "kioku"

tunnel-client doctor --profile kioku-local --explain
tunnel-client run --profile kioku-local
```

Keep `tunnel-client run` healthy while ChatGPT uses the native Kioku connection. Stopping the tunnel makes that private MCP path unavailable until it reconnects.

## Register Kioku in ChatGPT developer mode

1. Enable ChatGPT developer mode.
2. Open the Plugins page and choose to create a new plugin/MCP connection.
3. Use `Kioku` as the name and describe it as the local-first project-memory MCP server.
4. Under **Connection**, choose **Tunnel** rather than **Server URL**.
5. Select the available tunnel or enter its real `tunnel_id`.
6. Create the connection and inspect the discovered MCP tools/metadata.
7. Verify read-only calls before attempting writes.
8. Verify one deliberately scoped focused write; do not use destructive tools merely to prove connectivity.
9. Copy the resulting technical ChatGPT connection ID only if a separate plugin package needs to reference the registered connection.

Do not guess an Authentication dropdown value from this repository. The stdio MCP process does not use `ApiKeyMiddleware`; validate the current ChatGPT tunnel registration behavior in the target workspace.

## Minimum native smoke set

Use a known test project or explicitly scoped disposable memory entry.

| Check | Expected evidence |
| --- | --- |
| `get_server_capabilities` | live capability profile from the connected Kioku server |
| `list_projects` | configured project identifiers returned by Kioku |
| `get_project_context` | compact current context for a known project |
| focused write | successful `save_project_knowledge`, `record_adr`, `record_bug`, or `create_implementation_plan` call on an explicitly scoped test case |
| work sessions, when intentionally tested | real session identity from `start_work_session` / `end_work_session` |

Do not exercise `delete_note`, permanent trash operations, broad moves, or other destructive tools solely for a connection smoke test.

## Provider behavior for a ChatGPT Kioku workflow

Use this provider order:

1. connected native Kioku MCP tool/capability;
2. a separately configured Cortex-L7 compatibility workflow when native Kioku is unavailable;
3. `Blocked` when neither provider can satisfy the requested operation.

Any compatibility fallback is intentionally weaker and must not claim live MCP discovery, semantic/hybrid retrieval, local Obsidian state, native session identity, atomic filesystem writes, compare-and-swap enforcement, mutation IDs, claims, leases, or fencing.

## Streamable HTTP alternative

Kioku also exposes Streamable HTTP at `/mcp`. Use it behind Secure MCP Tunnel only when you already have a reason to operate Kioku as an HTTP service.

The current HTTP security contract remains unchanged:

- loopback is the default binding;
- non-loopback HTTP requires `KIOKU_API_KEY` unless an explicit unsafe override is set;
- configured exact browser origins and trusted proxies remain enforced;
- the liveness endpoint is the only API-key exemption when a key is configured.

See [Streamable HTTP authentication](auth-options.md) before changing HTTP deployment settings.

## Public plugin boundary

Secure MCP Tunnel is appropriate for private/developer-mode use. It is not a substitute for the public HTTPS, authentication, privacy, stable-schema, and review requirements of a universally published ChatGPT plugin. Treat public Kioku publication as a separate architecture and security project.

## External references

- OpenAI Secure MCP Tunnel: <https://developers.openai.com/api/docs/guides/secure-mcp-tunnels>
- Connect an MCP server to ChatGPT: <https://developers.openai.com/plugins/deploy/connect-chatgpt>
- Plugin packaging: <https://developers.openai.com/plugins/build/plugins>
