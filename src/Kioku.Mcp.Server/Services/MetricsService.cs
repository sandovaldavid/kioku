using System.Collections.Concurrent;
using System.Diagnostics;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Lightweight, opt-in, in-memory metrics and tracing source for Kioku.
/// Metrics are disabled by default; enable them with KIOKU_ENABLE_METRICS=true.
/// Tracing is disabled by default; enable W3C-compatible activities with
/// KIOKU_ENABLE_TRACING=true and configure a listener in the hosting process.
/// This service never records note contents, vault paths, resource keys, or authority scopes.
/// </summary>
public sealed class MetricsService(KiokuConfiguration config)
{
    public const string ActivitySourceName = "Kioku.Coordination";

    public static ActivitySource CoordinationActivitySource { get; } = new(ActivitySourceName);

    private readonly ConcurrentDictionary<string, long> _counters = new();
    private long _toolCallCount;
    private long _coordinationRecoveryCount;
    private long _coordinationRecoveryDurationMilliseconds;
    private long _coordinationRecoveryMaxMilliseconds;

    public bool Enabled => config.EnableMetrics;

    public bool TracingEnabled => config.EnableTracing;

    /// <summary>Records a single invocation of a tool.</summary>
    public void RecordToolCall(string toolName)
    {
        if (!Enabled)
        {
            return;
        }

        _counters.AddOrUpdate(toolName, 1, (_, count) => count + 1);
        Interlocked.Increment(ref _toolCallCount);
    }

    /// <summary>Returns a snapshot of all recorded counters.</summary>
    public IReadOnlyDictionary<string, long> GetSnapshot() => new Dictionary<string, long>(_counters);

    /// <summary>Total number of recorded tool calls.</summary>
    public long TotalCalls => Interlocked.Read(ref _toolCallCount);

    /// <summary>
    /// Starts an optional internal coordination activity with only bounded domain metadata.
    /// </summary>
    public Activity? StartCoordinationActivity(
        string operation,
        string? runId = null,
        string? workItemId = null,
        string? attemptId = null,
        string? sessionId = null,
        string? claimId = null)
    {
        if (!TracingEnabled)
        {
            return null;
        }

        var activity = CoordinationActivitySource.StartActivity(operation, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("kioku.profile.id", KiokuCapabilityCatalog.CoordinationProfileId);
        activity.SetTag("kioku.profile.version", KiokuCapabilityCatalog.CoordinationProfileVersion);
        SetTag(activity, "kioku.run_id", runId);
        SetTag(activity, "kioku.work_item_id", workItemId);
        SetTag(activity, "kioku.attempt_id", attemptId);
        SetTag(activity, "kioku.session_id", sessionId);
        SetTag(activity, "kioku.claim_id", claimId);
        return activity;
    }

    public void RecordCoordinationTransition(string eventType)
    {
        Increment("coordination.transitions.total");
        Increment($"coordination.transitions.{MapEventType(eventType)}");
    }

    public void RecordCoordinationReplay(string outcome)
    {
        Increment("coordination.replay.total");
        Increment($"coordination.replay.{MapReplayOutcome(outcome)}");
    }

    public void RecordCoordinationClaim(string outcome)
    {
        Increment("coordination.claims.total");
        Increment($"coordination.claims.{MapClaimOutcome(outcome)}");
    }

    public void RecordCoordinationMutation(string outcome)
    {
        Increment("coordination.mutations.total");
        Increment($"coordination.mutations.{MapMutationOutcome(outcome)}");
    }

    public void RecordCoordinationRecovery(TimeSpan duration, bool succeeded)
    {
        if (!Enabled)
        {
            return;
        }

        var milliseconds = Math.Max(0, (long)duration.TotalMilliseconds);
        Interlocked.Increment(ref _coordinationRecoveryCount);
        Interlocked.Add(ref _coordinationRecoveryDurationMilliseconds, milliseconds);
        UpdateMaximum(ref _coordinationRecoveryMaxMilliseconds, milliseconds);
        Increment(succeeded ? "coordination.recovery.succeeded" : "coordination.recovery.failed");
    }

    public CoordinationMetricsSnapshot GetCoordinationSnapshot() => new(
        new Dictionary<string, long>(_counters
            .Where(pair => pair.Key.StartsWith("coordination.", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)),
        Interlocked.Read(ref _coordinationRecoveryCount),
        Interlocked.Read(ref _coordinationRecoveryDurationMilliseconds),
        Interlocked.Read(ref _coordinationRecoveryMaxMilliseconds));

    private void Increment(string name)
    {
        if (Enabled)
        {
            _counters.AddOrUpdate(name, 1, (_, count) => count + 1);
        }
    }

    private static void SetTag(Activity activity, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            activity.SetTag(name, value);
        }
    }

    private static string MapEventType(string value) => value switch
    {
        "work-item.created" => "created",
        "work-item.claimed" => "claimed",
        "work-item.started" => "started",
        "work-item.blocked" => "blocked",
        "work-item.partial" => "partial",
        "work-item.failed" => "failed",
        "work-item.completed" => "completed",
        "work-item.canceled" => "canceled",
        "work-item.stale" => "stale",
        "work-item.reopened" => "reopened",
        "work-item.claim.renewed" => "claim-renewed",
        "work-item.claim.released" => "claim-released",
        _ => "other",
    };

    private static string MapReplayOutcome(string value) => value switch
    {
        "replayed" => "succeeded",
        "duplicate" => "duplicate",
        "corrupt-history" => "corrupt-history",
        "projection-corrupt" => "projection-corrupt",
        "invalid-sequence" => "invalid-sequence",
        "access-denied" => "access-denied",
        "unsupported-schema-version" => "unsupported-schema-version",
        _ => "other",
    };

    private static string MapClaimOutcome(string value) => value switch
    {
        "Acquired" => "acquired",
        "Renewed" => "renewed",
        "Released" => "released",
        "Expired" => "expired",
        "TakenOver" => "taken-over",
        "Completed" => "completed",
        "Canceled" => "canceled",
        "Duplicate" => "duplicate",
        "claim-conflict" => "contention",
        "claim-expired" => "expired",
        "claim-fenced" => "fenced",
        "not-owner" => "not-owner",
        _ => "other",
    };

    private static string MapMutationOutcome(string value) => value switch
    {
        "committed" => "committed",
        "WRITE_CONFLICT" or "write-conflict" => "conflict",
        "INVALID_PRECONDITION" or "invalid-precondition" => "conflict",
        "DESTINATION_EXISTS" or "destination-exists" => "conflict",
        "MUTATION_ID_REUSED" or "mutation-id-reused" => "conflict",
        "STALE_FENCE" or "stale-fence" => "stale-fence",
        "ACCESS_DENIED" or "access-denied" => "access-denied",
        "CANCELED" or "canceled" => "canceled",
        _ => "other",
    };

    private static void UpdateMaximum(ref long target, long value)
    {
        while (true)
        {
            var current = Interlocked.Read(ref target);
            if (current >= value || Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}

public sealed record CoordinationMetricsSnapshot(
    IReadOnlyDictionary<string, long> Counters,
    long RecoveryCount,
    long RecoveryDurationMilliseconds,
    long RecoveryMaxMilliseconds);
