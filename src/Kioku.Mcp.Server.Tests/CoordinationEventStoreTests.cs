using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CoordinationEventStoreTests : IDisposable
{
    private readonly string _vaultPath = Path.Combine(
        Path.GetTempPath(),
        $"kioku-coordination-{Guid.NewGuid():N}");
    private readonly CoordinationEventStore _store;

    public CoordinationEventStoreTests()
    {
        Directory.CreateDirectory(_vaultPath);
        var configuration = new KiokuConfiguration { VaultPath = _vaultPath };
        _store = new CoordinationEventStore(
            new VaultPathPolicy(configuration),
            new CoordinationFileSystem(),
            new CoordinationContractValidator(),
            TimeProvider.System);
    }

    [Fact]
    public async Task AppendAndReplayRebuildsTheSameProjection()
    {
        var history = CreateCompletedHistory();
        foreach (var coordinationEvent in history)
        {
            var result = await _store.AppendAsync(coordinationEvent);

            Assert.Equal(CoordinationAppendDisposition.Appended, result.Disposition);
        }

        var firstReplay = await _store.ReplayAsync("run-01", "work-01");
        var secondReplay = await _store.ReplayAsync("run-01", "work-01");

        Assert.Equal(4, firstReplay.Events.Count);
        Assert.NotNull(firstReplay.Projection);
        Assert.Equal(CoordinationStates.Completed, firstReplay.Projection!.State);
        Assert.Equal(3, firstReplay.Projection.StateVersion);
        Assert.Equal(
            CoordinationContractSerializer.Serialize(firstReplay.Projection),
            CoordinationContractSerializer.Serialize(secondReplay.Projection!));

        var eventFiles = Directory.GetFiles(
            Path.Combine(_vaultPath, ".kioku", "coordination", "events"),
            "*.json",
            SearchOption.AllDirectories);
        Assert.Equal(4, eventFiles.Length);
        Assert.Empty(Directory.GetFiles(_vaultPath, "*.md", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExactDuplicateEventIsAStableNoOp()
    {
        var coordinationEvent = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);

        await _store.AppendAsync(coordinationEvent);
        var duplicate = await _store.AppendAsync(coordinationEvent);
        var replay = await _store.ReplayAsync("run-01", "work-01");

        Assert.Equal(CoordinationAppendDisposition.Duplicate, duplicate.Disposition);
        Assert.Equal(coordinationEvent.EventId, duplicate.Event.EventId);
        Assert.Single(replay.Events);
    }

    [Fact]
    public async Task ConflictingDuplicateEventIdIsRejected()
    {
        var original = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var conflicting = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0,
            reason: "A different transition payload.");

        await _store.AppendAsync(original);
        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.AppendAsync(conflicting));

        Assert.Equal(CoordinationStoreErrorCodes.DuplicateEventId, exception.Code);
    }

    [Fact]
    public async Task ConflictingDuplicateTransitionIsRejected()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var claimed = CreateEvent(
            eventId: "event-02",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemClaimed,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Claimed,
            previousHash: created.ContentHash,
            stateVersion: 1,
            transitionId: "transition-02");
        var conflicting = CreateEvent(
            eventId: "event-03",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemClaimed,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Claimed,
            previousHash: created.ContentHash,
            stateVersion: 1,
            transitionId: "transition-02",
            reason: "A different transition payload.");

        await _store.AppendAsync(created);
        await _store.AppendAsync(claimed);
        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.AppendAsync(conflicting));

        Assert.Equal(CoordinationStoreErrorCodes.DuplicateTransition, exception.Code);
    }

    [Fact]
    public async Task SequenceGapsAndUnsafeIdentifiersAreRejected()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var gap = CreateEvent(
            eventId: "event-03",
            sequence: 3,
            eventType: CoordinationEventTypes.WorkItemClaimed,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Claimed,
            previousHash: created.ContentHash,
            stateVersion: 1);

        await _store.AppendAsync(created);
        var sequenceException = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.AppendAsync(gap));
        var unsafeEvent = CreateEvent(
            eventId: "../escape",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemClaimed,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Claimed,
            previousHash: created.ContentHash,
            stateVersion: 1);
        var identifierException = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.AppendAsync(unsafeEvent));

        Assert.Equal(CoordinationStoreErrorCodes.InvalidSequence, sequenceException.Code);
        Assert.Equal(CoordinationStoreErrorCodes.UnsafeIdentifier, identifierException.Code);
        Assert.False(File.Exists(Path.Combine(_vaultPath, "escape.json")));
    }

    [Fact]
    public async Task InvalidTransitionIsRejectedBeforeTheEventIsPersisted()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var invalid = CreateEvent(
            eventId: "event-02",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemStarted,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Running,
            previousHash: created.ContentHash,
            stateVersion: 1);

        await _store.AppendAsync(created);
        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.AppendAsync(invalid));
        var replay = await _store.ReplayAsync("run-01", "work-01");

        Assert.Equal(CoordinationProjectionErrorCodes.InvalidTransition, exception.Code);
        Assert.Single(replay.Events);
        Assert.DoesNotContain(
            Directory.GetFiles(
                Path.Combine(_vaultPath, ".kioku", "coordination", "events"),
                "*.json",
                SearchOption.AllDirectories),
            path => Path.GetFileName(path) == "event-02.json");
    }

    [Fact]
    public async Task MissingProjectionIsRebuiltAndCorruptProjectionIsRejected()
    {
        var history = CreateCompletedHistory();
        foreach (var coordinationEvent in history)
        {
            await _store.AppendAsync(coordinationEvent);
        }

        var projectionPath = Path.Combine(
            _vaultPath,
            ".kioku",
            "coordination",
            "snapshots",
            "work-items",
            "work-01.json");
        var expected = await _store.ReadProjectionAsync("work-01");
        Assert.NotNull(expected);
        File.Delete(projectionPath);

        var rebuilt = await _store.RebuildProjectionAsync("run-01", "work-01");
        Assert.NotNull(rebuilt.Projection);
        Assert.Equal(
            CoordinationContractSerializer.Serialize(expected!),
            CoordinationContractSerializer.Serialize(rebuilt.Projection!));

        await File.WriteAllTextAsync(projectionPath, "{\"corrupt\":true}");
        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.ReadProjectionAsync("work-01"));

        Assert.Equal(CoordinationStoreErrorCodes.ProjectionCorrupt, exception.Code);
    }

    [Fact]
    public async Task CorruptEventHistoryFailsClosedWithoutDeletingTheEvent()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        await _store.AppendAsync(created);

        var eventPath = Directory.GetFiles(
            Path.Combine(_vaultPath, ".kioku", "coordination", "events"),
            "event-01.json",
            SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(eventPath, "{\"truncated\":");

        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.ReplayAsync("run-01", "work-01"));

        Assert.Equal(CoordinationStoreErrorCodes.CorruptHistory, exception.Code);
        Assert.True(File.Exists(eventPath));
    }

    [Fact]
    public async Task CancellationAfterEventWriteLeavesReplayableHistory()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var interruptedStore = new CoordinationEventStore(
            new VaultPathPolicy(new KiokuConfiguration { VaultPath = _vaultPath }),
            new CancelProjectionFileSystem(),
            new CoordinationContractValidator(),
            TimeProvider.System);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => interruptedStore.AppendAsync(created));

        var replay = await _store.ReplayAsync("run-01", "work-01");

        Assert.Single(replay.Events);
        Assert.Equal(CoordinationStates.Pending, replay.Projection!.State);
    }

    [Fact]
    public async Task CorruptManifestPreventsReplayWithoutDeletingHistory()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        await _store.AppendAsync(created);

        var manifestPath = Path.Combine(_vaultPath, ".kioku", "coordination", "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{\"format_version\":1}");

        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.ReplayAsync("run-01", "work-01"));

        Assert.Equal(CoordinationStoreErrorCodes.CorruptHistory, exception.Code);
        Assert.True(File.Exists(Path.Combine(
            _vaultPath,
            ".kioku",
            "coordination",
            "events",
            "2026",
            "07",
            "event-01.json")));
    }

    [Fact]
    public async Task OutOfOrderPersistedEventsAreRejectedDuringReplay()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var claimed = CreateEvent(
            eventId: "event-02",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemClaimed,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Claimed,
            previousHash: created.ContentHash,
            stateVersion: 1);
        var gap = CreateEvent(
            eventId: "event-04",
            sequence: 4,
            eventType: CoordinationEventTypes.WorkItemStarted,
            previousState: CoordinationStates.Claimed,
            nextState: CoordinationStates.Running,
            previousHash: claimed.ContentHash,
            stateVersion: 2);

        await _store.AppendAsync(created);
        await _store.AppendAsync(claimed);
        var gapPath = Path.Combine(
            _vaultPath,
            ".kioku",
            "coordination",
            "events",
            gap.RecordedAt.UtcDateTime.ToString("yyyy", CultureInfo.InvariantCulture),
            gap.RecordedAt.UtcDateTime.ToString("MM", CultureInfo.InvariantCulture),
            $"{gap.EventId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(gapPath)!);
        await File.WriteAllTextAsync(gapPath, CoordinationContractSerializer.Serialize(gap));

        var exception = await Assert.ThrowsAsync<CoordinationStoreException>(
            () => _store.ReplayAsync("run-01", "work-01"));

        Assert.Equal(CoordinationStoreErrorCodes.InvalidSequence, exception.Code);
    }

    [Fact]
    public void ReducerIsDeterministicAndRejectsInvalidTransitions()
    {
        var history = CreateCompletedHistory();
        var reversed = history.Reverse().ToArray();

        var projection = CoordinationProjectionReducer.Reduce(reversed);

        Assert.Equal(CoordinationStates.Completed, projection.State);
        Assert.Equal(3, projection.StateVersion);
        Assert.Equal("event-04", projection.LastEventId);
        Assert.Equal("success", projection.Outcome!.Summary);

        var invalidTransition = CreateEvent(
            eventId: "event-02",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemStarted,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Running,
            previousHash: history[0].ContentHash,
            stateVersion: 1);
        var exception = Assert.Throws<CoordinationProjectionException>(() =>
            CoordinationProjectionReducer.Reduce([history[0], invalidTransition]));

        Assert.Equal(CoordinationProjectionErrorCodes.InvalidTransition, exception.Code);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_vaultPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static CoordinationEvent[] CreateCompletedHistory()
    {
        var created = CreateEvent(
            eventId: "event-01",
            sequence: 1,
            eventType: CoordinationEventTypes.WorkItemCreated,
            previousState: null,
            nextState: CoordinationStates.Pending,
            previousHash: null,
            stateVersion: 0);
        var claimed = CreateEvent(
            eventId: "event-02",
            sequence: 2,
            eventType: CoordinationEventTypes.WorkItemClaimed,
            previousState: CoordinationStates.Pending,
            nextState: CoordinationStates.Claimed,
            previousHash: created.ContentHash,
            stateVersion: 1);
        var started = CreateEvent(
            eventId: "event-03",
            sequence: 3,
            eventType: CoordinationEventTypes.WorkItemStarted,
            previousState: CoordinationStates.Claimed,
            nextState: CoordinationStates.Running,
            previousHash: claimed.ContentHash,
            stateVersion: 2);
        var completed = CreateEvent(
            eventId: "event-04",
            sequence: 4,
            eventType: CoordinationEventTypes.WorkItemCompleted,
            previousState: CoordinationStates.Running,
            nextState: CoordinationStates.Completed,
            previousHash: started.ContentHash,
            stateVersion: 3,
            outcome: "success",
            resultReference: "artifact:result-01");
        return [created, claimed, started, completed];
    }

    private static CoordinationEvent CreateEvent(
        string eventId,
        long sequence,
        string eventType,
        string? previousState,
        string nextState,
        string? previousHash,
        long stateVersion,
        string? transitionId = null,
        string reason = "Coordination transition.",
        string? outcome = null,
        string? resultReference = null)
    {
        var occurredAt = new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero)
            .AddSeconds(sequence);
        var unhashed = new CoordinationEvent
        {
            EventId = eventId,
            RunId = "run-01",
            WorkItemId = "work-01",
            Project = "example-project",
            ResourceScope = ["note:Notes/Plan.md"],
            AttemptId = sequence > 1 ? "attempt-01" : null,
            SessionId = "session-01",
            ClaimId = sequence is 2 or 3 ? "claim-01" : null,
            SequenceNumber = sequence,
            EventType = eventType,
            TransitionId = transitionId ?? $"transition-{sequence:00}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
            Actor = new CoordinationActor
            {
                Agent = "agent-a",
                ClientName = "client-a",
                SessionId = "session-01",
            },
            Payload = new CoordinationTransitionPayload
            {
                PreviousState = previousState,
                NextState = nextState,
                ExpectedStateVersion = sequence > 1 ? stateVersion - 1 : null,
                StateVersion = stateVersion,
                Reason = reason,
                Outcome = outcome,
                ResultReference = resultReference,
            },
            PreviousHash = previousHash,
            ContentHash = string.Empty,
        };

        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(unhashed))!.AsObject();
        using var document = JsonDocument.Parse(node.ToJsonString());
        node[CoordinationContract.ContentHashPropertyName] = CanonicalJson.ComputeSha256Hex(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName);
        return JsonSerializer.Deserialize(
            node.ToJsonString(),
            CoordinationJsonContext.Default.CoordinationEvent)!;
    }

    private sealed class CancelProjectionFileSystem : ICoordinationFileSystem
    {
        private readonly CoordinationFileSystem _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
            _inner.MoveFile(sourcePath, destinationPath, overwrite);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
            _inner.ReadAllTextAsync(path, cancellationToken);

        public IReadOnlyList<string> EnumerateJsonFiles(string directory) =>
            _inner.EnumerateJsonFiles(directory);

        public Task WriteNewAtomicallyAsync(string path, string content, CancellationToken cancellationToken) =>
            _inner.WriteNewAtomicallyAsync(path, content, cancellationToken);

        public Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken) =>
            throw new OperationCanceledException("Simulated interruption before projection replacement.");

        public Task<FileStream> AcquireExclusiveLockAsync(string path, CancellationToken cancellationToken) =>
            _inner.AcquireExclusiveLockAsync(path, cancellationToken);
    }
}
