# Server Configuration Reference

> Generated from `docs/public-metadata.json`. Do not edit manually.
> Regenerate: `node scripts/generate-public-docs.mjs --write`
> Verify: `node scripts/generate-public-docs.mjs --check`

The `Kioku` configuration section is canonical. `KIOKU_*` environment variables remain supported compatibility aliases and are mechanically checked against runtime mappings and the MCP package manifest.

## Core

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_VAULT_PATH` | `Kioku:VaultPath` | yes | no | — | Absolute path to the Obsidian vault root. |
| `KIOKU_MAX_RESULTS` | `Kioku:MaxSearchResults` | no | no | `20` | Maximum number of search results returned by query tools. |

## Transport

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_TRANSPORT` | `Kioku:Transport` | no | no | `stdio` | MCP transport value: stdio or http. The http value selects Streamable HTTP. |
| `KIOKU_HTTP_HOST` | `Kioku:HttpHost` | no | no | `127.0.0.1` | Host or interface used by the Streamable HTTP listener. |
| `KIOKU_HTTP_PORT` | `Kioku:HttpPort` | no | no | `5173` | Port used by the Streamable HTTP listener. |

## Transport security

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_API_KEY` | `Kioku:ApiKey` | no | yes | — | Bearer token for Streamable HTTP; required for non-loopback bindings unless the unsafe override is explicitly enabled. |
| `KIOKU_HTTP_ALLOWED_ORIGINS` | `Kioku:HttpAllowedOrigins` | no | no | `http://localhost, http://127.0.0.1, http://[::1], app://obsidian.md` | Comma-separated exact browser origins accepted by Streamable HTTP. |
| `KIOKU_HTTP_TRUSTED_PROXIES` | `Kioku:HttpTrustedProxies` | no | no | — | Comma-separated proxy IP addresses trusted to supply forwarded headers. |
| `KIOKU_ALLOW_INSECURE_HTTP` | `Kioku:AllowInsecureHttp` | no | no | `false` | Unsafe override that permits a non-loopback Streamable HTTP listener without an API key. |
| `KIOKU_HTTP_MAX_REQUEST_BODY_BYTES` | `Kioku:HttpMaxRequestBodyBytes` | no | no | `1048576` | Maximum accepted MCP HTTP request body size in bytes. |
| `KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS` | `Kioku:HttpRequestTimeoutSeconds` | no | no | `300` | Maximum execution time for an MCP POST request in seconds. |

## Obsidian bridge

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_OBSIDIAN_PORT` | `Kioku:ObsidianBridgePort` | no | no | `7765` | WebSocket port exposed by the optional Obsidian plugin bridge. |
| `KIOKU_BRIDGE_TOKEN` | `Kioku:BridgeToken` | no | yes | — | Shared secret for the Obsidian bridge WebSocket; it must match the plugin setting. |

## Local AI

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_OLLAMA_URL` | `Kioku:OllamaUrl` | no | no | `http://localhost:11434` | Base URL of the Ollama service used for embeddings and optional generation. |
| `KIOKU_EMBEDDING_MODEL` | `Kioku:EmbeddingModel` | no | no | `nomic-embed-text` | Ollama embedding model. |
| `KIOKU_GEN_MODEL` | `Kioku:GenerationModel` | no | no | — | Ollama model for optional local generation tools; unset disables generation. |

## Performance

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_INDEX_CONCURRENCY` | `Kioku:IndexConcurrency` | no | no | `max(1, CPU count / 2)` | Maximum concurrent vault indexing operations. |
| `KIOKU_EMBEDDING_CONCURRENCY` | `Kioku:EmbeddingConcurrency` | no | no | `2` | Maximum concurrent embedding requests. |

## Filesystem security

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_ALLOW_EXTERNAL_READS` | `Kioku:AllowExternalReads` | no | no | `false` | Allows read-only imports outside the vault only from explicitly configured roots. |
| `KIOKU_EXTERNAL_READ_ROOTS` | `Kioku:ExternalReadRoots` | no | no | — | Platform-path-separator-delimited roots allowed for external read-only imports. |
| `KIOKU_ALLOW_PERMANENT_DELETE` | `Kioku:AllowPermanentDelete` | no | no | `false` | Enables irreversible deletion; soft delete remains available when false. |

## Integrations

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_GITHUB_TOKEN` | `Kioku:GitHubToken` | no | yes | — | GitHub token used by the share_as_gist tool. |

## Observability

| Environment variable | Configuration path | Required | Sensitive | Default | Description |
|---|---|---:|---:|---|---|
| `KIOKU_ENABLE_METRICS` | `Kioku:EnableMetrics` | no | no | `false` | Enables in-memory tool-call counters; note contents are never recorded. |
| `KIOKU_SENTRY_DSN` | `Kioku:SentryDsn` | no | yes | — | Opt-in Sentry DSN for crash reporting. |

## Transport terminology

- `stdio` — **stdio**: Local process transport used by desktop and CLI MCP clients.
- `http` — **Streamable HTTP**: HTTP transport for long-running or shared deployments; loopback is the secure default.
