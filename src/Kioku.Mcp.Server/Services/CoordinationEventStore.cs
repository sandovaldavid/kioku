using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain.Coordination;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Stable outcomes returned when an event is submitted to the local event store.
/// </summary>
public enum CoordinationAppendDisposition
{
    Appended,
    Duplicate,
}

/// <summary>
/// Stable errors raised by the coordination event store.
/// </summary>
public static class CoordinationStoreErrorCodes
{
    public const string AccessDenied = "access-denied";
    public const string CorruptHistory = "corrupt-history";
    public const string DuplicateEventId = "duplicate-event-id";
    public const string DuplicateTransition = "duplicate-transition";
    public const string InvalidEvent = "invalid-event";
    public const string InvalidSequence = "invalid-sequence";
    public const string ProjectionCorrupt = "projection-corrupt";
    public const string UnsafeIdentifier = "unsafe-identifier";
}

/// <summary>
/// Content-safe exception raised by coordination persistence operations.
/// </summary>
public sealed class CoordinationStoreException(string code)
    : InvalidOperationException($"Coordination store operation failed: {code}.")
{
    public string Code { get; } = code;
}

/// <summary>
/// Result of an append or exact idempotent duplicate.
/// </summary>
public sealed record CoordinationAppendResult(
    CoordinationAppendDisposition Disposition,
    CoordinationEvent Event,
    WorkItemProjection Projection);

/// <summary>
/// Ordered history and rebuilt projection for one work item.
/// </summary>
public sealed record CoordinationReplayResult(
    IReadOnlyList<CoordinationEvent> Events,
    WorkItemProjection? Projection);

/// <summary>
/// Application boundary for local durable coordination history.
/// </summary>
public interface ICoordinationEventStore
{
    Task<CoordinationAppendResult> AppendAsync(
        CoordinationEvent coordinationEvent,
        CancellationToken cancellationToken = default);

