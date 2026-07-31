# Coordination contracts

This document describes the versioned JSON contracts implemented for issue
[#304](https://github.com/sandovaldavid/kioku/issues/304). The contracts are
the shared boundary for future coordination persistence, replay, MCP adapters,
and interoperability fixtures. Event storage, claims, note mutation, and
coordination tools remain outside this implementation.

## Contract documents

Each top-level document uses `schema_version: 1` and includes a root
`content_hash` field. The reviewed schemas are embedded in the server
assembly and the representative fixtures are copied into the test output.

| Contract | Domain purpose | Schema |
|---|---|---|
| Handoff packet | Transfers run, work-item, attempt, session, scope, checkpoint, and safe next-action state. | `handoff-packet.schema.json` |
| Coordination event | Records one immutable state transition for a future event log. | `coordination-event.schema.json` |
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
bytes for a future persistence layer to retain. A typed reader must not rewrite
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
`work-item.completed`, `work-item.canceled`, `work-item.stale`, and
`work-item.reopened`.

Each event also carries a server-generated `event_id`, a monotonically ordered
`sequence_number`, a `transition_id`, server timestamps, actor diagnostics, and
the transition payload. `agent`, `client_name`, and session metadata are
diagnostic context only. They never grant authority, ownership, or a claim.

The future event store must apply these idempotency rules:

- Repeating a `transition_id` with the same canonical payload returns the
  original result.
- Reusing a `transition_id` with a different payload is a duplicate-transition
  conflict.
- A different event ID does not make a repeated transition valid.
- Sequence and state-version checks remain separate from idempotency checks.

## Validation

`CoordinationContractValidator` loads only the reviewed embedded schema for the
requested `CoordinationContractKind`. It validates raw JSON or a typed
coordination model and returns `CoordinationValidationResult` with stable path
and code pairs.

The tests verify that representative fixtures pass, malformed fixtures fail,
unknown fields remain readable, canonical hashes are repeatable, and caller
metadata cannot become an authority scope. Later persistence and MCP work must
reuse these contracts rather than introduce a second serializer or schema
catalog.

## Scope boundary

This contract slice does not persist or replay events, acquire or expire claims,
perform compare-and-swap note writes, expose coordination MCP tools, or adopt
CloudEvents or A2A as mandatory internal formats. Those behaviors remain in the
dependent issues described by the [durable coordination architecture](durable-coordination.md).
