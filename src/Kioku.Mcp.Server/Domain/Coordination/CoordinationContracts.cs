using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kioku.Mcp.Server.Domain.Coordination;

/// <summary>
/// Identifies the versioned coordination documents supported by this server.
/// </summary>
public enum CoordinationContractKind
{
    HandoffPacket,
    CoordinationEvent,
    CoordinationClaim,
    CoordinationConflict,
    WorkItemProjection,
}

/// <summary>
/// Constants shared by the coordination contract and its JSON schemas.
/// </summary>
public static class CoordinationContract
{
    public const int CurrentSchemaVersion = 1;
    public const string ContentHashPropertyName = "content_hash";
    public const string ContentHashAlgorithm = "SHA-256";
    public const string ContentHashEncoding = "uppercase hexadecimal";
}

/// <summary>
/// Stable states for a coordinated work item.
/// </summary>
public static class CoordinationStates
{
    public const string Pending = "pending";
    public const string Claimed = "claimed";
    public const string Running = "running";
    public const string Blocked = "blocked";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Stale = "stale";
    public const string Completed = "completed";
    public const string Canceled = "canceled";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Pending,
        Claimed,
        Running,
        Blocked,
        Partial,
        Failed,
        Stale,
        Completed,
        Canceled,
    };
}

/// <summary>
/// Stable discriminators for persisted coordination events.
/// </summary>
public static class CoordinationEventTypes
{
    public const string WorkItemCreated = "work-item.created";
    public const string WorkItemClaimed = "work-item.claimed";
    public const string WorkItemStarted = "work-item.started";
    public const string WorkItemBlocked = "work-item.blocked";
    public const string WorkItemPartial = "work-item.partial";
    public const string WorkItemFailed = "work-item.failed";
    public const string WorkItemCompleted = "work-item.completed";
    public const string WorkItemCanceled = "work-item.canceled";
    public const string WorkItemStale = "work-item.stale";
    public const string WorkItemReopened = "work-item.reopened";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkItemCreated,
        WorkItemClaimed,
        WorkItemStarted,
        WorkItemBlocked,
        WorkItemPartial,
        WorkItemFailed,
        WorkItemCompleted,
        WorkItemCanceled,
        WorkItemStale,
        WorkItemReopened,
    };
}

/// <summary>
/// Server-derived scopes that may be recorded in a handoff packet.
/// </summary>
public static class CoordinationAuthorityScopes
{
    public const string Read = "coordination.read";
    public const string Write = "coordination.write";
    public const string Recover = "coordination.recover";
}

/// <summary>
/// Lifecycle values for a coordination claim projection.
/// </summary>
public static class CoordinationClaimStatuses
{
    public const string Active = "active";
    public const string Released = "released";
    public const string Expired = "expired";
    public const string Fenced = "fenced";
}

/// <summary>
/// Categories used to classify a coordination conflict without copying note content.
/// </summary>
public static class CoordinationConflictKinds
{
    public const string ClaimConflict = "claim-conflict";
    public const string CorruptHistory = "corrupt-history";
    public const string DuplicateTransition = "duplicate-transition";
    public const string ManualEdit = "manual-edit";
    public const string RevisionMismatch = "revision-mismatch";
}

/// <summary>
/// Resolution state for a persisted coordination conflict.
/// </summary>
public static class CoordinationConflictStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
}

/// <summary>
/// Untrusted diagnostic metadata associated with an operation.
/// </summary>
public sealed class CoordinationActor
{
    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}

/// <summary>
/// The bounded checkpoint carried by a handoff packet.
/// </summary>
public sealed class HandoffCheckpoint
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("revision")]
    public long? Revision { get; init; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; init; }
}

/// <summary>
/// A bounded next action in a handoff packet.
/// </summary>
public sealed class HandoffAction
{
    [JsonPropertyName("action_id")]
    public required string ActionId { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("resource_key")]
    public string? ResourceKey { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// A reference to an artifact produced by an attempt.
/// </summary>
public sealed class HandoffArtifact
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("reference")]
    public required string Reference { get; init; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; init; }
}

/// <summary>
/// A bounded blocker description.
/// </summary>
public sealed class HandoffBlocker
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("resolution_reference")]
    public string? ResolutionReference { get; init; }
}

/// <summary>
/// A compact reference to an unresolved coordination conflict.
/// </summary>
public sealed class HandoffConflictReference
{
    [JsonPropertyName("conflict_id")]
    public required string ConflictId { get; init; }

    [JsonPropertyName("resource_key")]
    public required string ResourceKey { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>
/// Versioned handoff state exchanged between independent Kioku processes.
/// </summary>
public sealed class HandoffPacket
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = CoordinationContract.CurrentSchemaVersion;

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    [JsonPropertyName("attempt_id")]
    public string? AttemptId { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("parent_session_id")]
    public string? ParentSessionId { get; init; }

    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("project")]
    public required string Project { get; init; }

    [JsonPropertyName("resource_scope")]
    public IReadOnlyList<string> ResourceScope { get; init; } = [];

    [JsonPropertyName("authority_scope")]
    public IReadOnlyList<string> AuthorityScope { get; init; } = [];

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("checkpoint")]
    public required HandoffCheckpoint Checkpoint { get; init; }

    [JsonPropertyName("next_actions")]
    public IReadOnlyList<HandoffAction> NextActions { get; init; } = [];

    [JsonPropertyName("artifacts")]
    public IReadOnlyList<HandoffArtifact> Artifacts { get; init; } = [];

    [JsonPropertyName("blockers")]
    public IReadOnlyList<HandoffBlocker> Blockers { get; init; } = [];

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<HandoffConflictReference> Conflicts { get; init; } = [];

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("state_version")]
    public long StateVersion { get; init; }

    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }
}

