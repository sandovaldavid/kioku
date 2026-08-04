# Coordination contracts

This document describes Kioku's versioned JSON contracts, validation rules, and
their use by the coordination stores and gated MCP surface. The architecture,
storage, filesystem, and work-session boundaries are canonical in the [durable
coordination profile](durable-coordination.md).

## Contract documents

Each top-level document uses `schema_version: 1` and includes a root
`content_hash` field. The reviewed schemas are embedded in the server
assembly and the representative fixtures are copied into the test output.
Coordination tools return these typed documents inside the standard Kioku MCP
result envelope, while coordination resources expose read-only JSON views.

| Contract | Domain purpose | Schema |
|---|---|---|
| Handoff packet | Transfers run, work-item, attempt, session, scope, checkpoint, and safe next-action state. | `handoff-packet.schema.json` |
| Coordination event | Records one immutable state transition in the coordination event log. | `coordination-event.schema.json` |
| Coordination claim | Projects one server-issued lease and fence generation. | `coordination-claim.schema.json` |
| Coordination conflict | Records a safe revision, claim, history, or manual-edit conflict. | `coordination-conflict.schema.json` |
| Work-item projection | Represents rebuildable current state derived from events. | `work-item-projection.schema.json` |

The C# models live under
`src/Kioku.Mcp.Server/Domain/Coordination/`. The JSON schemas live under
`src/Kioku.Mcp.Server/Resources/Coordination/Schemas/`, and the public test
fixtures live under `src/Kioku.Mcp.Server.Tests/Fixtures/Coordination/`.

## Canonical JSON

The server canonicalizes a contract before hashing it. Canonical output is
compact UTF-8 JSON without a byte-order mark or insignificant whitespace.

The canonicalizer applies these rules:

- Sort object property names by ordinal comparison at every object depth.
- Preserve array order because array order is contract data.
- Escape strings through `Utf8JsonWriter`.
- Emit booleans and `null` using their JSON literals.
- Emit integer values using their invariant decimal representation.
- Normalize supported decimal and finite floating-point values using invariant
  formatting.

The canonicalizer does not sort or deduplicate arrays. Callers must provide
stable ordering when a list represents an ordered sequence, and the domain
must define a sorted order before hashing a set-like list.

## Content hashes

Every top-level contract uses SHA-256 over the canonical UTF-8 bytes of the
document after removing only the root `content_hash` property. The resulting
digest is encoded as 64 uppercase hexadecimal characters.

The hash therefore covers unknown extension fields and nested fields. Changing
the serializer's property declaration order cannot change the digest because
property names are sorted before encoding. A reader must verify the hash before
trusting a persisted document; a hash mismatch is a corrupt or stale contract,
not a request to repair the document silently.

Nested `content_hash` fields, when present, remain part of the parent document's
hash. Only the root field is excluded when verifying a top-level contract.

## Compatibility

Readers ignore unknown fields and schemas permit additional properties. This
supports additive forward-compatible fields while preserving the original JSON
 bytes for coordination stores to retain. A typed reader must not rewrite
an unknown field away unless an explicit migration owns that behavior.

The compatibility policy is:

- Additive optional fields are compatible within schema version `1`.
- New enum values are not assumed to be understood by an older reader.
- Required-field changes, type changes, hash-rule changes, and semantic changes
  require a new schema version.
- An unsupported schema version returns `unsupported-schema-version` rather than
  being downgraded or guessed.
- A malformed JSON document returns the stable `invalid-json` validation code.
- Schema validation reports stable paths and the `invalid-contract` code without
  including note bodies, secrets, or raw payload values in the error contract.
- A supported document with a mismatched root hash returns
  `content-hash-mismatch`.

## Events and idempotency

`CoordinationEvent.EventType` is a stable discriminator. The current values are
`work-item.created`, `work-item.claimed`, `work-item.started`,
`work-item.blocked`, `work-item.partial`, `work-item.failed`,
`work-item.completed`, `work-item.canceled`, `work-item.stale`,
`work-item.reopened`, `work-item.claim.renewed`, and
`work-item.claim.released`.

Each event also carries a server-generated `event_id`, a monotonically ordered
`sequence_number`, a `transition_id`, server timestamps, actor diagnostics, and
the transition payload. `agent`, `client_name`, and session metadata are
diagnostic context only. They never grant authority, ownership, or a claim.

The event store applies these idempotency rules:

- Repeating a `transition_id` with the same canonical payload returns the
  original result.
- Reusing a `transition_id` with a different payload is a duplicate-transition
  conflict.
- A different event ID does not make a repeated transition valid.
- Sequence and state-version checks remain separate from idempotency checks.

## Claims and leases

Claim, lease, expiry, fencing, resource-key, and fail-closed behavior are
defined in the [durable coordination profile](durable-coordination.md). The
`coordination-claim.schema.json` contract covers the serialized claim
projection; this document does not duplicate the coordination state rules.

## Event persistence and replay

The [durable coordination profile](durable-coordination.md) owns the local
storage layout, immutable event persistence, projection rebuild, replay
ordering, and filesystem boundary. The contract documents and fixtures listed
above define the JSON representation validated at that boundary.

## Validation

`CoordinationContractValidator` loads only the reviewed embedded schema for the
requested `CoordinationContractKind`. It validates raw JSON or a typed
coordination model and returns `CoordinationValidationResult` with stable path
and code pairs.

The tests verify that representative fixtures pass, malformed fixtures fail,
unknown fields remain readable, canonical hashes are repeatable, caller
metadata cannot become an authority scope, event history replays
deterministically, duplicates are idempotent, invalid history fails closed,
projections recover after deletion, competing claimants produce one owner,
expiry advances fencing, stale owners are rejected, and lease state survives a
restart. The coordination application service and MCP adapters reuse these
contracts rather than introducing a second serializer or schema catalog.

## Scope boundary

This document owns JSON schemas, canonicalization, hashes, compatibility, and
validation. The gated capability and release policy is documented in
[coordination-rollout.md](coordination-rollout.md); the coordination profile's
storage, filesystem, session, and external-protocol boundaries are documented
in [durable-coordination.md](durable-coordination.md).
