# ADR-0004: Support both stdio and Streamable HTTP transports

## Status

Accepted (implemented; documented in `docs/configuration-reference.md` and
`docs/threat-and-privacy-model.md`).

## Context

Kioku serves two different deployment shapes. The common case is a single local AI client
(Claude Code, Codex, and similar) spawning Kioku as a child process against one vault on the same
machine — no network involved. The other case is a longer-running, remote-reachable, or
containerized deployment (see `docs/docker.md`, `docs/dev-container.md`) where a persistent,
network-addressable server is needed instead of a per-client child process, potentially serving
more than one client against the same vault.

## Decision

Kioku supports both transports behind one switch, `KIOKU_TRANSPORT` (default `stdio`, or `http`
for Streamable HTTP). stdio needs no network configuration: the client spawns the process and
talks over stdin/stdout. Streamable HTTP (`src/Kioku.Mcp.Server/Http/`) binds to `127.0.0.1` by
default and layers `OriginValidationMiddleware`, CORS, `ApiKeyMiddleware` (bearer token, fixed-time
comparison), and `McpRequestLimitsMiddleware`. `KiokuConfiguration.ValidateHttpTransport()`
refuses to start a non-loopback, unauthenticated listener unless an operator explicitly sets
`KIOKU_ALLOW_INSECURE_HTTP=true`, which also logs a prominent startup warning.

## Alternatives rejected

**HTTP-only.** This would force network configuration, port management, and authentication setup
onto the common case — one desktop AI client and one local vault — where stdio needs none of that
and has no listening socket to secure at all.

**stdio-only.** This can't serve the deployment scenarios Streamable HTTP exists for:
containerized or remote hosting, and any setup where more than one client needs to reach the same
running server over a network rather than each spawning its own process.

## Consequences

- Two authorization models to maintain: none for stdio, versus bearer-token, origin allowlisting,
  CORS, and trusted-proxy configuration for HTTP — documented as a defense-in-depth stack in
  `docs/threat-and-privacy-model.md`'s "Streamable HTTP exposure" section specifically because HTTP
  is the transport reachable by something other than the trusted local client.
- Filesystem-boundary and other authorization rules apply identically to both transports, so
  transport choice doesn't change what a tool call is allowed to do — only how a client reaches
  the server.
- `docs/deploy/auth-options.md` carries the full HTTP deployment and key-management guidance,
  kept separate from this decision record.
