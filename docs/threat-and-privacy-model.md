# Threat and privacy model

This document describes Kioku's current security and privacy boundaries on `develop`. Each section distinguishes **Implemented** controls from named gaps or **Unconfirmed** behavior.

The generated [MCP commands reference](commands-reference.md) is authoritative for tool risk annotations. [Streamable HTTP authentication](deploy/auth-options.md) is authoritative for deployment recipes.

## Trust assumptions

Kioku assumes the operating system and the account running it are trusted. It does not defend against:

- a compromised operating system;
- a malicious process already running as the same user;
- a malicious Obsidian plugin with direct vault or UI access;
- an MCP client deliberately exposing secrets or note content outside Kioku.

Tool arguments and vault content are still treated as untrusted at filesystem, HTTP, and bridge boundaries.

## Protected assets

- Markdown notes, attachments, and YAML frontmatter in the configured vault.
- Work-session files and ownership metadata.
- The derived embeddings cache at `{vault}/.kioku/embeddings.bin`.
- Obsidian editor state exposed by the optional bridge.
- Availability of Kioku, Ollama, and the optional plugin.
- Secrets: `KIOKU_API_KEY`, `KIOKU_BRIDGE_TOKEN`, and `KIOKU_SENTRY_DSN`.

## Local and external data flows

Kioku's default configuration keeps vault content on the local machine.

| Data | Destination | Default | Leaves the machine when |
|---|---|---|---|
| Note text for embeddings | `{KIOKU_OLLAMA_URL}/api/embeddings` | `http://localhost:11434` | The operator configures a non-local Ollama-compatible endpoint. |
| Note-derived generation prompts | `{KIOKU_OLLAMA_URL}/api/generate` | Generation disabled unless configured | The generation capability is enabled, a model is configured, and Ollama is non-local. |
| MCP HTTP requests and responses | Bound Streamable HTTP interface | `127.0.0.1` | The operator binds a non-loopback interface. |
| Crash data | Configured Sentry DSN | Disabled | The operator sets `KIOKU_SENTRY_DSN`. |
| Tool-call counters | Process memory | Disabled | Never; there is no metrics network sink. |
| External BibTeX input | Allowlisted local directories | Disabled | Never; this is a local read. |
| Deprecated GitHub token setting | No current consumer | Unset | Never through a registered current tool. |

## Filesystem boundary

### Implemented

- Vault reads and writes pass through canonical path validation.
- Writes outside the configured vault are denied.
- External reads are disabled unless `KIOKU_ALLOW_EXTERNAL_READS=true` and the canonical source is under `KIOKU_EXTERNAL_READ_ROOTS`.
- Permanent deletion is disabled unless `KIOKU_ALLOW_PERMANENT_DELETE=true`.
- Soft deletion remains available when permanent deletion is disabled.
- Symlink and reparse-point handling is covered by filesystem security tests.
- Frontmatter mutation preserves unknown fields.

### Known gaps

- Operating-system permissions remain the final boundary.
- Two agents editing the same note body can still race; work-session ownership does not serialize arbitrary note-content edits.
- An allowlisted external directory can contain sensitive files. The operator owns that allowlist.

## Streamable HTTP

### Implemented

- `stdio` is the default transport.
- Streamable HTTP binds to `127.0.0.1` by default.
- A non-loopback bind requires `KIOKU_API_KEY` unless `KIOKU_ALLOW_INSECURE_HTTP=true` is explicitly set.
- Present browser `Origin` headers are checked against `KIOKU_HTTP_ALLOWED_ORIGINS`.
- Forwarded headers are accepted only from exact IPs in `KIOKU_HTTP_TRUSTED_PROXIES`.
- Bearer tokens use fixed-time comparison.
- MCP request bodies and POST execution have configurable limits.
- `/health/live` is minimal. `/health/ready` follows protected deployment configuration.

### Known gaps

- The API key is one static shared secret with no users, scopes, expiry, or built-in rotation.
- The unsafe HTTP override deliberately weakens the deployment boundary.
- Kioku is not a multi-tenant authorization service. Use an appropriate gateway for internet-facing or multi-user deployment.

## Optional Obsidian bridge

The bridge plugin is maintained in [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian).

### Implemented

- The bridge binds to loopback.
- Protocol compatibility is negotiated during the authentication handshake.
- The server and plugin maintain canonical protocol fixtures.
- Capabilities separate read, UI navigation, editor mutation, third-party integrations, vault-wide actions, and explicitly allowlisted unsafe commands.
- Payload size, connection count, request rate, concurrency, execution time, replay, and heartbeat behavior are bounded.
- Error responses are sanitized.

### Known gaps

- Loopback is not authentication; configure `KIOKU_BRIDGE_TOKEN`.
- A process running as the same desktop user can bypass the bridge and access the vault directly.
- Explicitly allowlisted unsafe commands and third-party plugin APIs can have effects Kioku cannot classify.
- Plugin implementation and release behavior must be verified in its own repository.

