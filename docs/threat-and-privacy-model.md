# Threat and privacy model

This document is Kioku's unified security and privacy reference. It replaces the former
`filesystem-security.md` and `obsidian-bridge-threat-model.md`, and adds coverage for data
flows, Ollama, Sentry, API keys and bridge tokens, prompt injection, destructive tools, and
concurrent agents.

Two other docs remain authoritative for their own areas and are not duplicated here:

- [`docs/deploy/auth-options.md`](deploy/auth-options.md) — full Streamable HTTP configuration
  and deployment recipes.
- [`docs/commands-reference.md`](commands-reference.md) — the generated, authoritative list of
  every tool and its `readOnly`/`destructive`/`idempotent`/`openWorld` annotations.

Every section below separates **Implemented** (what Kioku does today, verified against the
current code) from **Future work / known gaps** (what is not mitigated, or not yet built).

## Trust assumptions

Kioku assumes the local machine and its operating-system user account are trustworthy. It does
not attempt to defend against a fully compromised OS, a malicious process already running as the
same desktop user, or a malicious Obsidian plugin with full UI-level API access. Any of those can
read or write the vault directly, independently of Kioku.

Within that assumption, Kioku defends against:

- accidental or malicious tool arguments that try to read, write, or delete outside the
  configured vault (path traversal, symlink escapes, external writes);
