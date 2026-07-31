# Coordination interoperability

Kioku keeps its durable coordination event log, claims, fencing, compare-and-
swap checks, and conflict records as the normative local model. External
protocols can describe or transport a coordination view, but they must not
replace that model or become necessary to recover state.

## MCP Tasks

The current MCP Tasks extension is documented in the [official Tasks overview](https://modelcontextprotocol.io/extensions/tasks/overview).
The repository currently pins `ModelContextProtocol` and
`ModelContextProtocol.AspNetCore` to version `1.4.1`.

The current Kioku surface remains focused tools and read-only resources. Kioku
does not advertise or require the MCP Tasks extension, and it does not add a
Tasks adapter in this rollout. The capability document is the Kioku-specific
negotiation mechanism because the durable profile has semantics that a generic
task lifecycle does not represent, including immutable history, claims, lease
expiry, fencing, compare-and-swap preconditions, and manual-edit conflicts.

The following table is an intentionally lossy diagnostic mapping. A client
must not use it to rebuild the Kioku event log or infer claim ownership.

| Kioku state | MCP Tasks view | Loss or restriction |
|---|---|---|
| `pending` | `working` as a best-effort queued view | MCP Tasks has no equivalent queued state in this mapping; the distinction between queued and actively executing is lost |
| `claimed` | `working` | Claim owner, lease expiry, and fence generation are not represented |
| `running` | `working` | Attempt identity and transition history are not represented |
| `blocked` | `input_required` only when the blocker requires client input | Operational dependency blocks have no lossless Tasks status and must remain Kioku state |
| `partial` | No lossless equivalent | Keep the Kioku state; mapping to `failed` or `completed` changes retry semantics |
| `failed` | `failed` | Kioku error codes, retry policy, and history are not represented |
| `completed` | `completed` | Result references and durable event history are not represented |
| `canceled` | `cancelled` | Kioku cancellation actor and idempotency history are not represented |
| `stale` | No lossless equivalent | Mapping to `failed` loses the recoverable lease-expiry meaning |

Claims, fences, resource canonicalization, state versions, mutation IDs, and
conflict resolution have no equivalent in the basic Tasks state view. A future
adapter must retain the Kioku identifiers and query the Kioku tools or
resources for those details.

## A2A mapping

A future A2A integration can provide a non-normative view for communication
between independent remote agents. It must be treated as a message or task
projection, not as shared coordination authority.

| Kioku concept | Future A2A view | Boundary |
|---|---|---|
| `run_id` and `work_item_id` | Remote task context or correlation metadata | The remote task ID cannot replace the Kioku IDs |
| Handoff packet | Task message or artifact reference | The adapter must omit note content and private vault paths unless separately authorized |
| `pending`, `claimed`, and `running` | Submitted or working lifecycle view | Claims, leases, and fences remain local |
| `blocked` | Input-required message when a remote agent must respond | Dependency blocks that do not require remote input remain Kioku-only |
| `completed`, `failed`, and `canceled` | Corresponding terminal task outcome | Kioku history and retry semantics remain authoritative |

This mapping does not make A2A a Kioku dependency. Kioku does not add a remote
agent directory, remote authorization model, or cross-vault lock protocol.

## CloudEvents

CloudEvents is suitable for a future optional publication adapter when an
operator needs to notify external consumers. It is not required for internal
persistence, replay, recovery, or conflict handling.

A future adapter may publish a contract-safe envelope with fields such as the
event ID, event type, profile version, recorded time, and validated domain
identifiers. It must apply the same privacy boundary as local logs and traces:
no note bodies, handoff payloads, canonical paths, raw resource keys, tokens,
authority scopes, or sensitive conflict details. External delivery failure must
not prevent local event durability, and an external consumer must not be able
to rewrite local history.

CloudEvents therefore remains an integration choice, not a persistence format
or a recovery dependency. Kioku does not add Better Agent as a dependency or
normative protocol.

## Compatibility rule

Clients must call `get_server_capabilities` before using optional coordination
features. They must ignore unknown additive capability fields, disable writes
when the profile or schema version is unsupported, and continue to use the
ordinary Kioku surface when coordination is gated. See [the rollout policy](coordination-rollout.md)
for the version and downgrade rules.
