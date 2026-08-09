# Security Policy

## Supported versions

Kioku security maintenance follows the current stable server release rather than keeping multiple historical release lines active indefinitely.

| Release line | Security support |
|---|---|
| Latest stable Kioku server release | ✅ Supported |
| Older server releases | ❌ Upgrade to the latest stable release |

The current published version is shown in the root [README](README.md) and on the [GitHub Releases](https://github.com/sandovaldavid/kioku/releases) page. The Obsidian plugin is released independently from [`sandovaldavid/kioku-obsidian`](https://github.com/sandovaldavid/kioku-obsidian) and has its own version lifecycle.

## Reporting a vulnerability

We take security vulnerabilities seriously. Do not open a public issue for an undisclosed vulnerability.

### How to report

**Email:** Send details to [security@sandovaldavid.com](mailto:security@sandovaldavid.com)

**GitHub:** Use [GitHub Security Advisories](https://github.com/sandovaldavid/kioku/security/advisories/new)

Please include:

- description of the vulnerability;
- affected Kioku version/commit and transport;
- minimal steps to reproduce;
- potential impact;
- suggested fix or mitigation when available;
- sanitized logs or fixtures that do not expose vault contents, credentials, or private paths.

### What to expect

- **Acknowledgment:** within 48 hours;
- **Initial assessment:** within 1 week;
- **Regular updates:** at least weekly until resolved;
- **Public disclosure:** coordinated after a fix or mitigation is available.

## Current security boundaries

The detailed, branch-current model is maintained in [Threat and privacy model](docs/threat-and-privacy-model.md). Exact HTTP deployment controls are documented in [Streamable HTTP security](docs/deploy/auth-options.md). Those documents take precedence over abbreviated examples here.

### Filesystem and vault safety

- Vault reads and writes use canonical path validation and guarded filesystem boundaries.
- Writes outside the configured vault are denied.
- External reads are disabled unless explicitly enabled and constrained to configured roots.
- Permanent deletion is disabled unless explicitly enabled; soft delete remains the normal deletion path.
- Unknown YAML frontmatter fields are preserved by supported structured mutations.
- Symlink/reparse-point and path-traversal cases are covered by filesystem security tests.
- Guarded writes can use expected revisions/hashes, mutation IDs, and optional coordination claims/fencing when the caller needs conflict detection.

Kioku runs with the permissions of the operating-system account that launches it. A malicious process already running as the same user can bypass Kioku and access the vault directly; Kioku is not an OS sandbox.

### Streamable HTTP

- `stdio` is the default transport.
- Streamable HTTP binds to `127.0.0.1` by default.
- A non-loopback listener requires `KIOKU_API_KEY` unless the operator deliberately enables `KIOKU_ALLOW_INSECURE_HTTP=true`.
- Present browser `Origin` headers are checked against an exact allowlist.
- Forwarded headers are trusted only from explicitly configured proxy IP addresses.
- Bearer tokens use fixed-time comparison.
- Request bodies and MCP POST execution have configurable limits.
- `/health/live` is minimal; `/health/ready` follows the protected deployment configuration.

Kioku does **not** claim to terminate TLS itself for every deployment. Keep the server on loopback when possible and terminate TLS at a trusted reverse proxy, private tunnel, or mesh VPN when traffic leaves the local machine. Never send the bearer token over untrusted plaintext HTTP.

`KIOKU_API_KEY` is one shared secret, not a user/role/scope system. Internet-facing or multi-tenant deployments require an appropriate authorization gateway.

### Optional Obsidian bridge

The companion bridge is separate from the core headless server.

- The bridge binds to loopback.
- Configure `KIOKU_BRIDGE_TOKEN`; loopback alone is not authentication.
- Protocol compatibility and capabilities are negotiated during the bridge authentication handshake.
- Payload size, connection count, request rate, concurrency, execution time, replay, and heartbeat behavior are bounded.
- Explicitly allowlisted unsafe commands or third-party plugin APIs can have effects Kioku cannot classify; enable them only after review.

The plugin implementation and its release behavior must be verified in the plugin repository rather than inferred from the server version.

### Optional external services

- Ollama defaults to a local endpoint. A non-local `KIOKU_OLLAMA_URL` can receive note-derived text for embeddings or generation.
- Generation is disabled unless its capability group and model are configured.
- Sentry is disabled unless `KIOKU_SENTRY_DSN` is configured; enabling it introduces an external crash-data flow documented in the threat model.
- Coordination metrics are in-memory by default; tracing requires explicit enablement and a host-configured listener/exporter.

Review privacy and retention policies before pointing Kioku at any remote Ollama-compatible service, telemetry sink, proxy, or gateway.

## Dependency and release security evidence

Repository CI currently provides these security/quality controls:

- .NET vulnerability auditing with `dotnet list package --vulnerable --include-transitive`;
- dependency review for applicable pull requests targeting `main`;
- repository-wide Roslyn/.NET analyzers with warnings-as-errors;
- CodeQL coverage for repository JavaScript/TypeScript tooling;
- generated MCP contract and portable-integration validation;
- native Ubuntu, macOS, and Windows tests;
- installed-tool `stdio` and native Streamable HTTP package smoke tests;
- complete .NET package inventories uploaded as CI artifacts for the configured retention period.

Do not assume a release artifact is signed or accompanied by an SPDX/CycloneDX SBOM unless that specific release actually publishes and documents those artifacts. The current repository policy does not use absent signatures/SBOMs as claimed release guarantees.

See [CI quality and release gates](docs/ci-quality-gates.md) for the maintained evidence contract.

## Deployment checklist

1. Run Kioku as an unprivileged operating-system user.
2. Grant that account access only to the intended vault and reviewed external-read roots.
3. Keep permanent deletion and optional high-risk capability groups disabled unless required.
4. Prefer local `stdio` for desktop/CLI clients when remote transport is unnecessary.
5. Keep Streamable HTTP on loopback when possible; require a strong API key for non-loopback deployments.
6. Use TLS through a trusted reverse proxy/private tunnel/VPN whenever HTTP traffic leaves the local machine.
7. Configure exact browser origins and trusted proxy IPs.
8. Configure `KIOKU_BRIDGE_TOKEN` before enabling bridge capabilities.
9. Verify remote Ollama/Sentry/gateway destinations before allowing note-derived data to leave the machine.
10. Remove secrets, vault content, and private paths from logs and reproduction material before sharing them.

## Security updates

Security fixes use the SemVer level required by the compatibility impact of the fix. Consumers should upgrade to the latest stable Kioku server release instead of relying on an old release line receiving backports.

## Responsible disclosure

We appreciate responsible disclosure and will acknowledge contributors unless they prefer anonymity.