    Task<CoordinationReplayResult> ReplayAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default);

    Task<CoordinationReplayResult> RebuildProjectionAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoordinationEvent>> ReadHistoryAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItemProjection>> ListProjectionsAsync(
        string? runId = null,
        string? workItemId = null,
        string? project = null,
        string? state = null,
        CancellationToken cancellationToken = default);

    Task<WorkItemProjection?> ReadProjectionAsync(
        string workItemId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists one immutable JSON file per event and rebuilds projections from event history.
/// </summary>
internal sealed class CoordinationEventStore(
    VaultPathPolicy paths,
    ICoordinationFileSystem fileSystem,
    CoordinationContractValidator validator,
    TimeProvider timeProvider,
    ICoordinationFaultInjector? faultInjector = null,
    MetricsService? metrics = null,
    ILogger<CoordinationEventStore>? logger = null) : ICoordinationEventStore
{
    private const string CoordinationRoot = ".kioku/coordination";
    private const string ManifestFileName = "manifest.json";
    private const int ManifestVersion = 1;

    public async Task<CoordinationAppendResult> AppendAsync(
        CoordinationEvent coordinationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinationEvent);
        using var activity = metrics?.StartCoordinationActivity(
            "coordination.event.append",
            coordinationEvent.RunId,
            coordinationEvent.WorkItemId,
            coordinationEvent.AttemptId,
            coordinationEvent.SessionId,
            coordinationEvent.ClaimId);
        ValidateIdentifier(coordinationEvent.EventId);
        ValidateIdentifier(coordinationEvent.TransitionId);
        ValidateIdentifier(coordinationEvent.RunId);
        ValidateIdentifier(coordinationEvent.WorkItemId);

        var validation = await validator.ValidateAsync(
            CoordinationContractKind.CoordinationEvent,
            coordinationEvent,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.InvalidEvent);
        }

        await using var gate = await AcquireWorkItemLockAsync(coordinationEvent.WorkItemId, cancellationToken)
            .ConfigureAwait(false);
        await EnsureManifestAsync(cancellationToken).ConfigureAwait(false);
        var storedEvents = await ReadStoredEventsAsync(cancellationToken).ConfigureAwait(false);
        var workItemEvents = storedEvents
            .Where(stored => string.Equals(stored.Event.WorkItemId, coordinationEvent.WorkItemId, StringComparison.Ordinal))
            .OrderBy(stored => stored.Event.SequenceNumber)
            .ToArray();
        var candidateJson = CoordinationContractSerializer.Serialize(coordinationEvent);
        var candidateFingerprint = CoordinationEventFingerprint.Compute(coordinationEvent);

        var duplicateId = storedEvents.FirstOrDefault(stored =>
            string.Equals(stored.Event.EventId, coordinationEvent.EventId, StringComparison.Ordinal));
        if (duplicateId is not null)
        {
            if (!string.Equals(duplicateId.Event.WorkItemId, coordinationEvent.WorkItemId, StringComparison.Ordinal) ||
                !string.Equals(duplicateId.Event.RunId, coordinationEvent.RunId, StringComparison.Ordinal) ||
                !string.Equals(duplicateId.CanonicalJson, candidateJson, StringComparison.Ordinal))
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.DuplicateEventId);
            }

            var duplicateProjection = await RebuildAndPersistAsync(
                coordinationEvent.RunId,
                coordinationEvent.WorkItemId,
                storedEvents,
                cancellationToken).ConfigureAwait(false);
            metrics?.RecordCoordinationReplay("duplicate");
            return new(CoordinationAppendDisposition.Duplicate, duplicateId.Event, duplicateProjection);
        }

        var duplicateTransition = storedEvents.FirstOrDefault(stored =>
            string.Equals(stored.Event.TransitionId, coordinationEvent.TransitionId, StringComparison.Ordinal));
        if (duplicateTransition is not null)
        {
            if (!string.Equals(duplicateTransition.Event.WorkItemId, coordinationEvent.WorkItemId, StringComparison.Ordinal) ||
                !string.Equals(duplicateTransition.Event.RunId, coordinationEvent.RunId, StringComparison.Ordinal) ||
                !string.Equals(
                    CoordinationEventFingerprint.Compute(duplicateTransition.Event),
                    candidateFingerprint,
                    StringComparison.Ordinal))
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.DuplicateTransition);
            }

            var transitionProjection = await RebuildAndPersistAsync(
                coordinationEvent.RunId,
                coordinationEvent.WorkItemId,
                storedEvents,
                cancellationToken).ConfigureAwait(false);
            metrics?.RecordCoordinationReplay("duplicate");
            return new(CoordinationAppendDisposition.Duplicate, duplicateTransition.Event, transitionProjection);
        }

        var expectedSequence = workItemEvents.Length == 0
            ? 1
            : workItemEvents[^1].Event.SequenceNumber + 1;
        if (coordinationEvent.SequenceNumber != expectedSequence)
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.InvalidSequence);
        }

        if (workItemEvents.Length > 0 &&
            !string.Equals(coordinationEvent.PreviousHash, workItemEvents[^1].Event.ContentHash, StringComparison.Ordinal))
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.InvalidSequence);
        }

        var allEvents = storedEvents.Select(stored => stored.Event).Append(coordinationEvent).ToArray();
        try
        {
            CoordinationProjectionReducer.Reduce(allEvents
                .Where(eventItem =>
                    string.Equals(eventItem.RunId, coordinationEvent.RunId, StringComparison.Ordinal) &&
                    string.Equals(eventItem.WorkItemId, coordinationEvent.WorkItemId, StringComparison.Ordinal))
                .ToArray());
        }
        catch (CoordinationProjectionException exception)
        {
            throw new CoordinationStoreException(exception.Code);
        }

        var eventPath = GetEventPath(coordinationEvent);
        try
        {
            await fileSystem.WriteNewAtomicallyAsync(eventPath, candidateJson, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (fileSystem.FileExists(eventPath))
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.DuplicateEventId);
        }

        await InjectAsync(
                CoordinationFaultPoint.AfterEventDurabilityBeforeProjection,
                cancellationToken)
            .ConfigureAwait(false);
        var projection = await RebuildAndPersistAsync(
            coordinationEvent.RunId,
            coordinationEvent.WorkItemId,
            allEvents.Select(eventItem => new StoredEvent(eventItem, CoordinationContractSerializer.Serialize(eventItem))).ToArray(),
            cancellationToken).ConfigureAwait(false);
        metrics?.RecordCoordinationTransition(coordinationEvent.EventType);
        logger?.Info(
            "Coordination event accepted. RunId={RunId} WorkItemId={WorkItemId} EventType={EventType} SequenceNumber={SequenceNumber}.",
            coordinationEvent.RunId,
            coordinationEvent.WorkItemId,
            coordinationEvent.EventType,
            coordinationEvent.SequenceNumber);
        return new(CoordinationAppendDisposition.Appended, coordinationEvent, projection);
    }

    public Task<CoordinationReplayResult> ReplayAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default) =>
        RebuildProjectionAsync(runId, workItemId, cancellationToken);

    public async Task<CoordinationReplayResult> RebuildProjectionAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(runId);
        ValidateIdentifier(workItemId);
        using var activity = metrics?.StartCoordinationActivity(
            "coordination.history.replay",
            runId,
            workItemId);
        var startedAt = timeProvider.GetTimestamp();
        var succeeded = false;
        try
        {
            await using var gate = await AcquireWorkItemLockAsync(workItemId, cancellationToken).ConfigureAwait(false);
            await EnsureManifestAsync(cancellationToken).ConfigureAwait(false);
            var storedEvents = await ReadStoredEventsAsync(cancellationToken).ConfigureAwait(false);
            var result = await RebuildAndReturnAsync(runId, workItemId, storedEvents, cancellationToken)
                .ConfigureAwait(false);
            metrics?.RecordCoordinationReplay("replayed");
            succeeded = true;
            return result;
        }
        catch (CoordinationStoreException exception)
        {
            metrics?.RecordCoordinationReplay(exception.Code);
            logger?.Warn(
                "Coordination replay failed. RunId={RunId} WorkItemId={WorkItemId} Code={Code}.",
                runId,
                workItemId,
                exception.Code);
            throw;
        }
        finally
        {
            metrics?.RecordCoordinationRecovery(timeProvider.GetElapsedTime(startedAt), succeeded);
        }
    }

    public async Task<IReadOnlyList<CoordinationEvent>> ReadHistoryAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(runId);
        ValidateIdentifier(workItemId);
        await using var gate = await AcquireWorkItemLockAsync(workItemId, cancellationToken).ConfigureAwait(false);
        await EnsureManifestAsync(cancellationToken).ConfigureAwait(false);
        var events = (await ReadStoredEventsAsync(cancellationToken).ConfigureAwait(false))
            .Where(stored =>
                string.Equals(stored.Event.RunId, runId, StringComparison.Ordinal) &&
                string.Equals(stored.Event.WorkItemId, workItemId, StringComparison.Ordinal))
            .OrderBy(stored => stored.Event.SequenceNumber)
            .Select(stored => stored.Event)
            .ToArray();

        if (events.Length > 0)
        {
            try
            {
                CoordinationProjectionReducer.Reduce(events);
            }
            catch (CoordinationProjectionException exception)
            {
                throw new CoordinationStoreException(exception.Code);
            }
        }

        return events;
    }

    public async Task<IReadOnlyList<WorkItemProjection>> ListProjectionsAsync(
        string? runId = null,
        string? workItemId = null,
        string? project = null,
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        if (runId is not null)
        {
            ValidateIdentifier(runId);
        }

        if (workItemId is not null)
        {
            ValidateIdentifier(workItemId);
        }

        await EnsureManifestAsync(cancellationToken).ConfigureAwait(false);
        var snapshotRoot = paths.ResolveVaultReadPath(Path.Combine(CoordinationRoot, "snapshots", "work-items"));
        var projections = new List<WorkItemProjection>();
        foreach (var candidatePath in fileSystem.EnumerateJsonFiles(snapshotRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var safePath = paths.ResolveVaultReadPath(candidatePath);
                var json = await fileSystem.ReadAllTextAsync(safePath, cancellationToken).ConfigureAwait(false);
                var validation = await validator.ValidateAsync(
                    CoordinationContractKind.WorkItemProjection,
                    json,
                    cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
                }

                var projection = JsonSerializer.Deserialize(
                    json,
                    CoordinationJsonContext.Default.WorkItemProjection)
                    ?? throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
                if ((runId is null || string.Equals(projection.RunId, runId, StringComparison.Ordinal)) &&
                    (workItemId is null || string.Equals(projection.WorkItemId, workItemId, StringComparison.Ordinal)) &&
                    (project is null || string.Equals(projection.Project, project, StringComparison.Ordinal)) &&
                    (state is null || string.Equals(projection.State, state, StringComparison.Ordinal)))
                {
                    projections.Add(projection);
                }
            }
            catch (CoordinationStoreException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
            }
            catch (VaultAccessDeniedException)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.AccessDenied);
            }
            catch (IOException)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
            }
        }

        return projections
            .OrderByDescending(projection => projection.UpdatedAt)
            .ThenBy(projection => projection.WorkItemId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<WorkItemProjection?> ReadProjectionAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(workItemId);
        var projectionPath = GetProjectionPath(workItemId);
        if (!fileSystem.FileExists(projectionPath))
        {
            return null;
        }

        try
        {
            var json = await fileSystem.ReadAllTextAsync(projectionPath, cancellationToken).ConfigureAwait(false);
            var validation = await validator.ValidateAsync(
                CoordinationContractKind.WorkItemProjection,
                json,
                cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
            }

            return JsonSerializer.Deserialize(
                json,
                CoordinationJsonContext.Default.WorkItemProjection)
                ?? throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
        }
        catch (JsonException)
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.ProjectionCorrupt);
        }
    }

    private async Task<CoordinationReplayResult> RebuildAndReturnAsync(
        string runId,
        string workItemId,
        IReadOnlyList<StoredEvent> storedEvents,
        CancellationToken cancellationToken)
    {
        var events = storedEvents
            .Where(stored =>
                string.Equals(stored.Event.RunId, runId, StringComparison.Ordinal) &&
                string.Equals(stored.Event.WorkItemId, workItemId, StringComparison.Ordinal))
            .OrderBy(stored => stored.Event.SequenceNumber)
            .Select(stored => stored.Event)
            .ToArray();
        if (events.Length == 0)
        {
            return new([], null);
        }

        WorkItemProjection projection;
        try
        {
            projection = CoordinationProjectionReducer.Reduce(events);
        }
        catch (CoordinationProjectionException exception)
        {
            throw new CoordinationStoreException(exception.Code);
        }

        await InjectAsync(CoordinationFaultPoint.DuringProjectionReplacement, cancellationToken)
            .ConfigureAwait(false);
        await fileSystem.WriteAtomicallyAsync(
            GetProjectionPath(workItemId),
            CoordinationContractSerializer.Serialize(projection),
            cancellationToken).ConfigureAwait(false);
        return new(events, projection);
    }

    private async Task<WorkItemProjection> RebuildAndPersistAsync(
        string runId,
        string workItemId,
        IReadOnlyList<StoredEvent> storedEvents,
        CancellationToken cancellationToken)
    {
        var result = await RebuildAndReturnAsync(runId, workItemId, storedEvents, cancellationToken)
            .ConfigureAwait(false);
        return result.Projection
            ?? throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
    }

    private async Task<IReadOnlyList<StoredEvent>> ReadStoredEventsAsync(CancellationToken cancellationToken)
    {
        var eventRoot = paths.ResolveVaultReadPath(Path.Combine(CoordinationRoot, "events"));
        var eventPaths = fileSystem.EnumerateJsonFiles(eventRoot);
        var storedEvents = new List<StoredEvent>(eventPaths.Count);
        foreach (var candidatePath in eventPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string json;
            try
            {
                var safePath = paths.ResolveVaultReadPath(candidatePath);
                json = await fileSystem.ReadAllTextAsync(safePath, cancellationToken).ConfigureAwait(false);
                var validation = await validator.ValidateAsync(
                    CoordinationContractKind.CoordinationEvent,
                    json,
                    cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
                }

                using var document = JsonDocument.Parse(json);
                var coordinationEvent = JsonSerializer.Deserialize(
                    document.RootElement.GetRawText(),
                    CoordinationJsonContext.Default.CoordinationEvent)
                    ?? throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
                storedEvents.Add(new(
                    coordinationEvent,
                    CanonicalJson.Serialize(document.RootElement)));
            }
            catch (CoordinationStoreException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
            }
            catch (VaultAccessDeniedException)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.AccessDenied);
            }
            catch (IOException)
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
            }
        }

        if (storedEvents
            .GroupBy(stored => stored.Event.EventId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.DuplicateEventId);
        }

        if (storedEvents
            .GroupBy(stored => stored.Event.TransitionId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.DuplicateTransition);
        }

        return storedEvents;
    }

    private async Task EnsureManifestAsync(CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath();
        if (fileSystem.FileExists(manifestPath))
        {
            await ValidateManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        var eventRoot = paths.ResolveVaultReadPath(Path.Combine(CoordinationRoot, "events"));
        if (fileSystem.EnumerateJsonFiles(eventRoot).Count > 0)
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
        }

        var manifest = CreateManifest();
        try
        {
            await fileSystem.WriteNewAtomicallyAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (fileSystem.FileExists(manifestPath))
        {
        }

        await ValidateManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await fileSystem.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.GetProperty("format_version").GetInt32() != ManifestVersion ||
                string.IsNullOrWhiteSpace(root.GetProperty("coordination_epoch").GetString()) ||
                !CanonicalJson.ContentHashMatches(root, "content_hash"))
            {
                throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
            }
        }
        catch (CoordinationStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.CorruptHistory);
        }
    }

    private string CreateManifest()
    {
        var node = new JsonObject
        {
            ["format_version"] = ManifestVersion,
            ["coordination_epoch"] = Guid.CreateVersion7().ToString("D"),
            ["created_at"] = timeProvider.GetUtcNow().ToUniversalTime().ToString("O"),
            ["content_hash"] = string.Empty,
        };
        using var document = JsonDocument.Parse(node.ToJsonString());
        node["content_hash"] = CanonicalJson.ComputeSha256Hex(document.RootElement, "content_hash");
        using var completed = JsonDocument.Parse(node.ToJsonString());
        return CanonicalJson.Serialize(completed.RootElement);
    }

    private async Task<FileStream> AcquireWorkItemLockAsync(
        string workItemId,
        CancellationToken cancellationToken)
    {
        var lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workItemId)));
        try
        {
            return await fileSystem.AcquireExclusiveLockAsync(
                paths.ResolveVaultWritePath(Path.Combine(CoordinationRoot, "runtime", "locks", $"{lockName}.lock")),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VaultAccessDeniedException)
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.AccessDenied);
        }
    }

    private string GetManifestPath() =>
        paths.ResolveVaultWritePath(Path.Combine(CoordinationRoot, ManifestFileName));

    private string GetProjectionPath(string workItemId) =>
        paths.ResolveVaultWritePath(Path.Combine(
            CoordinationRoot,
            "snapshots",
            "work-items",
            $"{workItemId}.json"));

    private string GetEventPath(CoordinationEvent coordinationEvent) =>
        paths.ResolveVaultWritePath(Path.Combine(
            CoordinationRoot,
            "events",
            coordinationEvent.RecordedAt.UtcDateTime.ToString("yyyy"),
            coordinationEvent.RecordedAt.UtcDateTime.ToString("MM"),
            $"{coordinationEvent.EventId}.json"));

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new CoordinationStoreException(CoordinationStoreErrorCodes.UnsafeIdentifier);
        }
    }

    private Task InjectAsync(
        CoordinationFaultPoint point,
        CancellationToken cancellationToken) =>
        (faultInjector ?? NoOpCoordinationFaultInjector.Instance).InjectAsync(point, cancellationToken);

    private sealed record StoredEvent(CoordinationEvent Event, string CanonicalJson);
}
