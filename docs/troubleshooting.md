# Troubleshooting

Use this guide against the same branch and runtime you are executing. The generated [MCP contract reference](commands-reference.md) and [configuration reference](configuration-reference.md) are authoritative when examples disagree with a client UI.

## Server does not start

1. Confirm `KIOKU_VAULT_PATH` is set to an existing, accessible directory.
2. Confirm the client launches the expected `kioku` executable or the expected source build.
3. Check stderr or the MCP client's server logs. Under `stdio`, stdout is reserved for protocol traffic.
4. When running from source, use the .NET SDK selected by `global.json`.

Source diagnostics:

```bash
dotnet --version
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
```

Node.js is required for repository documentation tooling, not for running the installed .NET tool.

## MCP client cannot connect over stdio

- Use an absolute vault path.
- Verify the client configuration passes `KIOKU_VAULT_PATH`.
- Verify the command is available in the environment the client actually launches (`command -v kioku` on POSIX shells or `Get-Command kioku` in PowerShell).
- Restart the MCP client after changing its configuration.
- Inspect client logs for process-launch, permission, or JSON-RPC initialization errors.

The Obsidian plugin is **not required** for stdio or Streamable HTTP. It is required only for tools in the optional `bridge` and `plugin` capability groups.

Cold vault reconciliation no longer blocks the MCP transport startup path. A freshly started process can therefore accept warm-up-safe calls while the note index is still reconciling. `get_server_capabilities`, `get_server_status`, `list_projects`, and `get_project_context` are intentionally safe during that period; index-dependent operations wait for the cold-index readiness gate instead of reading a partial index.

After connection, call `get_server_status` through the MCP client to inspect vault, index, Ollama, bridge, and capability state. Call `get_server_capabilities` to inspect the stable profile, schema versions, transport, observability state, and rollout gate.

## Streamable HTTP does not start

For loopback development:

```bash
export KIOKU_TRANSPORT=http
export KIOKU_HTTP_HOST=127.0.0.1
export KIOKU_HTTP_PORT=5173
kioku
```

Check liveness:

```bash
curl -f http://127.0.0.1:5173/health/live
```

A non-loopback host requires `KIOKU_API_KEY` unless `KIOKU_ALLOW_INSECURE_HTTP=true` is deliberately set. The unsafe override is not recommended.

Common failures:

- invalid host or port;
- missing API key for a non-loopback bind;
- disallowed browser `Origin`;
- a reverse proxy not listed in `KIOKU_HTTP_TRUSTED_PROXIES`;
- request bodies or execution exceeding configured limits.

See [deploy/auth-options.md](deploy/auth-options.md).

## HTTP client receives 401, 403, 413, or a timeout

- **401** — send the configured bearer token.
- **403** — verify the exact `Origin` value is in `KIOKU_HTTP_ALLOWED_ORIGINS`.
- **413** — the request exceeds `KIOKU_HTTP_MAX_REQUEST_BODY_BYTES`.
- **Timeout** — the MCP POST exceeded `KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS`.

`/health/live` is public and minimal. `/health/ready` follows the protected deployment configuration. Because runtime initialization is background work, the process can be live while `/health/ready` still reports not-ready during cold reconciliation or later initialization. Do not use liveness as proof that every index-dependent workflow is ready.

## Index is loading or appears stale

Call `get_server_status` first. During cold startup it can report index/runtime work still in progress even though the MCP transport is already connected.

Expected behavior during cold reconciliation:

- warm-up-safe status/project-context calls can return;
- index-dependent calls wait on the cold-index readiness gate rather than returning partial search/graph/note-resolution state;
- genuine reconciliation failure is observable as failed readiness rather than silently falling back to an incomplete index.

Use the MCP `rebuild_index` tool when a full rebuild is required. Do not delete `.kioku/embeddings.bin` unless you intentionally want embeddings regenerated.

If external tools modify many files at once, allow the file watcher and indexing queue to settle, then inspect status again. See [indexing-pipeline.md](indexing-pipeline.md).

## `audit_vault` reports broken, ambiguous, malformed, or template-placeholder links

`audit_vault` deliberately keeps these categories separate:

