using System.Text.Json;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CoordinationServiceTests : IDisposable
{
    private readonly string _vaultPath = Path.Combine(
        Path.GetTempPath(),
        $"kioku-coordination-service-{Guid.NewGuid():N}");
    private readonly CoordinationService _service;

    public CoordinationServiceTests()
    {
        Directory.CreateDirectory(_vaultPath);
        var configuration = new KiokuConfiguration { VaultPath = _vaultPath };
        var paths = new VaultPathPolicy(configuration);
        var fileSystem = new CoordinationFileSystem();
        var validator = new CoordinationContractValidator();
        var eventStore = new CoordinationEventStore(paths, fileSystem, validator, TimeProvider.System);
        var claimStore = new CoordinationClaimStore(
            paths,
            fileSystem,
            eventStore,
            validator,
            TimeProvider.System);
        var conflictStore = new CoordinationConflictStore(paths, fileSystem, validator, TimeProvider.System);
        _service = new CoordinationService(eventStore, claimStore, conflictStore, TimeProvider.System);
    }

    [Fact]
    public async Task WorkItemLifecycle_ExposesProjectionHistoryRunsAndHandoff()
    {
        var created = await CreateWorkItemAsync();
        var duplicate = await CreateWorkItemAsync();

        Assert.Equal("run-01", created.Projection.RunId);
        Assert.Equal("work-01", created.Projection.WorkItemId);
        Assert.Equal(CoordinationStates.Pending, created.Projection.State);
        Assert.Equal(created.Projection.ContentHash, duplicate.Projection.ContentHash);

        var history = await _service.ListHistoryAsync("run-01", "work-01");
        var runs = await _service.ListRunsAsync();
        var handoff = await _service.GetHandoffPacketAsync("run-01", "work-01");

        Assert.Single(history.Items);
        Assert.Single(runs.Items);
        Assert.Equal(1, runs.Items[0].States[CoordinationStates.Pending]);
        Assert.Equal(CoordinationStates.Pending, handoff.State);
        Assert.Equal(
            CoordinationContractSerializer.ComputeContentHash(handoff),
            handoff.ContentHash);
    }

    [Fact]
    public async Task ClaimAndTransition_RequireAndPreserveFenceMetadata()
    {
        await CreateWorkItemAsync();
        var claim = await _service.AcquireClaimAsync(new CoordinationClaimAcquireRequest
        {
            RunId = "run-01",
            WorkItemId = "work-01",
            AttemptId = "attempt-01",
            SessionId = "session-01",
            ResourceKey = "logical:queue/main",
            TransitionId = "claim-01",
            LeaseDuration = TimeSpan.FromMinutes(1),
        });

        var transitioned = await _service.TransitionAsync(new CoordinationTransitionRequest
        {
            RunId = "run-01",
            WorkItemId = "work-01",
            AttemptId = "attempt-01",
            SessionId = "session-01",
            NextState = CoordinationStates.Running,
            TransitionId = "transition-01",
            ExpectedStateVersion = 1,
            ClaimId = claim.Claim.ClaimId,
            FenceGeneration = claim.Claim.FenceGeneration,
            Reason = "The attempt started.",
        });

        var current = await _service.GetWorkItemAsync("run-01", "work-01");
        var history = await _service.ListHistoryAsync("run-01", "work-01");

        Assert.Equal(CoordinationStates.Running, transitioned.WorkItem.Projection.State);
        Assert.Equal(CoordinationStates.Running, current.Projection.State);
        Assert.Single(current.ActiveClaims);
        Assert.Equal(claim.Claim.FenceGeneration, current.ActiveClaims[0].FenceGeneration);
        Assert.Equal(3, history.Total);
    }

    [Fact]
    public async Task ClaimConflict_IsDurableAndCanBeResolved()
    {
        await CreateWorkItemAsync();
        await _service.AcquireClaimAsync(new CoordinationClaimAcquireRequest
        {
            RunId = "run-01",
            WorkItemId = "work-01",
            AttemptId = "attempt-01",
            SessionId = "session-01",
            ResourceKey = "logical:queue/main",
            TransitionId = "claim-01",
            LeaseDuration = TimeSpan.FromMinutes(1),
        });

        var exception = await Assert.ThrowsAsync<CoordinationOperationException>(() =>
            _service.AcquireClaimAsync(new CoordinationClaimAcquireRequest
            {
                RunId = "run-01",
                WorkItemId = "work-01",
                AttemptId = "attempt-02",
                SessionId = "session-02",
                ResourceKey = "logical:queue/main",
                TransitionId = "claim-02",
                LeaseDuration = TimeSpan.FromMinutes(1),
            }));

        Assert.Equal(CoordinationOperationErrorCodes.ClaimConflict, exception.Code);
        var conflicts = await _service.ListConflictsAsync();
        Assert.Single(conflicts.Items);

        var resolved = await _service.ResolveConflictAsync(
            conflicts.Items[0].ConflictId,
            CoordinationConflictStatuses.Resolved,
            "The current owner will finish before takeover.",
            new CoordinationActor { Agent = "operator" });
        var open = await _service.ListConflictsAsync(status: CoordinationConflictStatuses.Open);

        Assert.Equal(CoordinationConflictStatuses.Resolved, resolved.Status);
        Assert.Empty(open.Items);
    }

    [Fact]
    public async Task HandoffPacket_IsAcceptedByItsVersionedSchema()
    {
        await CreateWorkItemAsync();

        var handoff = await _service.GetHandoffPacketAsync("run-01", "work-01");
        var validation = await new CoordinationContractValidator().ValidateAsync(
            CoordinationContractKind.HandoffPacket,
            CoordinationContractSerializer.Serialize(handoff));

        Assert.True(validation.IsValid, string.Join(", ", validation.Errors.Select(error => error.Code)));
        using var document = JsonDocument.Parse(CoordinationContractSerializer.Serialize(handoff));
        Assert.True(CanonicalJson.ContentHashMatches(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName));
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

    private Task<CoordinationWorkItemSnapshot> CreateWorkItemAsync() =>
        _service.CreateWorkItemAsync(new CoordinationCreateWorkItemRequest
        {
            RunId = "run-01",
            WorkItemId = "work-01",
            Project = "coordination-tests",
            AttemptId = "attempt-01",
            SessionId = "session-01",
            Agent = "agent-a",
            ResourceScope = ["logical:queue/main"],
            Summary = "Ready to begin the coordination test.",
            TransitionId = "create-01",
        });
}