/// <summary>
/// Common state-transition details carried by a coordination event payload.
/// </summary>
public sealed class CoordinationTransitionPayload
{
    [JsonPropertyName("previous_state")]
    public string? PreviousState { get; init; }

    [JsonPropertyName("next_state")]
    public string? NextState { get; init; }

    [JsonPropertyName("expected_state_version")]
    public long? ExpectedStateVersion { get; init; }

    [JsonPropertyName("state_version")]
    public long? StateVersion { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    [JsonPropertyName("result_reference")]
    public string? ResultReference { get; init; }

    [JsonPropertyName("progress_reference")]
    public string? ProgressReference { get; init; }

    [JsonPropertyName("lease_expires_at")]
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    [JsonPropertyName("expected_hash")]
    public string? ExpectedHash { get; init; }

    [JsonPropertyName("actual_hash")]
    public string? ActualHash { get; init; }
}

/// <summary>
/// Immutable transition record for a future coordination event log.
/// </summary>
public sealed class CoordinationEvent
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = CoordinationContract.CurrentSchemaVersion;

    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    [JsonPropertyName("attempt_id")]
    public string? AttemptId { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("claim_id")]
    public string? ClaimId { get; init; }

    [JsonPropertyName("sequence_number")]
    public long SequenceNumber { get; init; }

    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("transition_id")]
    public required string TransitionId { get; init; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("recorded_at")]
    public DateTimeOffset RecordedAt { get; init; }

    [JsonPropertyName("actor")]
    public required CoordinationActor Actor { get; init; }

    [JsonPropertyName("payload")]
    public required CoordinationTransitionPayload Payload { get; init; }

    [JsonPropertyName("previous_hash")]
    public string? PreviousHash { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }
}

/// <summary>
/// A server-issued claim projection for one attempt and resource key.
/// </summary>
public sealed class CoordinationClaim
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = CoordinationContract.CurrentSchemaVersion;

    [JsonPropertyName("claim_id")]
    public required string ClaimId { get; init; }

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    [JsonPropertyName("attempt_id")]
    public required string AttemptId { get; init; }

    [JsonPropertyName("resource_key")]
    public required string ResourceKey { get; init; }

    [JsonPropertyName("fence_generation")]
    public long FenceGeneration { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("acquired_at")]
    public DateTimeOffset AcquiredAt { get; init; }

    [JsonPropertyName("lease_expires_at")]
    public DateTimeOffset LeaseExpiresAt { get; init; }

    [JsonPropertyName("released_at")]
    public DateTimeOffset? ReleasedAt { get; init; }

    [JsonPropertyName("release_reason")]
    public string? ReleaseReason { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }
}

/// <summary>
/// A conflict record containing safe revision and resource metadata only.
/// </summary>
public sealed class CoordinationConflict
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = CoordinationContract.CurrentSchemaVersion;

    [JsonPropertyName("conflict_id")]
    public required string ConflictId { get; init; }

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    [JsonPropertyName("attempt_id")]
    public string? AttemptId { get; init; }

    [JsonPropertyName("resource_key")]
    public required string ResourceKey { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("detected_at")]
    public DateTimeOffset DetectedAt { get; init; }

    [JsonPropertyName("expected_revision")]
    public long? ExpectedRevision { get; init; }

    [JsonPropertyName("actual_revision")]
    public long? ActualRevision { get; init; }

    [JsonPropertyName("expected_hash")]
    public string? ExpectedHash { get; init; }

    [JsonPropertyName("actual_hash")]
    public string? ActualHash { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    [JsonPropertyName("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; init; }

    [JsonPropertyName("resolved_by")]
    public CoordinationActor? ResolvedBy { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }
}

/// <summary>
/// A safe result summary referenced by a work-item projection.
/// </summary>
public sealed class CoordinationOutcome
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("result_reference")]
    public string? ResultReference { get; init; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Rebuildable current-state projection for one coordinated work item.
/// </summary>
public sealed class WorkItemProjection
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = CoordinationContract.CurrentSchemaVersion;

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("work_item_id")]
    public required string WorkItemId { get; init; }

    [JsonPropertyName("project")]
    public required string Project { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("state_version")]
    public long StateVersion { get; init; }

    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("attempt_id")]
    public string? AttemptId { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("parent_session_id")]
    public string? ParentSessionId { get; init; }

    [JsonPropertyName("resource_scope")]
    public IReadOnlyList<string> ResourceScope { get; init; } = [];

    [JsonPropertyName("active_claims")]
    public IReadOnlyList<CoordinationClaim> ActiveClaims { get; init; } = [];

    [JsonPropertyName("blockers")]
    public IReadOnlyList<HandoffBlocker> Blockers { get; init; } = [];

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<HandoffConflictReference> Conflicts { get; init; } = [];

    [JsonPropertyName("last_event_id")]
    public string? LastEventId { get; init; }

    [JsonPropertyName("last_event_sequence")]
    public long LastEventSequence { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("outcome")]
    public CoordinationOutcome? Outcome { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }
}

/// <summary>
/// Source-generated JSON metadata for the coordination contracts.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    WriteIndented = false)]
[JsonSerializable(typeof(HandoffPacket))]
[JsonSerializable(typeof(CoordinationEvent))]
[JsonSerializable(typeof(CoordinationClaim))]
[JsonSerializable(typeof(CoordinationConflict))]
[JsonSerializable(typeof(WorkItemProjection))]
internal partial class CoordinationJsonContext : JsonSerializerContext
{
}
