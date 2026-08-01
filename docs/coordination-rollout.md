# Coordination rollout policy

The durable coordination profile is implemented as an optional, local-first
capability. It remains gated until reliability evidence, package behavior,
representative client behavior, and privacy controls are verified together.

<!-- prettier-ignore -->
> [!IMPORTANT]
> The coordination capability group is disabled by default. Do not enable it
> as a default profile or treat a successful package build as proof of the
> cross-process filesystem guarantee.

## Capability contract

Clients use `get_server_capabilities` as the stable machine-readable contract.
The current contract is:

| Field | Current value |
|---|---|
| `profile_id` | `kioku.durable-coordination` |
| `profile_version` | `1` |
| `schema_version` | `1` |
| Coordination feature IDs | `coordination.core`, `coordination.history`, `coordination.claims`, `coordination.fencing`, `coordination.cas`, and `coordination.conflicts` |
| Rollout status | `gated` |
| Default enabled | `false` |
| Supported transports | `stdio` and `http` |

The response also reports whether the `coordination` capability group is
enabled, the server transport, metrics and tracing state, and the compatibility
policy. Clients must not parse prose from status or log output to negotiate the
profile.

## Compatibility policy

The profile uses additive evolution within a version and explicit refusal for
unsupported versions.

| Change | Policy |
|---|---|
| Add an optional response field or capability | Compatible; clients ignore unknown fields and capabilities |
| Change an existing field's meaning or requiredness | Breaking; increment `profile_version` or `schema_version` as appropriate |
| Receive an unsupported profile version | Disable coordination writes and report the profile as unavailable |
| Downgrade to a server without the requested profile | Keep the ordinary Kioku surface; coordination is read-only unavailable |
| Deprecate a field or capability | Keep it through the documented profile-version window before removal |

The server package version and coordination profile version are separate. A
package upgrade can add a profile field without changing the profile version;
a semantic contract change must not be hidden behind a package patch release.

## Enablement gate

The following controls must be verified before the profile enters a default
capability profile:

1. Run the full reliability suite for event persistence, replay, corruption,
   claims, leases, expiry, fencing, compare-and-swap writes, restore behavior,
   concurrency, and supported filesystem boundaries.
2. Run the server smoke test against both `stdio` and Streamable HTTP, with the
   coordination capability disabled and explicitly enabled.
3. Verify a release binary or package, not only an in-tree `dotnet run`, with a
   representative MCP client.
4. Verify `tools/list`, `get_server_status`, and `get_server_capabilities` for
   both capability states and both transports.
5. Regenerate and check the public command, configuration, version, and MCP
   manifest contracts.
6. Review logs, optional traces, Sentry filtering, and documentation against
   the privacy boundary in [coordination observability](coordination-observability.md).
7. Verify the support boundary on each supported local filesystem and reject
   network replicas, cloud-sync folders, and independent shared checkouts.
8. Record package, release, platform, and client evidence before changing the
   default capability profile.

Until every gate is satisfied, set `coordination` in the vault capability
configuration only for an explicitly reviewed deployment. A rollout decision
must not be based on telemetry or an external protocol adapter.

## Transport and package behavior

`stdio` and Streamable HTTP expose the same capability IDs, profile versions,
schemas, tools, and resources. Only the transport field and authentication
boundary differ. `stdio` keeps stdout reserved for MCP traffic; diagnostic
logs go to stderr.

The package smoke test checks that `get_server_capabilities` exists, reports
profile and schema version `1`, and matches the requested coordination state.
It also exercises the ordinary legacy session and note paths so enabling the
profile does not remove compatibility behavior.

## Local-first support boundary

Kioku supports coordination between processes that share one supported local
filesystem and one vault boundary. The event log and coordination projections
must be restored together, and recovery establishes a new coordination epoch
when required.

The following are not shared-coordination implementations:

- Dropbox, iCloud, Syncthing, or similar concurrent replica folders;
- independent Git checkouts of the same vault;
- a hosted lock service or cloud coordinator;
- MCP Tasks, A2A, or CloudEvents as a replacement for the local event log.

Use [coordination interoperability](coordination-interoperability.md) for
optional, lossy external mappings and [the durable coordination profile](durable-coordination.md)
for the filesystem and recovery contract.

## Release checklist

Before a release changes the default capability profile, the release owner
must attach:

- the exact server package and release-binary versions tested;
- the supported operating systems and filesystem types tested;
- the `stdio` and Streamable HTTP smoke-test results;
- the representative client and `tools/list` capability results;
- the generated documentation and manifest check results;
- the privacy review for any host-configured telemetry exporter; and
- the migration or deprecation notes for any profile or schema change.

If any evidence is missing, keep the profile gated and document the missing
gate rather than treating it as a successful rollout.