- **broken / missing** — the target cannot be resolved;
- **ambiguous** — more than one canonical candidate matches;
- **malformed** — the reference syntax or path is invalid, including traversal outside the vault boundary;
- **template placeholder** — a closed empty wikilink/embed in a recognized template source.

Counts distinguish occurrences, unique source-target edges, and unique targets. Use the returned pagination metadata (`offset`, `limit`, `has_more`) to retrieve the complete finding set rather than treating the first text preview as the entire audit.

Kioku's canonical resolver supports exact/vault-relative paths, source-relative links, unique basenames and aliases, dotted basenames, and literal `#` filenames. Do not rewrite a vault merely to reduce an audit counter until the structured finding status and canonical resolution are understood.

Template placeholders are audit evidence, not malformed live links. Non-empty malformed references inside templates remain malformed, and traversal outside the configured vault remains rejected.

## Semantic or hybrid search is unavailable

Keyword search does not require Ollama. Semantic retrieval requires:

```bash
ollama serve
ollama pull nomic-embed-text
```

Verify that `KIOKU_OLLAMA_URL` and `KIOKU_EMBEDDING_MODEL` match the running service. A remote `KIOKU_OLLAMA_URL` can send note content off the local machine; review the [threat and privacy model](threat-and-privacy-model.md).

## Generation tools are unavailable

Generation tools are optional and disabled unless both conditions are met:

1. the `generation` capability group is enabled for the vault;
2. `KIOKU_GEN_MODEL` names an available Ollama model.

Restart Kioku after changing capability configuration.

## Coordination tools are unavailable

The coordination capability group is disabled by default. Confirm the capability state before changing a vault configuration:

1. Call `get_server_capabilities` and check `capability_group.enabled`.
2. Check the vault's `.kioku/config.yml` and confirm `coordination` is in the enabled capability list only for an explicitly reviewed deployment.
3. Restart Kioku after changing capability configuration.
4. Confirm that the profile reports `kioku.durable-coordination`, profile version `1`, schema version `1`, and rollout status `gated`.

If the profile is enabled but a claim or mutation fails, inspect the stable coordination error code and use the read-only history or conflict tools. Do not retry with a different claim or fence value until the current projection and history have been reloaded.

Coordination supports shared processes only on the documented local filesystem boundary. Cloud-sync folders, network replicas, and independent Git checkouts are not supported shared-coordination deployments.

## Coordination observability is missing

Metrics are in-memory and disabled unless `KIOKU_ENABLE_METRICS=true`. W3C activities require both `KIOKU_ENABLE_TRACING=true` and a host-configured activity listener; Kioku does not configure an exporter. `KIOKU_SENTRY_DSN` enables a separate, opt-in crash sink. Review [coordination observability](coordination-observability.md) before forwarding logs or adding an exporter.

## Obsidian bridge tools are unavailable

The bridge is optional and maintained in [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian).

Verify:

- the plugin is installed and enabled in Obsidian;
- the `bridge` or `plugin` capability group is enabled as required;
- `KIOKU_OBSIDIAN_PORT` matches the plugin port;
- `KIOKU_BRIDGE_TOKEN` matches the plugin token;
- server and plugin support the negotiated bridge protocol.

Use `get_server_status` and `get_obsidian_state` through the MCP client. See [versioning.md](versioning.md) for compatibility semantics.

## Docker Compose fails

Validate the root Compose file:

```bash
docker compose config
```

The supplied stack requires `KIOKU_API_KEY`:

```bash
export KIOKU_API_KEY="$(openssl rand -hex 32)"
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
docker compose up --build
```

Check service logs and health:

```bash
docker compose ps
docker compose logs kioku-server
curl -f http://127.0.0.1:5173/health/live
```

See [docker.md](docker.md).

## Generated documentation is out of sync

```bash
dotnet build Kioku.slnx --configuration Release --no-restore
node scripts/generate-public-docs.mjs --write
node scripts/generate-public-docs.mjs --check
```

Do not hand-edit generated contract files.

## Before reporting a bug

Include:

- operating system and architecture;
- target branch, tag, or package version;
- transport (`stdio` or Streamable HTTP);
- exact command or MCP tool call;
- relevant stderr/client logs with secrets and private paths removed;
- whether Ollama or the optional Obsidian plugin was involved;
- the smallest reproducible vault fixture that does not expose private notes.
