# Durable coordination profile

This document defines the architecture contract for Kioku's durable
coordination profile. It is the review artifact for issue
[#303](https://github.com/sandovaldavid/kioku/issues/303); event persistence and
deterministic projection replay are implemented by issue
[#305](https://github.com/sandovaldavid/kioku/issues/305), and claims, leases,
and fencing are implemented by issue
[#306](https://github.com/sandovaldavid/kioku/issues/306). Later coordination
capabilities remain incomplete.

**Decision status:** Architecture boundary in progress; event persistence,
claims, leases, fencing, the guarded vault-mutation boundary, and work-session
compatibility are implemented. Crash/restart coverage, observability, and
rollout controls remain in later issues.

The profile coordinates independent Kioku processes that share one vault on a
supported local filesystem. It is not a distributed lock service, an
authentication provider, a hosted coordinator, or a replacement for Git.

## Scope and boundaries

The profile adds a machine-owned control plane for durable work coordination.
It does not change the existing ownership of note content or work-session
history.

The profile has these boundaries:

- Markdown files and YAML frontmatter remain the durable source of truth for
  note content, project documents, and existing work-session history.
- The coordination event log is the durable source of truth for coordination
  state, claims, attempts, and transition history.
- Search indexes, embeddings, state snapshots, and lease projections are
  derived data and must be rebuildable.
- The control plane stores references, identifiers, timestamps, outcomes, and
  safe reasons. It must not copy note bodies by default.
- Coordination MCP operations remain gated by server configuration and the
  default-off `coordination` capability group. The surface exposes focused tools
  for projections, runs, transitions, claims, history, handoffs, blockers,
  stale work, failed attempts, and conflicts, plus read-only resources for
  projections, history, and handoff packets.
- A caller can coordinate work only through the Kioku server. Direct edits to
  the vault remain outside the coordination guarantee.
- A mutation can require an expected content revision or hash, a current claim
  and fence generation, and an idempotency key. Empty preconditions preserve
  the legacy write behavior.

The following are explicit non-goals for this profile:

- providing a distributed transaction across unrelated vault resources;
- making direct filesystem edits participate in Kioku's coordination locks;
- providing multi-tenant authorization;
- synchronizing independent Git checkouts;
- making cloud-sync folders safe for concurrent writers.

## Domain model

The coordination record represents one durable work item. Its state describes
the work item, while each attempt records one execution of that work item.
Attempts become terminal when they finish, fail, are canceled, or become
stale. A retry creates a new attempt rather than reopening an old attempt.

### Identities

The server owns all durable identifiers. Callers can reference identifiers,
but supplying an identifier never grants authority over the referenced object.

| Identity | Responsibility and lifecycle |
|---|---|
| `run_id` | Identifies one coordinated execution graph or user goal. It is immutable, server-generated, and groups one or more work items. A run does not become a security principal. |
| `work_item_id` | Identifies one durable unit of work within a run. It remains stable across retries and is never reused for unrelated work. |
| `attempt_id` | Identifies one execution attempt for a work item. The server creates a new value for every retry. An attempt has one outcome and is not reopened after it ends. |
| `session_id` | Identifies an existing Kioku work session. It is a UUIDv7 and remains the primary identity of that session. A session can execute multiple work items, and a work item can exist without an active session. |
| `parent_session_id` | Records handoff provenance from one session to another. It does not transfer ownership, claims, authority, or lifecycle control. |
| `claim_id` | Identifies one server-issued lease for one attempt and one resource key. It is invalid after release, expiry, cancellation, or fencing. A new acquisition receives a new value. |
| `resource_key` | Identifies the canonical conflict scope protected by a claim. Note resources use a server-derived vault-relative key; logical resources use a server-defined namespace. Callers cannot bypass path policy by choosing an arbitrary equivalent path. |
| `agent` | Identifies the declared executor label after existing Kioku normalization. It is useful for display and diagnostics, but it is not an authenticated principal. |
| `client_name` | Identifies the declared MCP client or transport metadata. It is diagnostic metadata only and is never an authenticated principal. |

The following metadata is required for deterministic coordination, even though
it is not part of the requested identity list:

- `event_id` is a server-generated UUIDv7 for one immutable transition record.
- `state_version` is a monotonically increasing version for one work item.
- `fence_generation` is a monotonically increasing server value for a
  resource claim. A lower generation cannot mutate the resource.
- `observed_at`, `created_at`, `started_at`, `ended_at`, lease expiry, and
  retry timestamps are generated from server time and persisted in UTC.
- `transition_id` or an equivalent idempotency key identifies a retried
  request. It deduplicates an operation; it does not identify an actor.
- `reason`, `outcome`, and `error_code` contain bounded, safe metadata. They
  must not include note bodies or secrets by default.

### Relationships

The relationships between identities are normative and can be tested without
depending on filenames or modification times.

```text
run_id
  -> work_item_id
       -> attempt_id
            -> claim_id + resource_key

session_id -> work_item_id (optional execution context)
session_id -> parent_session_id (optional handoff provenance)
```

The following relationship rules apply:

- Every coordinated work item belongs to exactly one `run_id`.
- Every attempt belongs to exactly one `work_item_id`.
- An attempt can hold zero or more claims over its lifetime, but only one
  active claim exists for a given attempt and resource key.
- A resource key has at most one active claim in one coordination epoch.
- `parent_session_id` creates provenance only. It never makes the child session
  a continuation of the parent's ownership.
- `agent` and `client_name` can be copied into events for diagnostics, but
  state transitions use server validation, claims, and capability scope.

## Authority model

Kioku separates transport access, capability configuration, operational
ownership, and multi-tenant authorization. Combining these concepts would
make a caller-controlled label appear to be a security principal.

### Transport access

The existing transport boundary remains authoritative for access to the
server:

- `stdio` relies on the operating-system account that launched the process.
- Non-loopback Streamable HTTP uses the configured API key unless the operator
  explicitly enables the existing unsafe override.
- A transport credential proves access to that server instance. It does not
  identify a distinct agent, session, or tenant.

The current threat model already assumes that a malicious process running as
the same operating-system user can bypass Kioku and edit the vault directly.
The coordination profile does not change that assumption.

### Capability authority

The server derives operational authority from configuration, not from request
metadata. A coordination operation is admissible only when all of the
following checks pass:

- the transport satisfies the existing server access policy;
- the coordination capability group is enabled by the server and vault
  configuration;
- the target resource passes `VaultPathPolicy` and canonical path checks;
- the requested state transition is valid for the current state and version;
- a required claim and fence generation are current;
- the request does not exceed configured request and payload limits.

The vault `capabilities` block controls tool registration in the same way as
the existing optional groups. A client cannot enable a group by sending an
`enabled` claim in a tool argument or MCP metadata.

### Derived coordination scopes

The `coordination` capability group maps to coarse server-derived scopes. The
authority relationship is fixed by this contract.

| Scope | Derivation | Grant |
|---|---|---|
| `coordination.read` | The server is configured for the coordination profile and the vault capability group is enabled. | Read work-item state, transition history, and safe diagnostics. |
| `coordination.write` | `coordination.read` is active and the requested operation is a registered mutating coordination operation. | Create work items, request transitions, acquire or renew claims, and submit compare-and-swap mutations subject to state, claim, fence, and path checks. |
| `coordination.recover` | The server process is explicitly configured for local recovery or migration. The vault capability group alone never derives this scope. | Repair or migrate coordination metadata, establish a new epoch, and quarantine invalid machine records. |

Scopes are evaluated from server configuration and enabled capability groups at
startup. They are not selected by `agent`, `client_name`, MCP metadata, or a
caller-supplied authority claim. A write scope never implies recovery scope,
and a read scope never implies ownership of a work item or resource.

### Operational ownership

Operational ownership is a concurrency property, not an authentication
property. The server grants ownership through a current `claim_id`, its
resource key, its lease, and its fence generation. `VaultMutationService` can
require those values, together with an expected content revision or hash,
before it commits a write. It does not infer ownership from agent or client
labels.

The server uses `session_id` to associate an operation with a work context
when one is supplied. It uses `agent` and `client_name` for audit output and
diagnostics. It never accepts those values as proof that the caller owns a
claim.

### Multi-tenant authorization

Kioku is not a multi-tenant authorization service. It has no users, roles,
tenant isolation, per-client scope registry, or built-in credential rotation.
Internet-facing or multi-user deployments require an appropriate gateway and
must not treat `agent`, `client_name`, `run_id`, or `session_id` as tenant
boundaries.

## State machine

The state machine is deterministic. The current state is part of the durable
work-item projection, and every accepted transition records the previous
state, next state, server time, actor labels, required identifiers, and a
state version.

### State semantics

The `failed`, `partial`, `blocked`, and `stale` states are non-terminal at the
work-item level so that an explicit retry or resolution can continue the same
work item. The attempt that entered one of those states is terminal. The
`completed` and `canceled` states are terminal for both the work item and its
current attempt.

| State | Terminal | Required meaning | Active claim |
|---|---|---|---|
| `pending` | No | The work item exists but no attempt currently owns its resources. | None. |
| `claimed` | No | An attempt has acquired its resources but has not recorded that execution started. | One valid claim per protected resource. |
| `running` | No | The attempt has started work and must continue renewing its lease while it operates. | One valid claim per protected resource. |
| `blocked` | No | Progress cannot continue because a dependency, decision, or external condition is unresolved. | None. The previous claim is released. |
| `partial` | No | The attempt produced an accepted subset of the outcome but did not finish the work item. | None. The previous claim is released. |
| `failed` | No | The attempt ended without completing the work item and recorded a failure outcome. | None. The previous claim is released. |
| `stale` | No | The server observed that a claimed or running attempt's lease expired before a terminal outcome. | None. The expired claim is invalid. |
| `completed` | Yes | The work item reached its declared completion condition and recorded an outcome. | None. |
| `canceled` | Yes | The work item was intentionally stopped and recorded a reason and actor metadata. | None. |

`stale` is persisted. Lease expiry is the condition that permits the server
to create the transition, but the state does not change merely because a
clock has passed an expiry time. A server operation such as reconciliation,
read, or a competing claim observes the expiry and persists `stale` with the
expired claim, prior state, expiry time, and observation time.

### Transition rules

The following table is the complete transition set. Any transition not listed
is rejected without changing the work item, attempt, claim, or version.

| Transition | Allowed actor | Required metadata | Claim behavior | Retry and idempotency |
|---|---|---|---|---|
| `pending -> claimed` | Server after a successful claim acquisition. | New `attempt_id`, `claim_id`, `resource_key`, lease expiry, fence generation, session context if supplied. | Creates the active claim. | Repeating the same transition ID returns the original result. A different active claim returns a conflict. |
| `pending -> canceled` | Server-authorized coordination operation. | Server cancellation time, reason, and actor labels. | No claim exists. | Repeating the same request returns the terminal result. No retry is allowed. |
| `claimed -> running` | The current claim owner. | `attempt_id`, `claim_id`, fence generation, server start time, and expected state version. | Keeps the claim and renews its lease. | Repeating the same transition ID is a no-op. A stale or different claim is rejected. |
| `claimed -> blocked` | The current claim owner or a server-authorized coordination operation. | Block reason, server time, and dependency or next-action reference when available. | Releases the claim. | A duplicate returns the same blocked result. Resolution requires an explicit retry. |
| `claimed -> failed` | The current claim owner or the server after an internal failure. | Safe error code, bounded failure detail, server time, and retryable flag. | Releases the claim. | The attempt remains terminal. Retry creates a new attempt. |
| `claimed -> canceled` | The current claim owner or a server-authorized cancellation. | Server cancellation time, reason, and actor labels. | Releases the claim. | Repeating cancellation is idempotent. No retry is allowed. |
| `claimed -> stale` | Server reconciliation only. | Expired claim, prior state, lease expiry, detection time, and reason. | Invalidates and releases the claim. | Repeated detection does not create another transition. Retry requires a new attempt. |
| `running -> blocked` | The current claim owner or a server-authorized coordination operation. | Block reason, progress reference when available, server time, and expected version. | Releases the claim. | Duplicate requests return the existing blocked result. |
| `running -> partial` | The current claim owner. | Accepted progress summary, result references, server end time, and expected version. | Releases the claim. | Retry creates a new attempt and preserves the prior outcome. |
| `running -> failed` | The current claim owner or the server after an internal failure. | Safe error code, bounded failure detail, server time, and expected version. | Releases the claim. | Retry creates a new attempt. The failed attempt is never reopened. |
| `running -> completed` | The current claim owner. | Completion evidence or result reference, server completion time, and expected version. | Releases the claim. | A duplicate returns the completed result. No retry is allowed. |
| `running -> canceled` | The current claim owner or a server-authorized cancellation. | Server cancellation time, reason, and expected version. | Releases the claim. | Repeating cancellation is idempotent. No retry is allowed. |
| `running -> stale` | Server reconciliation only. | Expired claim, prior state, lease expiry, detection time, and reason. | Invalidates and releases the claim. | Repeated detection does not create another transition. Retry requires a new attempt. |
| `blocked -> pending` | Server-authorized resolution or retry operation. | Resolution or retry reason, server time, and expected version. | No claim is created until a later claim operation. | Repeating the same transition ID is idempotent. A later claim creates a new attempt. |
| `blocked -> canceled` | Server-authorized coordination operation. | Server cancellation time, reason, and actor labels. | No claim exists. | Terminal and idempotent. |
| `partial -> pending` | Server-authorized retry operation. | Retry reason, preserved-progress reference, server time, and expected version. | No claim is created until a later claim operation. | Creates a new attempt only when claimed. |
| `partial -> completed` | Server-authorized completion operation after accepting the partial outcome. | Completion reason, result reference, server time, and expected version. | No claim exists. | Terminal and idempotent. |
| `partial -> canceled` | Server-authorized coordination operation. | Server cancellation time, reason, and actor labels. | No claim exists. | Terminal and idempotent. |
| `failed -> pending` | Server-authorized retry operation. | Retry reason, prior failure reference, server time, and expected version. | No claim is created until a later claim operation. | Creates a new attempt only when claimed. |
| `failed -> canceled` | Server-authorized coordination operation. | Server cancellation time, reason, and actor labels. | No claim exists. | Terminal and idempotent. |
| `stale -> pending` | Server-authorized retry operation after stale detection. | Expired claim reference, retry reason, server time, and expected version. | No claim is created until a later claim operation. | Creates a new attempt only when claimed. |
| `stale -> canceled` | Server-authorized coordination operation. | Server cancellation time, reason, and actor labels. | No claim exists. | Terminal and idempotent. |

The server must reject a transition when its expected state version does not
match the current version. A rejected compare-and-swap does not advance the
version and does not release another actor's claim.

### Claim and lease rules

Claims provide operational ownership through the implemented
`CoordinationClaimStore`. They do not protect a vault from an operating-system
user or process that edits files without using Kioku. `VaultMutationService`
revalidates content and optional claim/fence preconditions while holding the
canonical resource lock before committing a mutation.

The following rules are normative:

- A claim is scoped to one `attempt_id` and one canonical `resource_key`.
- A multi-resource operation acquires individual resource claims in a stable
  sorted order or fails without a partial claim set.
- Only the server creates `claim_id` and `fence_generation` values.
- Lease and expiry decisions use server UTC time. Client timestamps are
  informational metadata only.
- A claim can be renewed only while its claim ID and fence generation remain
  current. An expired claim cannot be renewed.
- Expiry invalidates the claim before a new attempt can mutate the resource.
- A new attempt receives new claim and fence values. It never reuses the stale
  attempt's claim.
- Claims are released on every transition out of `claimed` or `running`.
- A claim conflict is a deterministic operational error, not a request to pick
  the most recently modified file or session.

## Storage and durability

The control plane lives inside the configured vault but outside the indexed
Markdown tree. This keeps coordination local to the vault while preventing
machine records from becoming ordinary notes or search results.

### Coordination layout

The following layout is the normative control-plane layout. Event files,
manifest validation, projections, runtime locks, and lease projections are
implemented in issues `#305` and `#306`; quarantine remains reserved for later
recovery work. The names are vault-relative and remain subject to
`VaultPathPolicy`.

```text
{vault}/
  .kioku/
    config.yml
    embeddings.bin
    coordination/
      manifest.json
      events/
        YYYY/
          MM/
            <event_id>.json
      snapshots/
        work-items/
          <work_item_id>.json
      leases/
        <resource_key_hash>.json
      mutations/
        <mutation_id_hash>.json
      quarantine/
      runtime/
```

Each area has one responsibility:

- `manifest.json` contains the format version, coordination epoch, and
  non-secret recovery metadata.
- `events/` contains immutable, server-generated transition records. One file
  per event avoids concurrent append interleaving and makes each event
  independently recoverable.
- `snapshots/work-items/` contains rebuildable current-state projections. A
  snapshot never replaces the event history as the durable source of truth.
- `leases/` contains rebuildable active-claim projections. The event history,
  current epoch, resource lock, and server-time check remain authoritative.
- `mutations/` contains bounded idempotency records for committed retries. A
  reused mutation ID with different operation inputs is rejected.
- `quarantine/` preserves malformed or rejected machine records for explicit
  operator recovery. The server must not silently delete them.
- `runtime/` contains ephemeral process coordination artifacts such as lock
  files. It is not durable state and is not required for replay.

The `.kioku` directory is already excluded from indexing because Kioku skips
hidden paths. The control plane must remain excluded from note retrieval,
embeddings, graph analysis, and ordinary vault organization tools.

The event store writes one immutable event per file, uses exclusive creation,
and serializes writers for one work item with a runtime lock. It validates the
manifest, event schema, content hash, sequence, hash chain, idempotency, and
state transition before accepting an event. Work-item snapshots are derived
and are written atomically after a successful reducer pass. Missing snapshots
are rebuilt from the event history; corrupt snapshots are rejected rather than
silently repaired.

The claim store serializes competing owners with a hashed resource lock and
writes lease projections atomically. It never treats an expired, released, or
superseded claim as current. Takeover creates a new claim ID and increments the
resource fence generation. The event log records renewals, releases, expiry,
takeovers, completion, and cancellation without copying note bodies.

### Source-of-truth relationship

The durable boundaries are explicit so a later implementation cannot turn a
cache into an authority by accident.

| Data | Durable source of truth | Derived data |
|---|---|---|
| Note body and user frontmatter | Markdown note | Vault search and graph indexes |
| Existing work-session history | Session Markdown and preserved frontmatter | Session lists and work-context views |
| Coordination state and transition history | `.kioku/coordination/events/` | Work-item snapshots and lease projections |
| Mutation retry identity | `.kioku/coordination/mutations/` | None |
| Embeddings | None; embeddings are rebuildable | `.kioku/embeddings.bin` |
| Active process locks | Operating-system lock state | Files under `coordination/runtime/` |

The event log is the authority for coordination state. Markdown remains the
authority for the human work product. A coordination record stores a note path,
resource key, or result reference rather than a second copy of the note body.

### Backup and restore

Backups must preserve the vault's Markdown content and the coordination event
log as one consistent unit.

The backup and restore contract is:

- Quiesce Kioku writers or stop all Kioku processes before taking a backup.
- Include `.kioku/coordination/` with the Markdown vault. Embeddings and other
  derived indexes can be omitted when the operator accepts a rebuild.
- Restore the vault and coordination directory together. A partial restore is
  not a supported coordination recovery.
- After a restore, the server must establish a new coordination epoch before
  accepting claims. Leases from before the restore are not reusable.
- Preserve the original event files during recovery. Never rebuild by deleting
  events that cannot currently be parsed.
- Report corruption as a coordination-unavailable error and fail closed for
  claim-protected mutations. Read-only note operations can continue when their
  independent index is healthy.

### Schema migration

Coordination data is versioned independently from note frontmatter. Migration
must be explicit, reversible, and safe to interrupt.

The migration contract is:

- The manifest and every event carry a format version.
- A newer unsupported version prevents coordination writes rather than being
  guessed or downgraded.
- A migration requires a verified backup and runs while all writers are
  stopped or quiesced.
- Migration writes a new representation atomically and retains the original
  until verification succeeds.
- Event migration is deterministic and idempotent. It never rewrites user
  Markdown as a side effect.
- A failed migration leaves the previous readable representation available or
  reports a blocked recovery state without silently dropping history.

### Filesystem support boundary

The profile depends on shared visibility, exclusive creation, reliable file
locks, and atomic replacement. The following support classification is
normative.

| Environment | Classification | Contract |
|---|---|---|
| Multiple Kioku processes on one host using one local filesystem path | Supported | The filesystem must provide the local platform's documented create, lock, flush, and replace semantics. |
| A container or VM sharing a local volume with the host | Unconfirmed | It requires explicit verification that all participants share the same lock and rename semantics. |
| NFS, SMB, WebDAV, or another network filesystem | Unsupported | Kioku does not rely on these systems for claim or fencing correctness. |
| Dropbox, iCloud, Syncthing, or similar replicated folders | Unsupported for concurrent writers | Replication is not a coordination protocol. A stopped-server copy may be used as an operator backup only after independent verification. |
| Independent Git checkouts of the same repository or vault | Unsupported for shared coordination | Each checkout has a separate control plane and claims do not cross the checkout boundary. |
| A quiesced backup or restore of the complete vault | Supported as an operator procedure | Stop writers, copy Markdown and coordination data together, then establish a new epoch before accepting claims. |

The configured vault path remains subject to canonical path validation. A
symlink or reparse point that resolves outside the configured vault is denied.
An alias that resolves inside the vault must still produce one canonical
`resource_key`; path spellings and modification times are never resource
identities.

## Compatibility with work sessions

Existing work sessions remain valid and do not require a bulk migration. The
coordination profile links to a session when useful, but it does not replace
the session lifecycle.

The compatibility rules are:

- Existing session notes retain their current `session_id`, `agent`,
  `client_name`, `status`, timestamps, and `parent_session_id` fields.
- `run_id`, `work_item_id`, and `attempt_id` are additive frontmatter fields;
  they are written only when a caller explicitly opts into coordination.
- Existing `active` and `done` session statuses are not translated into the
  coordination work-item state machine.
- A session can be the execution context for multiple work items. Closing a
  session does not implicitly complete, fail, or cancel those work items.
- A work item can continue to exist after its session ends. A later session can
  resume the work through an explicit relationship, not by filename guessing.
- The legacy `session_note` selector remains supported during the documented
  compatibility window.
- Legacy session notes without `session_id` remain readable as legacy records.
  New coordination operations must use durable IDs and must not infer identity
  from filenames or modification times.
- `parent_session_id` remains provenance only. It never reopens or closes the
  parent session and never grants the child session its claim.
- A new linked session creates one idempotent pending work item. Linking an
  existing session requires an expected content revision or hash, but no claim,
  because the link operation does not grant ownership.
- Resume and close operations for a linked session require an expected content
  revision or hash together with the current `claim_id` and
  `fence_generation`. A legacy selector cannot bypass those checks after the
  link is persisted.
- Closing a session does not implicitly transition its work item. The work item
  can continue after the session ends and a later session can reference it
  explicitly.
- Coordination writes must preserve unknown frontmatter fields and must not
  rewrite session Markdown unless an explicit session operation requests it.

## Threat-model update

The profile adds durable machine metadata and improves concurrency handling,
but it does not expand Kioku's authentication boundary. Event persistence,
claims, leases, and fencing are implemented in issues `#305` and `#306`.
The guarded vault-mutation boundary and precondition arguments on core
single-resource write tools are implemented in issue `#307`. Batch tools still
apply per-file mutations without one expected revision covering the batch.

| Threat | Control | Residual risk |
|---|---|---|
| A crashed or abandoned owner keeps a claim | Server-time lease expiry, persisted `stale` state, and a new fence generation for the next attempt. | A process that edits files directly can bypass the claim. |
| Two local Kioku processes race for one resource | Canonical resource keys, exclusive claim acquisition, state-version checks, and fencing. | The guarantee applies only inside the supported filesystem and server boundary. |
| Obsidian manually edits or moves a protected note | A supplied expected revision or hash rejects an unexpected version instead of silently overwriting it. | Direct edits are not transactionally merged, and callers that omit preconditions retain legacy unconditional-write behavior. |
| A caller supplies a convincing owner or authority label | Server-derived capability scope and server-issued claim data; `agent`, `client_name`, and caller claims remain untrusted. | Same-user processes and configured server operators retain the authority of the existing local trust model. |
| Control-plane data exposes private vault content | Event records contain identifiers, paths, labels, bounded reasons, and result references, not note bodies by default. The directory remains hidden from indexing. | The same operating-system account can read the vault and backups. Resource paths and labels can still be sensitive. |
| A cloud replica replays old claims | The support boundary rejects concurrent replicated folders and restore establishes a new coordination epoch. | An operator can still misuse an unsupported replica and lose coordination guarantees. |

The threat model retains these existing assumptions:

- the operating system and account running Kioku are trusted;
- a malicious same-user process can bypass Kioku and write the vault;
- the MCP client can expose note content outside Kioku;
- Kioku is not a multi-tenant authorization service.

## Executable invariants

The following invariants are the minimum contract for tests in later issues.
Each identifier is intended to become one or more deterministic test cases.

- **I-01 Identity uniqueness:** The server never creates two durable objects
  with the same `run_id`, `work_item_id`, `attempt_id`, or `claim_id`.
- **I-02 Relationship integrity:** Every attempt references one work item, and
  every work item references one run.
- **I-03 Transition completeness:** A state transition not listed in this
  document is rejected without changing durable state.
- **I-04 Version fencing:** A transition with a stale state version fails
  without advancing the current version or releasing another claim.
- **I-05 Active-attempt uniqueness:** One work item has at most one active
  attempt at a time.
- **I-06 Resource exclusivity:** One coordination epoch has at most one active
  claim for a canonical resource key.
- **I-07 Claim freshness:** A claim is valid only when its ID, fence
  generation, attempt, resource, and server-time lease all match.
- **I-08 Stale persistence:** An observed expired lease creates at most one
  persisted `stale` transition for the affected attempt.
- **I-09 Retry isolation:** Every retry creates a new attempt, claim, and
  fence generation. No stale attempt can mutate through a new attempt's claim.
- **I-10 Terminal immutability:** `completed` and `canceled` have no outgoing
  transitions. Corrections use a new work item.
- **I-11 Idempotent replay:** Repeating one transition ID with the same
  payload returns the original result; reusing it with a different payload is
  an error.
- **I-12 Server time:** Lease, expiry, ordering, and duration decisions never
  use a caller-provided timestamp.
- **I-13 Authority separation:** `agent`, `client_name`, and caller-supplied
  authority claims never satisfy a capability or ownership check.
- **I-14 Resource canonicalization:** Equivalent accepted vault paths map to
  one resource key, and paths outside the vault cannot produce a resource key.
- **I-15 Source separation:** Coordination events never become note content,
  and note indexing never becomes coordination authority.
- **I-16 Recovery fail-closed:** Unsupported schema, corrupted event history,
  and an invalid epoch prevent claim-protected writes without deleting history.
- **I-17 Session compatibility:** Existing session notes remain readable and
  preserve unknown frontmatter fields after coordination links are added.
- **I-18 Manual-edit safety:** An unexpected note version or path change
  prevents a preconditioned overwrite and exposes a conflict for resolution.

## Implementation gates

This document is the architecture gate for the remaining coordination work.
Later issues must implement the contract in dependency order and must not
silently narrow its semantics.

The planned sequence is:

1. [#304](https://github.com/sandovaldavid/kioku/issues/304) defines shared
   contracts and serialization shapes from this document.
2. [#305](https://github.com/sandovaldavid/kioku/issues/305) implements event
   persistence, replay, snapshots, corruption handling, and migration checks.
3. [#306](https://github.com/sandovaldavid/kioku/issues/306) implements claims,
   leases, expiry observation, and fencing.
4. [#307](https://github.com/sandovaldavid/kioku/issues/307) implements
   compare-and-swap vault mutation and manual-edit conflict handling.
5. [#308](https://github.com/sandovaldavid/kioku/issues/308) adds the gated MCP
   surface without making caller metadata authoritative.
6. [#309](https://github.com/sandovaldavid/kioku/issues/309) adds and documents
   additive work-session compatibility, lazy linking, and guarded legacy
   resume/close behavior. This gate is implemented on the current branch.
7. [#310](https://github.com/sandovaldavid/kioku/issues/310) derives crash,
   restart, concurrency, restore, and filesystem-boundary tests from the
   invariants.
8. [#311](https://github.com/sandovaldavid/kioku/issues/311) adds bounded
   observability and rollout controls without sending note content to a
   network sink.

No issue in this sequence may treat a successful in-process semaphore as proof
of cross-process durability. The filesystem support boundary and the
event-log source of truth must remain visible in tests and public
documentation.
