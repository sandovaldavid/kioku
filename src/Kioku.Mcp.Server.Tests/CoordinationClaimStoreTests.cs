using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CoordinationClaimStoreTests : IDisposable
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    private readonly string _vaultPath = Path.Combine(
        Path.GetTempPath(),
        $"kioku-claims-{Guid.NewGuid():N}");
    private readonly ManualTimeProvider _time = new(StartTime);
    private readonly CoordinationEventStore _events;
    private readonly CoordinationClaimStore _claims;

    public CoordinationClaimStoreTests()
    {
        Directory.CreateDirectory(_vaultPath);
        _events = CreateEventStore(_time);
        _claims = CreateClaimStore(_time);
    }

    [Fact]
    public async Task ConcurrentAcquiresProduceOneOwnerAndOneClaimEvent()
    {
        await CreateWorkItemAsync("run-01", "work-01");
        await CreateWorkItemAsync("run-02", "work-02");
        var otherClaims = CreateClaimStore(new ManualTimeProvider(StartTime));

        var outcomes = await Task.WhenAll(
            TryAcquireAsync(_claims, CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01")),
            TryAcquireAsync(otherClaims, CreateAcquireRequest("run-02", "work-02", "attempt-02", "session-02", "transition-02")));

        Assert.Single(outcomes, outcome => outcome.Result is not null);
        Assert.Single(outcomes, outcome => outcome.Error?.Code == CoordinationClaimErrorCodes.ClaimConflict);

        var firstHistory = await _events.ReplayAsync("run-01", "work-01");
        var secondHistory = await _events.ReplayAsync("run-02", "work-02");
        Assert.Equal(
            1,
            firstHistory.Events.Concat(secondHistory.Events)
                .Count(item => item.EventType == CoordinationEventTypes.WorkItemClaimed));
    }

    [Fact]
    public async Task AcquireRenewReleaseAreIdempotent()
    {
        await CreateWorkItemAsync();
        var acquire = CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01");
        var acquired = await _claims.AcquireAsync(acquire);
        var duplicateAcquire = await _claims.AcquireAsync(acquire);
        var renew = CreateMutation(acquired.Claim, "transition-02", TimeSpan.FromSeconds(10));
        var renewed = await _claims.RenewAsync(renew);
        var duplicateRenew = await _claims.RenewAsync(renew);
        var release = CreateMutation(renewed.Claim, "transition-03");
        var released = await _claims.ReleaseAsync(release);
        var duplicateRelease = await _claims.ReleaseAsync(release);
        var replay = await _events.ReplayAsync("run-01", "work-01");

        Assert.Equal(CoordinationClaimDisposition.Acquired, acquired.Disposition);
        Assert.Equal(CoordinationClaimDisposition.Duplicate, duplicateAcquire.Disposition);
        Assert.Equal(CoordinationClaimDisposition.Renewed, renewed.Disposition);
        Assert.Equal(CoordinationClaimDisposition.Duplicate, duplicateRenew.Disposition);
        Assert.Equal(CoordinationClaimDisposition.Released, released.Disposition);
        Assert.Equal(CoordinationClaimDisposition.Duplicate, duplicateRelease.Disposition);
        Assert.Equal(CoordinationClaimStatuses.Released, released.Claim.Status);
        Assert.Equal(CoordinationStates.Pending, replay.Projection!.State);
        Assert.Equal(4, replay.Events.Count);
    }

    [Fact]
    public async Task ConcurrentRenewalsWithSameTransitionAreIdempotent()
    {
        await CreateWorkItemAsync();
        var acquired = await _claims.AcquireAsync(
            CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01"));
        var restarted = CreateClaimStore(new ManualTimeProvider(_time.GetUtcNow()));
        var renewal = CreateMutation(acquired.Claim, "transition-02", TimeSpan.FromSeconds(10));

        var outcomes = await Task.WhenAll(
            _claims.RenewAsync(renewal),
            restarted.RenewAsync(renewal));
        var replay = await _events.ReplayAsync("run-01", "work-01");

        Assert.Single(outcomes, outcome => outcome.Disposition == CoordinationClaimDisposition.Renewed);
        Assert.Single(outcomes, outcome => outcome.Disposition == CoordinationClaimDisposition.Duplicate);
        Assert.All(outcomes, outcome => Assert.Equal(StartTime.AddSeconds(10), outcome.Claim.LeaseExpiresAt));
        Assert.Equal(3, replay.Events.Count);
    }

    [Fact]
    public async Task ExactLeaseExpiryFencesRenewAndPersistsStaleState()
    {
        await CreateWorkItemAsync();
        var acquired = await _claims.AcquireAsync(
            CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01",
                TimeSpan.FromSeconds(5)));
        _time.Advance(TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<CoordinationClaimException>(() =>
            _claims.RenewAsync(CreateMutation(acquired.Claim, "transition-02")));
        var current = await _claims.ReadAsync("note:Notes/Plan.md");
        var replay = await _events.ReplayAsync("run-01", "work-01");

        Assert.Equal(CoordinationClaimErrorCodes.ClaimExpired, exception.Code);
        Assert.Equal(CoordinationClaimStatuses.Expired, current!.Status);
        Assert.Equal(CoordinationStates.Stale, replay.Projection!.State);
        Assert.Equal(CoordinationEventTypes.WorkItemStale, replay.Events[^1].EventType);
    }

    [Fact]
    public async Task TakeoverAdvancesFenceAndRejectsTheStaleOwner()
    {
        await CreateWorkItemAsync();
        var first = await _claims.AcquireAsync(
            CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01",
                TimeSpan.FromSeconds(5)));
        _time.Advance(TimeSpan.FromSeconds(6));

        var second = await _claims.TakeoverAsync(
            CreateAcquireRequest("run-01", "work-01", "attempt-02", "session-02", "transition-02"));
        var exception = await Assert.ThrowsAsync<CoordinationClaimException>(() =>
            _claims.RenewAsync(CreateMutation(first.Claim, "transition-old")));
        var replay = await _events.ReplayAsync("run-01", "work-01");

        Assert.Equal(CoordinationClaimDisposition.TakenOver, second.Disposition);
        Assert.Equal(2, second.Claim.FenceGeneration);
        Assert.Equal(CoordinationClaimErrorCodes.ClaimFenced, exception.Code);
        Assert.Equal(CoordinationStates.Claimed, replay.Projection!.State);
        Assert.Equal(5, replay.Events.Count);
        Assert.Equal(CoordinationEventTypes.WorkItemReopened, replay.Events[^2].EventType);
    }

    [Fact]
    public async Task RestartPreservesTheActiveClaimAndRemainingLease()
    {
        await CreateWorkItemAsync();
        var acquired = await _claims.AcquireAsync(
            CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01",
                TimeSpan.FromSeconds(30)));
        _time.Advance(TimeSpan.FromSeconds(10));
        var restarted = CreateClaimStore(new ManualTimeProvider(_time.GetUtcNow()));

        var current = await restarted.ReadAsync("note:Notes/Plan.md");
        var renewed = await restarted.RenewAsync(CreateMutation(acquired.Claim, "transition-02"));

        Assert.Equal(CoordinationClaimStatuses.Active, current!.Status);
        Assert.Equal(StartTime.AddSeconds(30), current.LeaseExpiresAt);
        Assert.Equal(CoordinationClaimStatuses.Active, renewed.Claim.Status);
        Assert.Equal(StartTime.AddSeconds(40), renewed.Claim.LeaseExpiresAt);
    }

    [Fact]
    public async Task CompleteAndCancelReleaseClaimsAndReachTerminalStates()
    {
        await CreateWorkItemAsync("run-01", "work-01");
        await CreateWorkItemAsync("run-02", "work-02");
        var completed = await _claims.AcquireAsync(
            CreateAcquireRequest("run-01", "work-01", "attempt-01", "session-01", "transition-01"));
        var canceled = await _claims.AcquireAsync(
            CreateAcquireRequest(
                "run-02",
                "work-02",
                "attempt-02",
                "session-02",
                "transition-02",
                resourceKey: "note:Notes/Other.md"));

        var completeResult = await _claims.CompleteAsync(CreateMutation(completed.Claim, "transition-03"));
        var cancelResult = await _claims.CancelAsync(CreateMutation(canceled.Claim, "transition-04"));
        var completedHistory = await _events.ReplayAsync("run-01", "work-01");
        var canceledHistory = await _events.ReplayAsync("run-02", "work-02");

        Assert.Equal(CoordinationClaimDisposition.Completed, completeResult.Disposition);
        Assert.Equal(CoordinationClaimDisposition.Canceled, cancelResult.Disposition);
        Assert.Equal(CoordinationClaimStatuses.Released, completeResult.Claim.Status);
        Assert.Equal(CoordinationClaimStatuses.Released, cancelResult.Claim.Status);
        Assert.Equal(CoordinationStates.Completed, completedHistory.Projection!.State);
        Assert.Equal(CoordinationStates.Canceled, canceledHistory.Projection!.State);
    }

    [Fact]
    public async Task InvalidDurationsPathsAndAuthorityScopesAreRejected()
    {
        await CreateWorkItemAsync();

        var durationException = await Assert.ThrowsAsync<CoordinationClaimException>(() =>
            _claims.AcquireAsync(CreateAcquireRequest(
                "run-01", "work-01", "attempt-01", "session-01", "transition-01",
                CoordinationClaimLeasePolicy.MinimumDuration - TimeSpan.FromMilliseconds(1))));
        var pathException = await Assert.ThrowsAsync<CoordinationClaimException>(() =>
            _claims.AcquireAsync(CreateAcquireRequest(
                "run-01", "work-01", "attempt-01", "session-01", "transition-02",
                authorityScope: [], resourceKey: "note:../outside.md")));
        var authorityException = await Assert.ThrowsAsync<CoordinationClaimException>(() =>
            _claims.AcquireAsync(new CoordinationClaimAcquireRequest
            {
                RunId = "run-01",
                WorkItemId = "work-01",
                AttemptId = "attempt-01",
                SessionId = "session-01",
                ResourceKey = "note:Notes/Plan.md",
                TransitionId = "transition-03",
                AuthorityScope = [CoordinationAuthorityScopes.Write],
            }));

        Assert.Equal(CoordinationClaimErrorCodes.InvalidDuration, durationException.Code);
        Assert.Equal(CoordinationClaimErrorCodes.InvalidResource, pathException.Code);
        Assert.Equal(CoordinationClaimErrorCodes.AuthorityScopeDenied, authorityException.Code);
    }

    [Fact]
    public async Task CorruptLeaseStateFailsClosed()
    {
        await CreateWorkItemAsync();
        var leasePath = Path.Combine(
            _vaultPath,
            ".kioku",
            "coordination",
            "leases",
            $"{HashResourceKey("note:Notes/Plan.md")}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
        await File.WriteAllTextAsync(leasePath, "{\"status\":\"active\"}");

        var exception = await Assert.ThrowsAsync<CoordinationClaimException>(() =>
            _claims.AcquireAsync(CreateAcquireRequest(
                "run-01", "work-01", "attempt-01", "session-01", "transition-01")));

        Assert.Equal(CoordinationClaimErrorCodes.CorruptClaimState, exception.Code);
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

    private async Task CreateWorkItemAsync(string runId = "run-01", string workItemId = "work-01")
    {
        var coordinationEvent = CreateEvent(runId, workItemId);
        await _events.AppendAsync(coordinationEvent);
    }

    private CoordinationEventStore CreateEventStore(TimeProvider timeProvider) =>
        new(
            new VaultPathPolicy(new KiokuConfiguration { VaultPath = _vaultPath }),
            new CoordinationFileSystem(),
            new CoordinationContractValidator(),
            timeProvider);

    private CoordinationClaimStore CreateClaimStore(TimeProvider timeProvider) =>
        new(
            new VaultPathPolicy(new KiokuConfiguration { VaultPath = _vaultPath }),
            new CoordinationFileSystem(),
            CreateEventStore(timeProvider),
            new CoordinationContractValidator(),
            timeProvider);

    private static CoordinationClaimAcquireRequest CreateAcquireRequest(
        string runId,
        string workItemId,
        string attemptId,
        string sessionId,
        string transitionId,
        TimeSpan? duration = null,
        IReadOnlyList<string>? authorityScope = null,
        string resourceKey = "note:Notes/Plan.md") => new()
        {
            RunId = runId,
            WorkItemId = workItemId,
            AttemptId = attemptId,
            SessionId = sessionId,
            ResourceKey = resourceKey,
            TransitionId = transitionId,
            LeaseDuration = duration ?? CoordinationClaimLeasePolicy.DefaultDuration,
            AuthorityScope = authorityScope ?? [],
            Agent = "agent-a",
            ClientName = "client-a",
        };

    private static CoordinationClaimMutationRequest CreateMutation(
        CoordinationClaim claim,
        string transitionId,
        TimeSpan? duration = null) => new()
        {
            RunId = claim.RunId,
            WorkItemId = claim.WorkItemId,
            AttemptId = claim.AttemptId,
            SessionId = claim.SessionId!,
            ClaimId = claim.ClaimId,
            ResourceKey = claim.ResourceKey,
            TransitionId = transitionId,
            FenceGeneration = claim.FenceGeneration,
            LeaseDuration = duration ?? CoordinationClaimLeasePolicy.DefaultDuration,
            Agent = claim.Agent,
            ClientName = claim.ClientName,
        };

    private static async Task<(CoordinationClaimResult? Result, CoordinationClaimException? Error)> TryAcquireAsync(
        CoordinationClaimStore store,
        CoordinationClaimAcquireRequest request)
    {
        try
        {
            return (await store.AcquireAsync(request), null);
        }
        catch (CoordinationClaimException exception)
        {
            return (null, exception);
        }
    }

    private static CoordinationEvent CreateEvent(string runId, string workItemId)
    {
        var coordinationEvent = new CoordinationEvent
        {
            EventId = $"created-{workItemId}",
            RunId = runId,
            WorkItemId = workItemId,
            Project = "example-project",
            ResourceScope = ["note:Notes/Plan.md"],
            AttemptId = null,
            SessionId = null,
            ClaimId = null,
            SequenceNumber = 1,
            EventType = CoordinationEventTypes.WorkItemCreated,
            TransitionId = $"created-{workItemId}",
            OccurredAt = StartTime,
            RecordedAt = StartTime,
            Actor = new CoordinationActor
            {
                Agent = "agent-a",
                ClientName = "client-a",
                SessionId = null,
            },
            Payload = new CoordinationTransitionPayload
            {
                NextState = CoordinationStates.Pending,
                StateVersion = 0,
                Reason = "Created for claim testing.",
            },
            PreviousHash = null,
            ContentHash = string.Empty,
        };

        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(coordinationEvent))!.AsObject();
        using var document = JsonDocument.Parse(node.ToJsonString());
        node[CoordinationContract.ContentHashPropertyName] = CanonicalJson.ComputeSha256Hex(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName);
        return JsonSerializer.Deserialize(
            node.ToJsonString(),
            CoordinationJsonContext.Default.CoordinationEvent)!;
    }

    private static string HashResourceKey(string resourceKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey)));
}