## Ollama

### Implemented

- The default endpoint is loopback.
- Keyword search remains available when Ollama is unavailable.
- Semantic search uses note-derived chunks for embeddings.
- Generation is disabled unless the capability group and `KIOKU_GEN_MODEL` are configured.
- The embeddings cache stores derived vectors and metadata rather than complete note bodies.

### Known gaps

- `KIOKU_OLLAMA_URL` is not restricted to loopback or HTTPS.
- A remote endpoint receives note-derived text.
- Model privacy, retention, and transport security are the operator's responsibility when the endpoint is not local.

## Sentry

### Implemented

- Sentry is disabled when `KIOKU_SENTRY_DSN` is unset.
- Default PII sending, tracing, profiling, and automatic session tracking are disabled by configuration.
- No current tool intentionally sends note content to Sentry.

### Unconfirmed

- Exception messages can include contextual values such as paths or arguments depending on the throw site.
- The exact runtime interaction between ASP.NET Core Sentry integration and the repository's logging-provider reset requires an integration test against a controlled endpoint.
- Until that behavior is verified, treat enabled Sentry as capable of receiving crash data and possibly error-level logging context.

## Prompt injection from vault content

### Implemented

- Vault content does not bypass tool validation, filesystem policy, HTTP authentication, or bridge authorization.
- Destructive and open-world behavior is represented in MCP annotations.
- Higher-risk capability groups are disabled by default.
- Dry-run or preview modes are available for supported bulk operations.

### Residual risk

An MCP client can still choose to follow malicious instructions found in a note and then call a permitted tool. Kioku constrains the effects of that call; it cannot guarantee the model ignores the instruction.

Agents should treat note content as data, inspect proposed mutations, and require human review for high-impact actions.

## Destructive operations

- Soft delete is the default.
- Permanent deletion requires explicit opt-in.
- Bulk organization and link changes should be previewed before applying.
- Native Git remains the recovery mechanism for repository history; Kioku does not emulate Git operations.
- Bridge unsafe commands require explicit configuration.

## Concurrent agents

### Implemented

- Work sessions have stable identity and ownership metadata.
- Session lifecycle operations are concurrency-safe.
- One agent cannot implicitly close another agent's session.
- Session and project-document workflows use collision-safe file creation and synchronized index updates.

### Known gap

Concurrent edits to the same note body are not a transactional merge system. Coordinate agents at the project/session level and review changes through Git when multiple writers target the same file.

### Partial durable coordination implementation

The durable coordination architecture is documented in
[durable-coordination.md](durable-coordination.md). Event persistence, claims,
leases, fencing, and the guarded vault-mutation boundary are implemented.
Core single-resource write tools expose optional revision, hash, claim, fence,
and mutation-id preconditions. Public coordination tools remain planned.

The implementation adds a private `.kioku/coordination/` event log for machine
coordination state. It does not copy note bodies and does not make `agent`,
`client_name`, or caller-provided authority claims into security principals.
The event log records identifiers, canonical resource references, server
timestamps, bounded reasons, and transition outcomes. It remains outside note
indexing and embeddings, but the same operating-system account can read it and
any backup that contains it.

Implemented controls include exclusive immutable event creation, atomic
projection writes, schema and content-hash validation, deterministic replay,
idempotent duplicate handling, sequence and hash-chain checks, and
vault-boundary validation. Claims add hashed resource locks, bounded
server-time leases, takeover fencing, and fail-closed lease/history
reconciliation. A missing projection can be rebuilt; corrupt event history or
claim state fails closed without deleting the original files.

The remaining threat-model boundaries are:

- callers must supply an expected revision or hash when they need manual
  Obsidian edits to produce a conflict instead of being overwritten by a
  legacy unconditional write;
- network filesystems, cloud-sync replicas, and independent Git checkouts are
  unsupported for shared coordination;
- unsupported restore epochs require explicit recovery before claim-protected
  writes resume.

These controls do not protect against a same-user process that edits the vault
directly, merge arbitrary note bodies, or provide multi-tenant authorization.

## Deployment checklist

1. Run Kioku as an unprivileged operating-system user.
2. Grant access only to the intended vault and allowlisted external-read roots.
3. Keep permanent deletion and optional capability groups disabled unless required.
4. Use `KIOKU_API_KEY` for non-loopback HTTP.
5. Configure exact origins and trusted proxies.
6. Configure `KIOKU_BRIDGE_TOKEN` before enabling bridge capabilities.
7. Confirm `KIOKU_OLLAMA_URL` and `KIOKU_SENTRY_DSN` point to intended hosts.
8. Remove secrets and private paths from logs before sharing diagnostics.
9. Verify server/plugin protocol compatibility using versioned fixtures and the [versioning reference](versioning.md).