- network-adjacent clients when the Streamable HTTP transport is exposed beyond loopback;
- untrusted vault content that an AI agent reads and that may contain manipulative instructions
  (prompt injection) — bounded, not eliminated, see [Prompt injection](#prompt-injection-from-vault-content);
- accidental misconfiguration that would otherwise silently widen the trust boundary (for
  example, binding HTTP non-loopback without authentication, or enabling permanent delete).

It does not assume any particular MCP client is well-behaved: tool arguments supplied by an AI
model are treated as untrusted input at every filesystem and bridge boundary, regardless of the
model's stated intent.

## Assets

- Notes, attachments, and frontmatter in the configured Obsidian vault.
- Work session files and their frontmatter (`session_id`, agent, project, timestamps).
- The local embeddings cache (`{KIOKU_VAULT_PATH}/.kioku/embeddings.bin`).
- Editor state exposed through the Obsidian bridge (open notes, selection, cursor, active pane).
- Commands registered by Obsidian and third-party plugins, reachable through the bridge.
- Secrets: `KIOKU_BRIDGE_TOKEN`, `KIOKU_API_KEY`, `KIOKU_SENTRY_DSN`, `KIOKU_GITHUB_TOKEN`.
- Availability of the Kioku process, the Obsidian plugin process, and Ollama.

## Local vs. external data flows

Kioku's default configuration keeps all vault data on the local machine. Data can leave the
machine only through the specific, named paths below — never implicitly.

| Data | Destination | Default | Leaves the machine when |
| --- | --- | --- | --- |
| Note text used to build an embedding | `{KIOKU_OLLAMA_URL}/api/embeddings` | `http://localhost:11434` (loopback) | An operator sets `KIOKU_OLLAMA_URL` to a non-local host. |
| Note text used as a generation prompt (`summarize_note`, etc.) | `{KIOKU_OLLAMA_URL}/api/generate` | Disabled unless `KIOKU_GEN_MODEL` is set; same host as above | Same as above, and only for the `generation` tool group, which is disabled by default. |
| Crash reports (exception details, stack traces) | The configured Sentry DSN host | Disabled (`KIOKU_SENTRY_DSN` unset) | An operator explicitly sets `KIOKU_SENTRY_DSN`. |
| Tool-call counts (tool name only, never note content) | Nowhere — kept in server memory | Disabled (`KIOKU_ENABLE_METRICS=false`) | Never; `MetricsService` has no network sink. It only counts, and stays in-process. |
| BibTeX imports from an allowlisted external directory | Read only, stays local | Disabled (`KIOKU_ALLOW_EXTERNAL_READS=false`) | Never leaves the machine; this is a local filesystem read, not a network call. |
| Streamable HTTP request/response bodies (tool calls, note content) | Whatever host `KIOKU_HTTP_HOST` binds to | `127.0.0.1` (loopback) | An operator binds `KIOKU_HTTP_HOST` to a non-loopback address, which requires `KIOKU_API_KEY` or `KIOKU_ALLOW_INSECURE_HTTP=true`. |

`KIOKU_OLLAMA_URL` has no allowlist or loopback restriction analogous to
`KIOKU_HTTP_ALLOWED_ORIGINS` — see [Future work](#future-work--known-gaps).

### Ollama

Ollama is the only component that receives note-derived text as part of Kioku's normal
operation, and only when semantic search or local generation is used.

Implemented:

- Default endpoint is `http://localhost:11434`, configured by `KIOKU_OLLAMA_URL`. This is
  loopback by default, so under default configuration no note text leaves the machine.
- `EmbeddingService` sends note text (extracted by `MarkdownTextExtractor`, one request per
  chunk) as the JSON `prompt` field of a `POST {KIOKU_OLLAMA_URL}/api/embeddings` request. This
  happens during vault indexing and for `search_notes` with `mode='semantic'` or `'hybrid'`.
- `GenerationService` sends a prompt built from note content, truncated to about 4000
  characters, to `POST {KIOKU_OLLAMA_URL}/api/generate`. This only runs for tools in the
  `generation` capability group, which is disabled by default, and only when `KIOKU_GEN_MODEL`
  is configured.
- If Ollama is unreachable at startup, both services degrade gracefully: keyword search keeps
  working, and semantic/generation features report `[error] [DEPENDENCY_UNAVAILABLE]`.
- The local embeddings cache at `{KIOKU_VAULT_PATH}/.kioku/embeddings.bin` stores vault-relative
  paths, content hashes, section heading text, and embedding vectors. It does not store full note
  bodies. This file is a local performance cache; nothing about writing it sends data anywhere.

Future work / known gaps:

- Kioku does not validate or restrict `KIOKU_OLLAMA_URL` to loopback or a private network. If an
  operator points it at a remote or cloud-hosted Ollama-compatible endpoint, note text and
  generation prompts are sent to that host over whatever scheme the URL specifies (plaintext HTTP
  included, if configured that way). This is the single most consequential misconfiguration for
  privacy, and Kioku does not warn about it at startup.

## Sentry (opt-in crash reporting)

Sentry is disabled by default and only activates when an operator supplies a DSN.

Implemented:

- `KIOKU_SENTRY_DSN` defaults to `null`/empty. `ConfigureSentry` (stdio transport) and the HTTP
  transport's `UseSentry` call both no-op with `string.IsNullOrWhiteSpace(config.SentryDsn)`, so
  no Sentry SDK initialization, and no network contact with Sentry, happens unless this variable
  is explicitly set.
- When enabled, `Program.cs` configures the SDK with `SendDefaultPii = false` (no user IP,
  cookies, or request headers by default), `TracesSampleRate = 0.0` and `ProfilesSampleRate =
  0.0` (no performance tracing or profiling payloads), and `AutoSessionTracking = false` (no
  session/release-health telemetry).
- No code in this repository calls `SentrySdk.CaptureException` or `CaptureMessage` directly.
  What reaches Sentry, if anything, comes from the SDK's own automatic unhandled-exception
  capture (`AppDomain.UnhandledException` for stdio, ASP.NET Core's exception middleware for
  HTTP), not from an explicit "send this note" code path.
- Startup logs never print the DSN, API key, or bridge token values — only booleans (for
  example, `Auth: Bearer token enabled` vs. `disabled`).

Future work / known gaps (flagged as needs-verification, not asserted as fact):

- Exception messages can incidentally contain contextual strings — a file path, a tool argument
  fragment — depending on where in the codebase an exception originates. No systematic audit of
  every throw site has been done to guarantee exception messages never contain vault-relative
  paths or fragments of tool arguments. Stack traces themselves only contain source file/line
  information from the compiled assembly, not user data.
- Whether the ASP.NET Core Sentry logging integration (which can turn `ILogger` `Error`-level
  calls into Sentry events) survives the `ConfigureLogging(builder.Logging)` call that runs after
  `builder.WebHost.UseSentry(...)` in `RunHttpAsync`, and which calls `logging.ClearProviders()`,
  was not resolved with confidence from reading `Program.cs` alone. If the Sentry logging
  provider survives, structured log arguments passed to `logger.Error(...)` calls throughout the
  codebase could reach Sentry as breadcrumbs or events even without an unhandled exception. This
  needs a runtime test (enable a DSN against a local Sentry-compatible endpoint and inspect what
  arrives) rather than static reading, and is out of scope for this documentation task.
- In short: **when enabled**, Sentry receives exception/crash data and, unverified, possibly
  `Error`-level log messages. It does not receive note content, search queries, or full request
  bodies through any explicit code path found in this repository.

## API keys and bridge tokens

Two independent shared secrets exist. Neither has scopes, expiry, or rotation built in.

Implemented:

- `KIOKU_API_KEY` authenticates the Streamable HTTP transport. `ApiKeyMiddleware` compares the
  `Authorization: Bearer <token>` header against the configured key using a fixed-time,
  SHA-256-hashed comparison (`FixedTimeTokenEquals`) to avoid timing side channels. Only
  `/health/live` is exempt; `/health/ready` and `/mcp` both require the token when one is
  configured.
- `KiokuConfiguration.ValidateHttpTransport()` refuses to start an HTTP listener that is both
  non-loopback and unauthenticated: `!IsLoopbackHttpBinding && !HasApiKey && !AllowInsecureHttp`
  throws before Kestrel binds. The only way around this is the explicit
  `KIOKU_ALLOW_INSECURE_HTTP=true` escape hatch, which also logs a prominent startup warning.
  Loopback bindings never require a key.
- `KIOKU_BRIDGE_TOKEN` authenticates the WebSocket handshake between the Kioku server and the
  Obsidian bridge plugin (protocol v3, see [Obsidian bridge](#obsidian-bridge)). If unset, the
  handshake is a no-op — open mode — and the plugin surfaces a security warning in its UI.
- Full deployment guidance (key generation, reverse proxy setup, CORS/origin allowlisting,
  trusted-proxy configuration) lives in
  [`docs/deploy/auth-options.md`](deploy/auth-options.md) and is not duplicated here.

Future work / known gaps:

- Both secrets are static, long-lived, shared-secret tokens: no per-client identity, no scopes,
  no expiry, no built-in rotation. `docs/deploy/auth-options.md` already documents this
  explicitly for `KIOKU_API_KEY` and recommends a standards-based authorization gateway for
  internet-facing or multi-tenant deployments; the same limitation applies to `KIOKU_BRIDGE_TOKEN`.
- `KIOKU_GITHUB_TOKEN` is still accepted by `KiokuConfiguration` and `KiokuOptions`, but no tool
  in the current codebase reads it — the `share_as_gist` tool that used to consume it was removed
  during the v3 migration (see [`docs/migration-v3.md`](migration-v3.md)) specifically because a
  GitHub-scoped token living in the server was judged an unnecessary risk in favor of
  agent-native `gh gist create`. Setting this variable today has no effect. See
  [Documentation and configuration drift noticed during this task](#documentation-and-configuration-drift-noticed-during-this-task).

## Obsidian bridge

The Kioku Obsidian plugin (now released independently from
[`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian)) exposes a
loopback WebSocket bridge so the MCP server can invoke a small set of Obsidian UI and plugin
operations. This section describes the security boundary of bridge protocol v3.

Implemented:

- The bridge binds only to `127.0.0.1`. Loopback is not treated as authentication — any process
  running as the same desktop user can attempt to connect — so the shared `KIOKU_BRIDGE_TOKEN` is
  the actual authentication boundary, not the bind address.
- Every WebSocket connection must send `auth` as its first message: an authenticated capability
  handshake carrying supported protocol version range, optional client identity, requested
  capabilities, and the shared token when configured. Non-overlapping protocol ranges are
  rejected and the connection is closed. Reconnecting creates a new authentication and
  replay-protection scope.
- Capabilities are gated independently: `read`, `ui-navigation`, and `editor-mutation` are
  enabled by default; third-party integrations (Dataview, Templater, per-file Linter),
  `vault-wide` actions, and `unsafe-command` (explicitly listed custom Obsidian command IDs) are
  disabled by default. The generic `trigger-command` endpoint only accepts a small built-in UI
  allowlist unless unsafe mode and an explicit settings allowlist are both enabled.
- Transport controls: a 256 KiB WebSocket payload limit, at most four concurrent clients, per-client
  request rate and concurrency limits, response backpressure limits, heartbeat ping/pong with
  stale-client termination, a ten-second command execution timeout, per-connection request ID
  replay detection, text-only JSON messages, and sanitized error codes/messages (no stack traces
  or host paths in bridge responses). Start/stop/restart are serialized and idempotent.
- AI-generated tool arguments reaching the bridge are treated as untrusted: the bridge validates
  and authorizes every message independently of the calling model's stated intent, so a
  prompt-injected document cannot use the bridge to reach an Obsidian command outside the
  currently enabled capability set.

Future work / known gaps:

- A compromised process running as the desktop user can read Obsidian files directly, bypassing
  Kioku and the bridge entirely — this is a restatement of the [Trust assumptions](#trust-assumptions)
  above, not a bridge-specific gap.
- An explicitly allowlisted unsafe command may perform actions the plugin cannot classify or
  bound; enabling `unsafe-command` opts out of the bridge's own risk model for that command ID.
- Third-party plugin APIs (Dataview, Templater, Linter) are undocumented from Kioku's
  perspective and may change behavior or have side effects the bridge does not control.
- A command that times out (ten seconds) may continue running inside Obsidian if the underlying
  API doesn't support cancellation; Kioku discards the late response but cannot guarantee the
  side effect was actually stopped.
- The bridge token is stored in Obsidian plugin data, not a hardware-backed credential store. It
  protects the socket from unrelated local processes but not from another process running as the
  same OS user with filesystem access to the plugin's data directory.

## Prompt injection from vault content

An AI agent using Kioku reads note content through `read_note`, `search_notes`, and similar
tools. That content is written by whoever authored the note, and Kioku has no way to distinguish
"note content the author intended as information" from "text designed to look like an
instruction to the agent." This is a known MCP-ecosystem risk category, not specific to Kioku.

Implemented mitigations (these bound the blast radius of a successful injection; they do not
prevent the injection attempt itself):

- The filesystem boundary (see [Filesystem boundary](#filesystem-boundary-and-path-traversal))
  applies regardless of why a tool was called. An agent convinced by injected text to try to read
  or write outside the vault still gets `ACCESS_DENIED`.
- Permanent deletion is disabled by default (`KIOKU_ALLOW_PERMANENT_DELETE=false`). An injected
  instruction cannot make Kioku bypass an operator's own configuration.
- Destructive tools are annotated `destructive=true` in the MCP tool schema (see
  [Destructive tools](#destructive-tools)), which is the mechanism MCP clients use to decide
  whether to prompt a human for confirmation before executing a tool call. This confirmation is
  enforced by the client, not by Kioku — see the gap noted below.
- Many mutating tools support `dry_run`, letting a cautious agent or client preview the effect of
  a suggested action before it touches the vault.
- On the Obsidian bridge, tool arguments are explicitly treated as untrusted input (see
  [Obsidian bridge](#obsidian-bridge)), and the riskiest bridge capabilities
  (`unsafe-command`, `vault-wide`, third-party integrations) are disabled by default.

Not mitigated, and understood as a fundamental limitation:

- Kioku does not sanitize, classify, or filter note content for injection attempts before
  returning it to an MCP client. Doing so would require deciding which text is "safe," which is
  not possible without breaking the ability to serve arbitrary user-authored notes verbatim.
- Kioku does not enforce tool-call confirmation itself. Whether a human is asked to approve a
  `destructive=true` tool call before it runs depends entirely on the connecting MCP client's own
  UX (Claude Code, Claude Desktop, and similar clients typically prompt; a fully autonomous or
  misconfigured client would not).
- There is no per-note trust level. All vault content is equally available to the agent — Kioku
  does not distinguish a note the operator wrote from one imported or synced from an external,
  less-trusted source.

## Destructive tools

`docs/commands-reference.md` marks each tool's behavioral annotations, including
`destructive=true`. The following tools currently carry that annotation: `create_folder_readme`,
`create_moc`, `delete_note`, `edit_in_obsidian`, `edit_note`, `manage_css_snippets`,
`manage_trash`, `move_note`, `suggest_links`, `tidy_attachments`, `trigger_obsidian_command`,
`update_frontmatter`, and `lint`. Several of these (`create_folder_readme`, `create_moc`,
`update_frontmatter`) are marked destructive because they can overwrite existing content on
re-run, not because they delete anything.

Implemented, for the highest-impact cases:

- `delete_note` defaults to a soft delete (move to `.trash`). `permanent=true` requires
  `KIOKU_ALLOW_PERMANENT_DELETE=true`; without it, the tool returns `ACCESS_DENIED` and performs
  no filesystem change. `dry_run=true` reports what would happen without modifying the vault.
- `manage_trash` only supports `list` and `restore` actions — there is no "purge" or permanent
  delete reachable through it. Recovering a soft-deleted note never requires re-enabling the
  permanent-delete flag.
- `move_note` validates both source and destination against the filesystem boundary before
  acting, and supports `dry_run`.
- `edit_note` and the bridge's `edit_in_obsidian` mutate content in place but do not bypass the
  filesystem boundary or the bridge's own authorization.
- The MCP `destructive=true` annotation itself is a signal most MCP clients use to gate execution
  behind a user confirmation — see the caveat in [Prompt injection](#prompt-injection-from-vault-content)
  that this confirmation is client-enforced, not server-enforced.

Future work / known gaps:

- Kioku does not implement its own confirmation step independent of the MCP client. A client
  that auto-approves destructive tool calls removes this layer entirely, and Kioku has no way to
  detect that.
- Soft delete only protects note files moved into `.trash` inside the vault; it does not version
  edits made in place (`edit_note`, `update_frontmatter`) — an in-place edit has no built-in undo
  beyond whatever the operator's own backup or version control does. This is consistent with
  Kioku treating Markdown-in-Obsidian as the durable store of record, not as internally versioned.

## Filesystem boundary and path traversal

Kioku treats the configured Obsidian vault as its default filesystem boundary. MCP tools must
not read, write, move, restore, index, or delete files outside that boundary unless a narrowly
scoped external read has been explicitly enabled.

Implemented:

- Given `KIOKU_VAULT_PATH=/home/user/notes`, relative paths are always resolved relative to that
  root, never to the server process's working directory. Absolute paths outside the vault, and
  `..` traversal that would leave the vault, are denied.
- Symbolic links and reparse points are resolved before authorization. A link inside the vault
  that targets an external directory does not extend the vault boundary. This protects against
  symlink-based traversal on Linux/macOS, junctions and reparse points on Windows, nested links
  that ultimately resolve outside the vault, and a source path inside the vault paired with an
  external move destination.
- The indexer and asset scanners skip linked directories during recursive enumeration.
- Both the source and destination are validated before a move, restore, rename, or soft delete.
- These rules apply identically to stdio and Streamable HTTP transports.
- Filesystem authorization failures return a stable `[error] [ACCESS_DENIED]` response and
  intentionally omit the requested absolute path and the configured roots. Operational logs may
  contain administrator-facing diagnostics, but MCP clients never receive host filesystem
  details beyond the vault-relative path they supplied.

External read-only imports (used for file-based BibTeX imports):

- Disabled by default. Raw BibTeX content and `.bib` files already inside the vault work without
  any extra configuration.
- Enabling external reads requires both `KIOKU_ALLOW_EXTERNAL_READS=true` and
  `KIOKU_EXTERNAL_READ_ROOTS=<one or more absolute paths>` (platform path-list separator for
  multiple roots). The allowlist grants read-only access — it never authorizes create, modify,
  move, or delete outside the vault.

Future work / known gaps:

- The filesystem policy is a defense-in-depth boundary inside the Kioku process. Operating-system
  file permissions remain the final control; Kioku cannot stop another process running as the
  same OS user from reading or writing the vault directly.

## Concurrent agents

Multiple MCP clients or AI agents can operate against the same vault or project without
corrupting each other's work sessions.

Implemented (see [`docs/work-sessions.md`](work-sessions.md) for the full behavior):

- Every session has a durable `session_id` (UUIDv7) as its primary identity; filenames and
  modification timestamps are presentation details, not identity.
- Session creation uses exclusive filesystem creation to prevent filename collisions. When two
  concurrent `start_work_session` calls would produce the same historical filename, Kioku adds a
  short identifier suffix rather than overwriting or silently resuming another agent's session.
  `WorkSessionConcurrencyTests.ParallelStarts_ThreeAgents_GetDistinctIdsAndFiles` covers this.
- Resume and close operations are serialized per `session_id` within the server process; writes
  use a temporary file plus atomic replacement.
- Calling `end_work_session` without an explicit `session_id` is conservative: it filters by
  project and by the calling MCP client's identity, and only proceeds when exactly one candidate
  remains. Zero candidates return `NO_ACTIVE_SESSION`; multiple candidates return
  `AMBIGUOUS_SESSION` with actionable candidate records — Kioku never guesses by picking the most
  recently modified note. `WorkSessionConcurrencyTests.ImplicitEnd_WithMultipleAgents_ReturnsCandidatesInsteadOfChoosingLatest`
  and `.ImplicitEnd_WithAgent_ClosesOnlyThatAgentsSession` cover this.
- Different session IDs can be closed concurrently; a second close of an already-completed
  session returns `SESSION_ALREADY_CLOSED` instead of appending duplicate end blocks.
- `parent_session_id` records handoff provenance between agents (for example, Codex continuing
  work Claude Code started) without automatically closing the parent session.
- `WorkSessionService` and its infrastructure port (`IWorkSessionFileSystem`) contain no direct
  `File.*`/`Directory.*` calls outside the dedicated filesystem implementation — see
  [`docs/architecture.md`](architecture.md#session-vertical-slice) for the enforced architecture
  boundary.

Future work / known gaps:

- Concurrency guarantees are scoped to session lifecycle (start/resume/end). They do not
  guarantee serialized, conflict-free concurrent edits to the same note's body by two agents —
  two agents both calling `edit_note` on the same file can still race at the content level; only
  session bookkeeping is protected.

## Streamable HTTP exposure

Streamable HTTP is Kioku's network-reachable transport, used when `KIOKU_TRANSPORT=http`. The
full operational configuration lives in [`docs/deploy/auth-options.md`](deploy/auth-options.md);
this section states the threat-model summary.

Implemented:

- Binds to `127.0.0.1` by default (`KIOKU_HTTP_HOST`); a non-loopback bind without either
  `KIOKU_API_KEY` or `KIOKU_ALLOW_INSECURE_HTTP=true` is refused before Kestrel starts
  (`KiokuConfiguration.ValidateHttpTransport`).
- `OriginValidationMiddleware` validates every present `Origin` header against an exact allowlist
  (`KIOKU_HTTP_ALLOWED_ORIGINS`, defaulting to loopback origins and `app://obsidian.md`) and
  returns HTTP 403 for malformed or disallowed origins, deliberately before and separate from
  CORS — CORS alone only governs browser script access, not DNS-rebinding-style requests. Missing
  `Origin` headers are accepted for non-browser MCP clients.
- `ApiKeyMiddleware` enforces the bearer token with fixed-time comparison (see
  [API keys and bridge tokens](#api-keys-and-bridge-tokens)); only `/health/live` is exempt.
- Forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) are only honored from exact,
  explicitly configured proxy IP addresses (`KIOKU_HTTP_TRUSTED_PROXIES`); the list is empty, and
  forwarding disabled, by default.
- Request bodies are capped (`KIOKU_HTTP_MAX_REQUEST_BODY_BYTES`, default 1 MiB) and MCP POST
  execution is time-limited (`KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS`, default 300 seconds); SSE GET
  connections are not subject to that timeout.
- `/health/live` returns only a minimal public status; `/health/ready` requires the same bearer
  token as `/mcp` and reports index/embedding/generation readiness without paths or secrets.
- The unsafe non-loopback-without-auth override logs a prominent startup warning identifying the
  bound host.

Future work / known gaps:

- `KIOKU_API_KEY` is a static shared secret with no users, scopes, expiry, or delegated
  authorization — acceptable for a trusted personal or small-team deployment, explicitly not
  recommended as-is for an internet-facing or multi-tenant service.
  `docs/deploy/auth-options.md` already names the intended future direction: a standards-based
  authorization gateway today, and an MCP-authorization-specification-compliant multi-user mode
  in Kioku itself later.

## Deployment guidance

- Run Kioku as a dedicated, unprivileged operating-system user.
- Grant that user access only to the vault and intentionally allowlisted external-read roots;
  prefer read-only permissions for those roots.
- Do not expose Streamable HTTP publicly without authentication (`KIOKU_API_KEY`) and a trusted
  reverse proxy; see [`docs/deploy/auth-options.md`](deploy/auth-options.md).
- Keep soft delete enabled and permanent delete disabled (`KIOKU_ALLOW_PERMANENT_DELETE=false`)
  unless there is a documented operational need.
- Review vault symlinks before deployment.
- Keep `KIOKU_OLLAMA_URL` pointed at a local or otherwise trusted host; Kioku does not restrict
  it for you.
- Leave `KIOKU_SENTRY_DSN` and `KIOKU_ENABLE_METRICS` unset unless you have a specific need for
  crash reporting or tool-call counters, and understand what each does and does not send (see
  [Sentry](#sentry-opt-in-crash-reporting) and the metrics row in
  [Local vs. external data flows](#local-vs-external-data-flows)).

The filesystem policy, transport authentication, and bridge authorization are defense-in-depth
boundaries inside the Kioku process. Operating-system permissions remain the final control.

## Review checklist

Before enabling higher-risk configuration:

1. Verify the MCP server binary and Obsidian plugin are the expected, locally built or
   officially distributed versions.
2. Configure and test `KIOKU_BRIDGE_TOKEN` and, for Streamable HTTP, `KIOKU_API_KEY`.
3. Keep bridge third-party and `vault-wide` capabilities, and `KIOKU_ALLOW_PERMANENT_DELETE`,
   disabled unless required.
4. Inspect every additional bridge unsafe-command ID and its plugin source before allowlisting it.
5. Confirm `KIOKU_OLLAMA_URL` and `KIOKU_SENTRY_DSN` (if set) point at hosts you intend note
   content or crash data to reach.
6. Review bridge and server logs for repeated authorization, rate-limit, or protocol failures.

## Documentation and configuration drift noticed during this task

Two items surfaced while researching this document that are worth a maintainer's attention, even
though fixing them is out of scope for this documentation task:

- `docs/configuration-reference.md` still describes `KIOKU_GITHUB_TOKEN` as "GitHub token used by
  the `share_as_gist` tool," but that tool was removed in the v3 migration
  (`docs/migration-v3.md`) and no tool in the current codebase reads this variable. The
  configuration reference entry is stale.
- An unused `"web"` named `HttpClient` is registered in `KiokuHostingExtensions` alongside the
  `"ollama"` client, with no current caller (`grep` for `CreateClient("web")` finds only the
  registration). It is not wired to any tool today, so it introduces no active data flow, but a
  future tool added against that client would not need to touch this threat model's HTTP-client
  wiring to start making outbound requests — worth keeping in mind if this doc is audited again
  after new tools land.

Neither of these is a live vulnerability: both represent configuration or documentation that
outlived the feature it described, not a path that currently sends data anywhere.
