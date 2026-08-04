# Coordination observability

Kioku exposes operational signals for the durable coordination profile without
making telemetry part of the coordination source of truth. The event-log
authority and durable identifiers are defined in the [durable coordination
profile](durable-coordination.md); trace identifiers remain diagnostic only.

<!-- prettier-ignore -->
> [!NOTE]
> Coordination remains gated and disabled by default. Enabling observability
> does not enable the coordination capability group or send data to a network
> sink.

## Correlation model

Coordination operations correlate signals with bounded domain identifiers when
those identifiers are available. These identifiers are created and validated
by the coordination contract; a trace identifier is only a diagnostic context.

| Signal | Correlation fields | Persistence and destination |
|---|---|---|
| Structured logs | `run_id`, `work_item_id`, `attempt_id`, `session_id`, `claim_id`, event type, sequence, disposition, and safe error code | Process logging, normally stderr; not part of the coordination event log |
| Metrics | Fixed operation and outcome categories | Bounded in-memory counters; reset when the process exits |
| Activities | The same domain identifiers plus the profile identifier and version | In-process `ActivitySource`; exported only by an explicitly configured host listener |
| Sentry crash events | SDK release and filtered exception metadata | The configured Sentry DSN only when the operator opts in |

Domain identifiers are diagnostic correlation values, not security
principals. The server does not accept a `trace_id` as a run identity, claim
owner, fence, revision, or authorization decision.

## Bounded metrics

Metrics use fixed names and fixed outcome buckets. They do not include note
content, vault paths, raw resource keys, handoff payloads, authority scopes, or
unbounded caller text.

| Metric family | Recorded values |
|---|---|
| `coordination.transitions.*` | Total transitions and known state-event buckets such as `created`, `claimed`, `blocked`, `completed`, `failed`, and `stale` |
| `coordination.replay.*` | Total replay operations and buckets for success, duplicates, corrupt history, invalid sequence, access denial, and unsupported schema |
| `coordination.claims.*` | Total claim operations and buckets for acquisition, renewal, release, expiry, takeover, contention, fencing, and ownership failures |
| `coordination.mutations.*` | Total guarded mutations and buckets for commits, conflicts, stale fences, access denial, and cancellation |
| `coordination.recovery.*` | Successful and failed recovery counts, total recovery duration, and maximum recovery duration |

`get_server_status` reports whether metrics are enabled and the total tool-call
count. The in-process coordination snapshot is available to the hosting
application; it is not a public note or coordination resource.

## Optional activities

Kioku uses the `Kioku.Coordination` `ActivitySource` for internal W3C-compatible
activities. Activities cover event append and replay, work-item transitions,
claim operations, and guarded vault mutations. `KIOKU_ENABLE_TRACING=true`
permits activity creation, but activities produce no output unless the host
registers an `ActivityListener` or another compatible consumer.

Kioku does not configure an OpenTelemetry SDK, exporter, collector, or remote
trace endpoint. A host that adds one owns its endpoint, retention, access
control, and privacy review. The host must preserve the same field restrictions
when exporting activities.

## Sentry filtering

Sentry is an independent, opt-in crash sink. When `KIOKU_SENTRY_DSN` is set,
Kioku keeps PII sending, tracing, profiling, and automatic session tracking
disabled. The `before_send` filter removes the server name and replaces captured
exception values with `redacted` while removing captured stack traces.

This filter does not make arbitrary application exception messages safe for
external sharing. Treat enabled Sentry as an external crash-data destination,
review the DSN owner and retention policy, and remove private paths or secrets
from diagnostic reports.

## Configuration

All three settings are disabled by default and are independent of the vault's
`coordination` capability group.

| Setting | Effect |
|---|---|
| `KIOKU_ENABLE_METRICS=true` | Enables in-memory tool-call and coordination counters |
| `KIOKU_ENABLE_TRACING=true` | Enables W3C-compatible coordination activities for a host listener |
| `KIOKU_SENTRY_DSN` | Enables opt-in crash reporting with Kioku's filtering options |

`get_server_capabilities` reports the active metrics and tracing state,
transport, profile version, supported coordination features, and rollout gate.
Use that document for machine negotiation instead of parsing log text or
`get_server_status` prose.

## Transport behavior

The observability behavior is the same for `stdio` and Streamable HTTP. Kioku's
console provider sends diagnostics to stderr, so stdout remains exclusively
available for MCP protocol traffic under `stdio`. Streamable HTTP clients can
use the same capability document and do not receive a different coordination
contract based on transport.

## Privacy boundary

Coordination telemetry follows the trust, data-flow, and residual-risk controls
in the [threat and privacy model](threat-and-privacy-model.md). It contains no
note bodies, handoff payloads, canonical paths, raw resource keys, tokens,
authority scopes, or sensitive conflict details.

See [the threat and privacy model](threat-and-privacy-model.md) and [the rollout
policy](coordination-rollout.md) for external data-flow and release controls.
