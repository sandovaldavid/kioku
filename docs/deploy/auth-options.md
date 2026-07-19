---
layout: default
title: Streamable HTTP Security
sidebar: true
---

# Streamable HTTP security

Kioku's HTTP transport exposes vault tools to multiple MCP clients. Treat it as a privileged
local service: it can read and mutate the configured Obsidian vault with the permissions of the
Kioku process.

## Secure defaults

The server applies these controls before the MCP endpoint handles a request:

- binds to `127.0.0.1` by default;
- refuses an unauthenticated non-loopback listener;
- compares bearer tokens in constant time;
- validates every present `Origin` header against an exact allowlist and returns HTTP 403 for
  malformed or disallowed origins;
- accepts missing `Origin` headers for non-browser MCP clients;
- limits request bodies to 1 MiB and MCP POST execution to five minutes by default;
- trusts no forwarded headers unless exact proxy IP addresses are configured;
- exposes only a minimal public liveness response;
- protects readiness with the same bearer token as `/mcp`.

These defaults follow the MCP Streamable HTTP transport requirement to validate `Origin` and the
recommendation to bind local servers only to loopback.

## Configuration

| Variable | Default | Description |
|---|---:|---|
| `KIOKU_HTTP_HOST` | `127.0.0.1` | Listener host or interface. Use `0.0.0.0` only for an intentional container/LAN deployment. |
| `KIOKU_HTTP_PORT` | `5173` | Listener port. |
| `KIOKU_API_KEY` | unset | Bearer token. Required for non-loopback binding. Generate at least 32 random bytes. |
| `KIOKU_HTTP_ALLOWED_ORIGINS` | loopback and Obsidian origins | Comma-separated exact browser origins. Do not use wildcards. |
| `KIOKU_HTTP_TRUSTED_PROXIES` | unset | Comma-separated exact proxy IP addresses allowed to set `X-Forwarded-For` and `X-Forwarded-Proto`. |
| `KIOKU_HTTP_MAX_REQUEST_BODY_BYTES` | `1048576` | Request-body limit; valid range is 1 KiB–100 MiB. |
| `KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS` | `300` | Timeout for MCP POST calls; valid range is 1–3600 seconds. SSE GET connections are not timed out. |
| `KIOKU_ALLOW_INSECURE_HTTP` | `false` | Explicit escape hatch for an unauthenticated non-loopback listener. Avoid in real deployments. |

Generate an API key without storing it in shell history:

```bash
openssl rand -hex 32
```

## Local-only deployment

No API key is required when the listener remains on loopback:

```bash
export KIOKU_VAULT_PATH=/path/to/vault
export KIOKU_TRANSPORT=http
kioku
```

The MCP endpoint is `http://127.0.0.1:5173/mcp`.

## Reverse proxy deployment

Keep Kioku bound to loopback and terminate TLS at nginx, Caddy, a private tunnel, or a mesh VPN.
Configure the proxy address explicitly so arbitrary clients cannot spoof forwarded headers:

```bash
export KIOKU_HTTP_HOST=127.0.0.1
export KIOKU_API_KEY="$(openssl rand -hex 32)"
export KIOKU_HTTP_TRUSTED_PROXIES=127.0.0.1,::1
```

For a browser-based MCP client, also allow the public origin exactly:

```bash
export KIOKU_HTTP_ALLOWED_ORIGINS=https://kioku.example.com
```

Non-browser clients normally omit `Origin` and don't need an allowlist entry. Never trust
forwarded headers from `0.0.0.0/0`, and don't enable ASP.NET Core's unrestricted cloud-forwarding
environment switch.

See [`nginx.conf`](nginx.conf) for a hardened example with TLS, disabled buffering, bounded body
size, and matching timeouts.

## Health endpoints

| Endpoint | Authentication | Successful response | Purpose |
|---|---|---|---|
| `/health/live` | Public | `200 {"status":"ok"}` | Confirms the process and HTTP listener are alive. |
| `/health/ready` | Bearer token when configured | `200` when the vault index is ready; otherwise `503` | Reports index and optional Ollama capability state without paths or secrets. |

Examples:

```bash
curl --fail http://127.0.0.1:5173/health/live

curl --fail \
  -H "Authorization: Bearer $KIOKU_API_KEY" \
  http://127.0.0.1:5173/health/ready
```

Use liveness for process restarts and readiness for load-balancer traffic decisions.

## Client configuration

```json
{
  "mcpServers": {
    "kioku": {
      "type": "http",
      "url": "https://kioku.example.com/mcp",
      "headers": {
        "Authorization": "Bearer replace-with-your-token"
      }
    }
  }
}
```

Use HTTPS, a private tunnel, or a mesh VPN whenever traffic leaves the local machine. A bearer
token sent over plaintext HTTP can be intercepted.

## Authentication boundary

`KIOKU_API_KEY` is intentionally a simple shared-secret mechanism for a trusted personal or small
team deployment. It has no users, scopes, expiry, refresh, or delegated authorization. Rotate the
token if it is exposed and avoid placing it in repository files, container images, logs, or URLs.

For an internet-facing or multi-tenant service, place Kioku behind a standards-based authorization
gateway today. A future Kioku multi-user mode should implement the MCP authorization specification
with OAuth-based discovery and scoped access rather than extending the static API-key mechanism.

## Unsafe override

Kioku refuses this configuration:

```bash
KIOKU_HTTP_HOST=0.0.0.0 KIOKU_TRANSPORT=http kioku
```

For isolated testing only, it can be overridden explicitly:

```bash
KIOKU_HTTP_HOST=0.0.0.0 \
KIOKU_ALLOW_INSECURE_HTTP=true \
KIOKU_TRANSPORT=http \
kioku
```

The server emits a prominent warning. The override is not a substitute for authentication,
firewall rules, TLS, or a private network.
