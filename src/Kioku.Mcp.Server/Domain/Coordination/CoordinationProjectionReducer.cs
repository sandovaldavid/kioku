namespace Kioku.Mcp.Server.Domain.Coordination;

/// <summary>
/// Stable failures raised when an event sequence cannot produce a projection.
/// </summary>
public static class CoordinationProjectionErrorCodes
{
    public const string EmptyHistory = "empty-history";
    public const string InvalidSequence = "invalid-sequence";
    public const string HashChainViolation = "hash-chain-violation";
    public const string IdentityMismatch = "identity-mismatch";
    public const string InvalidTransition = "invalid-transition";
    public const string UnsupportedEventType = "unsupported-event-type";
}

/// <summary>
/// Content-safe exception raised by the pure coordination projection reducer.
/// </summary>
public sealed class CoordinationProjectionException(string code)
    : InvalidOperationException($"Coordination projection cannot be rebuilt: {code}.")
{
    public string Code { get; } = code;
}

/// <summary>
/// Rebuilds a work-item projection from an ordered, immutable event sequence.
/// </summary>
public static class CoordinationProjectionReducer
{
    private static readonly IReadOnlyDictionary<string, string> EventStates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CoordinationEventTypes.WorkItemCreated] = CoordinationStates.Pending,
            [CoordinationEventTypes.WorkItemClaimed] = CoordinationStates.Claimed,
            [CoordinationEventTypes.WorkItemStarted] = CoordinationStates.Running,
            [CoordinationEventTypes.WorkItemBlocked] = CoordinationStates.Blocked,
            [CoordinationEventTypes.WorkItemPartial] = CoordinationStates.Partial,
            [CoordinationEventTypes.WorkItemFailed] = CoordinationStates.Failed,
            [CoordinationEventTypes.WorkItemCompleted] = CoordinationStates.Completed,
            [CoordinationEventTypes.WorkItemCanceled] = CoordinationStates.Canceled,
            [CoordinationEventTypes.WorkItemStale] = CoordinationStates.Stale,
            [CoordinationEventTypes.WorkItemReopened] = CoordinationStates.Pending,
            [CoordinationEventTypes.WorkItemClaimRenewed] = CoordinationStates.Claimed,
            [CoordinationEventTypes.WorkItemClaimReleased] = CoordinationStates.Pending,
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTransitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [CoordinationStates.Pending] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Claimed,
                CoordinationStates.Canceled,
            },
            [CoordinationStates.Claimed] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Running,
                CoordinationStates.Blocked,
                CoordinationStates.Failed,
                CoordinationStates.Canceled,
                CoordinationStates.Stale,
                CoordinationStates.Claimed,
                CoordinationStates.Pending,
                CoordinationStates.Completed,
            },
            [CoordinationStates.Running] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Blocked,
                CoordinationStates.Partial,
                CoordinationStates.Failed,
                CoordinationStates.Completed,
                CoordinationStates.Canceled,
                CoordinationStates.Stale,
                CoordinationStates.Pending,
            },
            [CoordinationStates.Blocked] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Pending,
                CoordinationStates.Canceled,
            },
            [CoordinationStates.Partial] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Pending,
                CoordinationStates.Completed,
                CoordinationStates.Canceled,
            },
            [CoordinationStates.Failed] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Pending,
                CoordinationStates.Canceled,
            },
            [CoordinationStates.Stale] = new HashSet<string>(StringComparer.Ordinal)
            {
                CoordinationStates.Pending,
                CoordinationStates.Canceled,
            },
            [CoordinationStates.Completed] = new HashSet<string>(StringComparer.Ordinal),
            [CoordinationStates.Canceled] = new HashSet<string>(StringComparer.Ordinal),
        };

    public static WorkItemProjection Reduce(IReadOnlyList<CoordinationEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.EmptyHistory);
        }

        var ordered = events.OrderBy(item => item.SequenceNumber).ToArray();
        var first = ordered[0];
        ValidateIdentity(ordered, first);

        var state = CoordinationStates.Pending;
        var stateVersion = 0L;
        IReadOnlyList<HandoffBlocker> blockers = [];
        CoordinationOutcome? outcome = null;
        var createdAt = first.RecordedAt;

        for (var index = 0; index < ordered.Length; index++)
        {
            var current = ordered[index];
            var expectedSequence = index + 1L;
            if (current.SequenceNumber != expectedSequence)
            {
                throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidSequence);
            }

            if (index > 0 && !string.Equals(current.PreviousHash, ordered[index - 1].ContentHash, StringComparison.Ordinal))
            {
                throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.HashChainViolation);
            }

            if (!EventStates.TryGetValue(current.EventType, out var nextState))
            {
                throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.UnsupportedEventType);
            }

            if (index == 0)
            {
                if (!string.Equals(current.EventType, CoordinationEventTypes.WorkItemCreated, StringComparison.Ordinal) ||
                    current.PreviousHash is not null ||
                    current.Payload.PreviousState is not null ||
                    current.Payload.ExpectedStateVersion is not null ||
                    current.Payload.StateVersion is not 0 ||
                    !string.Equals(nextState, current.Payload.NextState, StringComparison.Ordinal))
                {
                    throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidTransition);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(current.Payload.PreviousState) &&
                    !string.Equals(current.Payload.PreviousState, state, StringComparison.Ordinal))
                {
                    throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidTransition);
                }

                if (!AllowedTransitions[state].Contains(nextState))
                {
                    throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidTransition);
                }

                var expectedVersion = stateVersion + 1;
                if (current.Payload.StateVersion is { } suppliedVersion && suppliedVersion != expectedVersion)
                {
                    throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidTransition);
                }

                stateVersion = expectedVersion;
            }

            if (index == 0)
            {
                stateVersion = current.Payload.StateVersion ?? 0;
            }

            if (current.Payload.StateVersion is < 0)
            {
                throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidTransition);
            }

            state = nextState;
            (blockers, outcome) = BuildStateDetails(current, state);
        }

        var last = ordered[^1];
        var projection = new WorkItemProjection
        {
            RunId = first.RunId,
            WorkItemId = first.WorkItemId,
            Project = first.Project ?? "unknown",
            State = state,
            StateVersion = stateVersion,
            Revision = last.SequenceNumber,
            AttemptId = last.AttemptId,
            SessionId = last.SessionId,
            ParentSessionId = first.ParentSessionId,
            ResourceScope = first.ResourceScope,
            ActiveClaims = [],
            Blockers = blockers,
            Conflicts = [],
            LastEventId = last.EventId,
            LastEventSequence = last.SequenceNumber,
            CreatedAt = createdAt,
            UpdatedAt = last.RecordedAt,
            Outcome = outcome,
        };
        projection.ContentHash = CoordinationContractSerializer.ComputeContentHash(projection);
        return projection;
    }

    private static void ValidateIdentity(IReadOnlyList<CoordinationEvent> events, CoordinationEvent first)
    {
        if (first.SequenceNumber != 1 ||
            !string.Equals(first.EventType, CoordinationEventTypes.WorkItemCreated, StringComparison.Ordinal))
        {
            throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.InvalidSequence);
        }

        if (events.Any(current =>
                !string.Equals(current.RunId, first.RunId, StringComparison.Ordinal) ||
                !string.Equals(current.WorkItemId, first.WorkItemId, StringComparison.Ordinal)))
        {
            throw new CoordinationProjectionException(CoordinationProjectionErrorCodes.IdentityMismatch);
        }
    }

    private static (IReadOnlyList<HandoffBlocker> Blockers, CoordinationOutcome? Outcome) BuildStateDetails(
        CoordinationEvent current,
        string state)
    {
        var payload = current.Payload;
        var blockers = state == CoordinationStates.Blocked
            ? new[]
            {
                new HandoffBlocker
                {
                    Code = payload.ErrorCode ?? "work-item.blocked",
                    Reason = payload.Reason ?? "The work item is blocked.",
                    ResolutionReference = payload.ResultReference,
                },
            }
            : Array.Empty<HandoffBlocker>();

        var hasOutcome = state is CoordinationStates.Partial or CoordinationStates.Failed or
            CoordinationStates.Completed or CoordinationStates.Canceled or CoordinationStates.Stale;
        var outcome = hasOutcome
            ? new CoordinationOutcome
            {
                Status = state,
                Summary = payload.Outcome ?? payload.Reason,
                ResultReference = payload.ResultReference ?? payload.ProgressReference,
                ErrorCode = payload.ErrorCode,
            }
            : null;

        return (blockers, outcome);
    }
}
