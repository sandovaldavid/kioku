using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for the durable coordination profile. MCP adapters call this service;
/// state-machine, event-ordering, claim, and conflict invariants remain below this boundary.
/// </summary>
public interface ICoordinationService
{
    Task<CoordinationWorkItemSnapshot> CreateWorkItemAsync(
        CoordinationCreateWorkItemRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationWorkItemSnapshot> GetWorkItemAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default);

    Task<CoordinationPage<CoordinationWorkItemSnapshot>> ListWorkItemsAsync(
        string? runId = null,
        string? project = null,
        string? state = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<CoordinationPage<CoordinationRunSummary>> ListRunsAsync(
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<CoordinationTransitionResult> TransitionAsync(
        CoordinationTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> AcquireClaimAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> RenewClaimAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> ReleaseClaimAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> ExpireClaimAsync(
        CoordinationClaimExpiryRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationPage<CoordinationClaim>> ListClaimsAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<CoordinationPage<CoordinationEvent>> ListHistoryAsync(
        string runId,
        string workItemId,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<HandoffPacket> GetHandoffPacketAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default);

    Task<CoordinationPage<CoordinationConflict>> ListConflictsAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<CoordinationConflict> ResolveConflictAsync(
        string conflictId,
        string status,
        string resolution,
        CoordinationActor actor,
        CancellationToken cancellationToken = default);
}

internal sealed class CoordinationService(
    ICoordinationEventStore events,
    ICoordinationClaimStore claims,
    ICoordinationConflictStore conflicts,
    TimeProvider timeProvider) : ICoordinationService
{
    public async Task<CoordinationWorkItemSnapshot> CreateWorkItemAsync(
        CoordinationCreateWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runId = NormalizeOrCreateIdentifier(request.RunId, "run_id");
        var workItemId = NormalizeOrCreateIdentifier(request.WorkItemId, "work_item_id");
        var project = RequireText(request.Project, "project", 256);
        var attemptId = NormalizeOrCreateIdentifier(request.AttemptId, "attempt_id");
        var transitionId = NormalizeOrCreateIdentifier(
            request.TransitionId,
            "transition_id",
            fallback: $"create:{workItemId}");
        ValidateOptionalIdentifier(request.SessionId, "session_id");
        ValidateOptionalIdentifier(request.ParentSessionId, "parent_session_id");
        ValidateActor(request.Agent, request.ClientName);
        var resourceScope = NormalizeResourceScope(request.ResourceScope);
        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? "The work item was initialized."
            : RequireText(request.Summary, "summary", 2000);

        try
        {
            var existing = await events.ListProjectionsAsync(
                    workItemId: workItemId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (existing.Count > 0)
            {
                var current = existing.SingleOrDefault(projection =>
                    string.Equals(projection.RunId, runId, StringComparison.Ordinal));
                if (current is null)
                {
                    throw Operation(
                        CoordinationOperationErrorCodes.Conflict,
                        "The work_item_id is already used by another run.",
                        "Choose a new work_item_id and retry.");
                }

                var history = await events.ReadHistoryAsync(runId, workItemId, cancellationToken)
                    .ConfigureAwait(false);
                if (history.Count > 0 && string.Equals(history[0].TransitionId, transitionId, StringComparison.Ordinal))
                {
                    return await BuildSnapshotAsync(current, cancellationToken).ConfigureAwait(false);
                }

                throw Operation(
                    CoordinationOperationErrorCodes.Conflict,
                    "The work item already exists.",
                    "Read the existing projection and use a new work_item_id for a different item.");
            }

            var now = timeProvider.GetUtcNow().ToUniversalTime();
            var created = new CoordinationEvent
            {
                EventId = Guid.CreateVersion7().ToString("D"),
                RunId = runId,
                WorkItemId = workItemId,
                Project = project,
                ResourceScope = resourceScope,
                AttemptId = attemptId,
                SessionId = request.SessionId,
                ParentSessionId = request.ParentSessionId,
                ClaimId = null,
                SequenceNumber = 1,
                EventType = CoordinationEventTypes.WorkItemCreated,
                TransitionId = transitionId,
                OccurredAt = now,
                RecordedAt = now,
                Actor = CreateActor(request.Agent, request.ClientName, request.SessionId),
                Payload = new CoordinationTransitionPayload
                {
                    NextState = CoordinationStates.Pending,
                    StateVersion = 0,
                    Reason = summary,
                },
                PreviousHash = null,
                ContentHash = string.Empty,
            };
            created = WithContentHash(created);
            var result = await events.AppendAsync(created, cancellationToken).ConfigureAwait(false);
            return await BuildSnapshotAsync(result.Projection, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationOperationException)
        {
            throw;
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
    }

    public async Task<CoordinationWorkItemSnapshot> GetWorkItemAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(runId, "run_id");
        ValidateIdentifier(workItemId, "work_item_id");
        try
        {
            var replay = await events.ReplayAsync(runId, workItemId, cancellationToken).ConfigureAwait(false);
            return replay.Projection is null
                ? throw Operation(
                    CoordinationOperationErrorCodes.NotFound,
                    "The coordination work item was not found.",
                    "Check run_id and work_item_id and retry.")
                : await BuildSnapshotAsync(replay.Projection, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationOperationException)
        {
            throw;
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
    }

    public async Task<CoordinationPage<CoordinationWorkItemSnapshot>> ListWorkItemsAsync(
        string? runId = null,
        string? project = null,
        string? state = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateOptionalIdentifier(runId, "run_id");
        ValidateOptionalText(project, "project", 256);
        ValidateState(state);
        (offset, limit) = NormalizePage(offset, limit);
        try
        {
            var projections = await events.ListProjectionsAsync(
                    runId,
                    project: string.IsNullOrWhiteSpace(project) ? null : project,
                    state: string.IsNullOrWhiteSpace(state) ? null : state,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var page = projections.Skip(offset).Take(limit).ToArray();
            var snapshots = new List<CoordinationWorkItemSnapshot>(page.Length);
            foreach (var projection in page)
            {
                snapshots.Add(await BuildSnapshotAsync(projection, cancellationToken).ConfigureAwait(false));
            }

            return new(snapshots, projections.Count, offset, limit, offset + page.Length < projections.Count);
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
        catch (CoordinationConflictStoreException exception)
        {
            throw TranslateConflictException(exception);
        }
    }

    public async Task<CoordinationPage<CoordinationRunSummary>> ListRunsAsync(
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        (offset, limit) = NormalizePage(offset, limit);
        try
        {
            var projections = await events.ListProjectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var runs = projections
                .GroupBy(projection => projection.RunId, StringComparer.Ordinal)
                .Select(group => new CoordinationRunSummary(
                    group.Key,
                    group.Count(),
                    group.GroupBy(projection => projection.State, StringComparer.Ordinal)
                        .ToDictionary(states => states.Key, states => states.Count(), StringComparer.Ordinal),
                    group.Max(projection => projection.UpdatedAt)))
                .OrderByDescending(run => run.UpdatedAt)
                .ThenBy(run => run.RunId, StringComparer.Ordinal)
                .ToArray();
            var page = runs.Skip(offset).Take(limit).ToArray();
            return new(page, runs.Length, offset, limit, offset + page.Length < runs.Length);
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
    }

    public async Task<CoordinationTransitionResult> TransitionAsync(
        CoordinationTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentifier(request.RunId, "run_id");
        ValidateIdentifier(request.WorkItemId, "work_item_id");
        ValidateState(request.NextState);
        ValidateOptionalIdentifier(request.AttemptId, "attempt_id");
        ValidateOptionalIdentifier(request.SessionId, "session_id");
        ValidateOptionalIdentifier(request.TransitionId, "transition_id");
        ValidateActor(request.Agent, request.ClientName);
        ValidateOptionalText(request.Reason, "reason", 2000);
        ValidateOptionalText(request.Outcome, "outcome", 2000);
        ValidateOptionalText(request.ErrorCode, "error_code", 128);
        ValidateOptionalText(request.ResultReference, "result_reference", 512);
        ValidateOptionalText(request.ProgressReference, "progress_reference", 512);
        ValidateClaimPrecondition(request.ClaimId, request.FenceGeneration);

        try
        {
            var replay = await events.ReplayAsync(request.RunId, request.WorkItemId, cancellationToken).ConfigureAwait(false);
            var projection = replay.Projection ?? throw Operation(
                CoordinationOperationErrorCodes.NotFound,
                "The coordination work item was not found.",
                "Create the work item or check its identifiers and retry.");
            var history = replay.Events;
            var transitionId = string.IsNullOrWhiteSpace(request.TransitionId)
                ? Guid.CreateVersion7().ToString("D")
                : request.TransitionId!;
            var existing = history.FirstOrDefault(item =>
                string.Equals(item.TransitionId, transitionId, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (!MatchesTransitionRequest(existing, request))
                {
                    throw Operation(
                        CoordinationOperationErrorCodes.DuplicateTransition,
                        "The transition_id was already used for different transition data.",
                        "Use the original transition parameters or choose a new transition_id.");
                }

                return new(
                    "duplicate",
                    existing,
                    await BuildSnapshotAsync(projection, cancellationToken).ConfigureAwait(false));
            }

            if (request.ExpectedStateVersion is { } expectedVersion &&
                expectedVersion != projection.StateVersion)
            {
                throw Operation(
                    CoordinationOperationErrorCodes.StateVersionConflict,
                    "The work-item state changed after the prior read.",
                    "Reload the work item and retry with its current state_version.");
            }

            var eventType = ResolveEventType(request.EventType, request.NextState, projection.State);
            var attemptId = request.AttemptId ?? projection.AttemptId;
            var sessionId = request.SessionId ?? projection.SessionId;
            var claim = await EnsureTransitionClaimAsync(
                    request,
                    projection,
                    attemptId,
                    sessionId,
                    cancellationToken)
                .ConfigureAwait(false);
            var now = timeProvider.GetUtcNow().ToUniversalTime();
            var transition = new CoordinationEvent
            {
                EventId = Guid.CreateVersion7().ToString("D"),
                RunId = request.RunId,
                WorkItemId = request.WorkItemId,
                Project = projection.Project,
                ResourceScope = projection.ResourceScope,
                AttemptId = attemptId,
                SessionId = sessionId,
                ClaimId = claim?.ClaimId ?? request.ClaimId,
                SequenceNumber = history[^1].SequenceNumber + 1,
                EventType = eventType,
                TransitionId = transitionId,
                OccurredAt = now,
                RecordedAt = now,
                Actor = CreateActor(request.Agent, request.ClientName, sessionId),
                Payload = new CoordinationTransitionPayload
                {
                    PreviousState = projection.State,
                    NextState = request.NextState,
                    ExpectedStateVersion = projection.StateVersion,
                    StateVersion = projection.StateVersion + 1,
                    Reason = request.Reason,
                    Outcome = request.Outcome,
                    ErrorCode = request.ErrorCode,
                    ResultReference = request.ResultReference,
                    ProgressReference = request.ProgressReference,
                    LeaseExpiresAt = claim?.LeaseExpiresAt,
                    ResourceKey = claim?.ResourceKey,
                    FenceGeneration = claim?.FenceGeneration ?? request.FenceGeneration,
                },
                PreviousHash = history[^1].ContentHash,
                ContentHash = string.Empty,
            };
            var append = await events.AppendAsync(WithContentHash(transition), cancellationToken).ConfigureAwait(false);
            return new(
                append.Disposition == CoordinationAppendDisposition.Duplicate ? "duplicate" : "appended",
                append.Event,
                await BuildSnapshotAsync(append.Projection, cancellationToken).ConfigureAwait(false));
        }
        catch (CoordinationOperationException)
        {
            throw;
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
    }

    public async Task<CoordinationClaimResult> AcquireClaimAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await claims.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationClaimException exception)
        {
            if (exception.Code == CoordinationClaimErrorCodes.ClaimConflict)
            {
                await RecordClaimConflictAsync(request, exception.Code, cancellationToken).ConfigureAwait(false);
            }

            throw TranslateClaimException(exception);
        }
    }

    public async Task<CoordinationClaimResult> RenewClaimAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await claims.RenewAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
    }

    public async Task<CoordinationClaimResult> ReleaseClaimAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await claims.ReleaseAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
    }

    public async Task<CoordinationClaimResult> ExpireClaimAsync(
        CoordinationClaimExpiryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await claims.ExpireAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
    }

    public async Task<CoordinationPage<CoordinationClaim>> ListClaimsAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateOptionalIdentifier(runId, "run_id");
        ValidateOptionalIdentifier(workItemId, "work_item_id");
        ValidateOptionalText(status, "status", 32);
        (offset, limit) = NormalizePage(offset, limit);
        try
        {
            var all = await claims.ListAsync(runId, workItemId, status, cancellationToken).ConfigureAwait(false);
            var page = all.Skip(offset).Take(limit).ToArray();
            return new(page, all.Count, offset, limit, offset + page.Length < all.Count);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
    }

    public async Task<CoordinationPage<CoordinationEvent>> ListHistoryAsync(
        string runId,
        string workItemId,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(runId, "run_id");
        ValidateIdentifier(workItemId, "work_item_id");
        (offset, limit) = NormalizePage(offset, limit, maxLimit: 200);
        try
        {
            var all = await events.ReadHistoryAsync(runId, workItemId, cancellationToken).ConfigureAwait(false);
            if (all.Count == 0)
            {
                throw Operation(
                    CoordinationOperationErrorCodes.NotFound,
                    "The coordination work item was not found.",
                    "Check run_id and work_item_id and retry.");
            }

            var page = all.Skip(offset).Take(limit).ToArray();
            return new(page, all.Count, offset, limit, offset + page.Length < all.Count);
        }
        catch (CoordinationOperationException)
        {
            throw;
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
    }

    public async Task<HandoffPacket> GetHandoffPacketAsync(
        string runId,
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await GetWorkItemAsync(runId, workItemId, cancellationToken).ConfigureAwait(false);
            var history = await events.ReadHistoryAsync(runId, workItemId, cancellationToken).ConfigureAwait(false);
            var last = history[^1];
            var packet = new HandoffPacket
            {
                RunId = runId,
                WorkItemId = workItemId,
                AttemptId = snapshot.Projection.AttemptId,
                SessionId = snapshot.Projection.SessionId,
                ParentSessionId = snapshot.Projection.ParentSessionId,
                Agent = last.Actor.Agent,
                ClientName = last.Actor.ClientName,
                Project = snapshot.Projection.Project,
                ResourceScope = snapshot.Projection.ResourceScope,
                AuthorityScope = [CoordinationAuthorityScopes.Read],
                State = snapshot.Projection.State,
                Checkpoint = new HandoffCheckpoint
                {
                    Summary = last.Payload.Reason ?? $"Current state: {snapshot.Projection.State}.",
                    Reference = last.EventId,
                    Revision = snapshot.Projection.Revision,
                    ContentHash = snapshot.Projection.ContentHash,
                },
                NextActions = [],
                Artifacts = [],
                Blockers = snapshot.Projection.Blockers,
                Conflicts = snapshot.Conflicts
                    .Select(conflict => new HandoffConflictReference
                    {
                        ConflictId = conflict.ConflictId,
                        ResourceKey = conflict.ResourceKey,
                        Status = conflict.Status,
                    })
                    .ToArray(),
                CreatedAt = snapshot.Projection.CreatedAt,
                UpdatedAt = snapshot.Projection.UpdatedAt,
                Sequence = snapshot.Projection.LastEventSequence,
                StateVersion = snapshot.Projection.StateVersion,
                Revision = snapshot.Projection.Revision,
                ContentHash = string.Empty,
            };
            return WithContentHash(packet);
        }
        catch (CoordinationOperationException)
        {
            throw;
        }
        catch (CoordinationStoreException exception)
        {
            throw TranslateStoreException(exception);
        }
    }

    public Task<CoordinationPage<CoordinationConflict>> ListConflictsAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        ListConflictsCoreAsync(runId, workItemId, status, offset, limit, cancellationToken);

    public async Task<CoordinationConflict> ResolveConflictAsync(
        string conflictId,
        string status,
        string resolution,
        CoordinationActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(conflictId, "conflict_id");
        ValidateOptionalText(status, "status", 32);
        ValidateOptionalText(resolution, "resolution", 2000);
        ValidateActor(actor.Agent, actor.ClientName);
        ValidateOptionalIdentifier(actor.SessionId, "session_id");
        try
        {
            return await conflicts.ResolveAsync(conflictId, status, resolution, actor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CoordinationConflictStoreException exception)
        {
            throw TranslateConflictException(exception);
        }
    }

    private async Task<CoordinationPage<CoordinationConflict>> ListConflictsCoreAsync(
        string? runId,
        string? workItemId,
        string? status,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateOptionalIdentifier(runId, "run_id");
        ValidateOptionalIdentifier(workItemId, "work_item_id");
        ValidateOptionalText(status, "status", 32);
        (offset, limit) = NormalizePage(offset, limit);
        try
        {
            var all = await conflicts.ListAsync(runId, workItemId, status, cancellationToken).ConfigureAwait(false);
            var page = all.Skip(offset).Take(limit).ToArray();
            return new(page, all.Count, offset, limit, offset + page.Length < all.Count);
        }
        catch (CoordinationConflictStoreException exception)
        {
            throw TranslateConflictException(exception);
        }
    }

    private async Task<CoordinationWorkItemSnapshot> BuildSnapshotAsync(
        WorkItemProjection projection,
        CancellationToken cancellationToken)
    {
        try
        {
            var activeClaims = await claims.ListAsync(
                    projection.RunId,
                    projection.WorkItemId,
                    CoordinationClaimStatuses.Active,
                    cancellationToken)
                .ConfigureAwait(false);
            var openConflicts = await conflicts.ListAsync(
                    projection.RunId,
                    projection.WorkItemId,
                    CoordinationConflictStatuses.Open,
                    cancellationToken)
                .ConfigureAwait(false);
            return new(projection, activeClaims, openConflicts);
        }
        catch (CoordinationClaimException exception)
        {
            throw TranslateClaimException(exception);
        }
        catch (CoordinationConflictStoreException exception)
        {
            throw TranslateConflictException(exception);
        }
    }

    private async Task<CoordinationClaim?> EnsureTransitionClaimAsync(
        CoordinationTransitionRequest request,
        WorkItemProjection projection,
        string? attemptId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var claimRequired = !(projection.State == CoordinationStates.Pending &&
            request.NextState == CoordinationStates.Canceled);
        if (!claimRequired && string.IsNullOrWhiteSpace(request.ClaimId) && !request.FenceGeneration.HasValue)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.ClaimId) || !request.FenceGeneration.HasValue)
        {
            throw Operation(
                CoordinationOperationErrorCodes.ClaimRequired,
                "The transition requires the current active claim and fence generation.",
                "Acquire or read the current claim and retry with claim_id and fence_generation.");
        }

        var currentClaims = await claims.ListAsync(
                request.RunId,
                request.WorkItemId,
                CoordinationClaimStatuses.Active,
                cancellationToken)
            .ConfigureAwait(false);
        var current = currentClaims.FirstOrDefault(claim =>
            string.Equals(claim.ClaimId, request.ClaimId, StringComparison.Ordinal) &&
            claim.FenceGeneration == request.FenceGeneration.Value &&
            string.Equals(claim.AttemptId, attemptId, StringComparison.Ordinal) &&
            (sessionId is null || string.Equals(claim.SessionId, sessionId, StringComparison.Ordinal)));
        if (current is null)
        {
            throw Operation(
                CoordinationOperationErrorCodes.ClaimFenced,
                "The supplied claim is expired or has been superseded.",
                "Read the current work item and acquire or renew its active claim before retrying.");
        }

        return current;
    }

    private async Task RecordClaimConflictAsync(
        CoordinationClaimAcquireRequest request,
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            var conflictId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{request.RunId}\n{request.WorkItemId}\n{request.ResourceKey}\n{code}")));
            await conflicts.RecordAsync(new CoordinationConflict
            {
                ConflictId = conflictId,
                RunId = request.RunId,
                WorkItemId = request.WorkItemId,
                AttemptId = request.AttemptId,
                ResourceKey = request.ResourceKey,
                Kind = CoordinationConflictKinds.ClaimConflict,
                Status = CoordinationConflictStatuses.Open,
                DetectedAt = timeProvider.GetUtcNow().ToUniversalTime(),
                ExpectedRevision = null,
                ActualRevision = null,
                ExpectedHash = null,
                ActualHash = null,
                Description = "Another active claim currently owns the requested resource.",
                Resolution = null,
                ResolvedAt = null,
                ResolvedBy = null,
                ContentHash = string.Empty,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationConflictStoreException)
        {
            // The original claim conflict is the actionable result. Conflict persistence must not
            // turn a safe refusal into an unrelated internal error.
        }
    }

    private static bool MatchesTransitionRequest(
        CoordinationEvent existing,
        CoordinationTransitionRequest request) =>
        string.Equals(existing.Payload.NextState, request.NextState, StringComparison.Ordinal) &&
        (request.EventType is null || string.Equals(existing.EventType, request.EventType, StringComparison.Ordinal)) &&
        (request.Reason is null || string.Equals(existing.Payload.Reason, request.Reason, StringComparison.Ordinal)) &&
        (request.Outcome is null || string.Equals(existing.Payload.Outcome, request.Outcome, StringComparison.Ordinal)) &&
        (request.ErrorCode is null || string.Equals(existing.Payload.ErrorCode, request.ErrorCode, StringComparison.Ordinal));

    private static string ResolveEventType(string? requested, string nextState, string currentState)
    {
        var expected = nextState switch
        {
            CoordinationStates.Pending when currentState == CoordinationStates.Stale => CoordinationEventTypes.WorkItemReopened,
            CoordinationStates.Pending => CoordinationEventTypes.WorkItemClaimReleased,
            CoordinationStates.Claimed when currentState == CoordinationStates.Claimed => CoordinationEventTypes.WorkItemClaimRenewed,
            CoordinationStates.Claimed => CoordinationEventTypes.WorkItemClaimed,
            CoordinationStates.Running => CoordinationEventTypes.WorkItemStarted,
            CoordinationStates.Blocked => CoordinationEventTypes.WorkItemBlocked,
            CoordinationStates.Partial => CoordinationEventTypes.WorkItemPartial,
            CoordinationStates.Failed => CoordinationEventTypes.WorkItemFailed,
            CoordinationStates.Stale => CoordinationEventTypes.WorkItemStale,
            CoordinationStates.Completed => CoordinationEventTypes.WorkItemCompleted,
            CoordinationStates.Canceled => CoordinationEventTypes.WorkItemCanceled,
            _ => throw Operation(
                CoordinationOperationErrorCodes.InvalidState,
                "The requested coordination state is unsupported.",
                "Use one of pending, claimed, running, blocked, partial, failed, stale, completed, or canceled."),
        };

        if (requested is not null && !string.Equals(requested, expected, StringComparison.Ordinal))
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidState,
                "The event_type does not match the requested state transition.",
                $"Use event_type '{expected}' for this transition.");
        }

        return expected;
    }

    private static CoordinationEvent WithContentHash(CoordinationEvent coordinationEvent)
    {
        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(coordinationEvent))!.AsObject();
        using var document = JsonDocument.Parse(node.ToJsonString());
        node[CoordinationContract.ContentHashPropertyName] = CanonicalJson.ComputeSha256Hex(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName);
        return JsonSerializer.Deserialize(
            node.ToJsonString(),
            CoordinationJsonContext.Default.CoordinationEvent)
            ?? throw Operation(
                CoordinationOperationErrorCodes.Internal,
                "The coordination event could not be serialized.",
                "Retry the operation; if it continues, inspect the coordination history.");
    }

    private static HandoffPacket WithContentHash(HandoffPacket packet)
    {
        var hash = CoordinationContractSerializer.ComputeContentHash(packet);
        return new HandoffPacket
        {
            SchemaVersion = packet.SchemaVersion,
            RunId = packet.RunId,
            WorkItemId = packet.WorkItemId,
            AttemptId = packet.AttemptId,
            SessionId = packet.SessionId,
            ParentSessionId = packet.ParentSessionId,
            Agent = packet.Agent,
            ClientName = packet.ClientName,
            Project = packet.Project,
            ResourceScope = packet.ResourceScope,
            AuthorityScope = packet.AuthorityScope,
            State = packet.State,
            Checkpoint = packet.Checkpoint,
            NextActions = packet.NextActions,
            Artifacts = packet.Artifacts,
            Blockers = packet.Blockers,
            Conflicts = packet.Conflicts,
            CreatedAt = packet.CreatedAt,
            UpdatedAt = packet.UpdatedAt,
            Sequence = packet.Sequence,
            StateVersion = packet.StateVersion,
            Revision = packet.Revision,
            ContentHash = hash,
        };
    }

    private static CoordinationActor CreateActor(string? agent, string? clientName, string? sessionId) => new()
    {
        Agent = agent,
        ClientName = clientName,
        SessionId = sessionId,
    };

    private static IReadOnlyList<string> NormalizeResourceScope(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > 256)
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                "resource_scope contains too many resources.",
                "Provide at most 256 resource keys.");
        }

        var normalized = values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var value in normalized)
        {
            if (value.Length > 512 ||
                (!value.StartsWith("note:", StringComparison.Ordinal) &&
                 !value.StartsWith("logical:", StringComparison.Ordinal)))
            {
                throw Operation(
                    CoordinationOperationErrorCodes.InvalidArgument,
                    "resource_scope contains an unsupported resource key.",
                    "Use note: or logical: resource keys within the configured vault boundary.");
            }
        }

        return normalized;
    }

    private static string NormalizeOrCreateIdentifier(
        string? value,
        string field,
        string? fallback = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback ?? Guid.CreateVersion7().ToString("D")
            : value.Trim();
        ValidateIdentifier(normalized, field);
        return normalized;
    }

    private static void ValidateIdentifier(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                $"{field} is missing or unsafe.",
                $"Provide a {field} containing only letters, numbers, '-', '_', '.', or ':'.");
        }
    }

    private static void ValidateOptionalIdentifier(string? value, string field)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ValidateIdentifier(value, field);
        }
    }

    private static string RequireText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                $"{field} is missing or exceeds its maximum length.",
                $"Provide {field} with at most {maxLength} characters.");
        }

        return value.Trim();
    }

    private static void ValidateOptionalText(string? value, string field, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                $"{field} exceeds its maximum length.",
                $"Provide {field} with at most {maxLength} characters.");
        }
    }

    private static void ValidateActor(string? agent, string? clientName)
    {
        ValidateOptionalText(agent, "agent", 128);
        ValidateOptionalText(clientName, "client_name", 128);
    }

    private static void ValidateState(string? state)
    {
        if (!string.IsNullOrWhiteSpace(state) && !CoordinationStates.All.Contains(state))
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                "state is not a supported coordination state.",
                "Use one of pending, claimed, running, blocked, partial, failed, stale, completed, or canceled.");
        }
    }

    private static void ValidateClaimPrecondition(string? claimId, long? fenceGeneration)
    {
        if ((claimId is null) == !fenceGeneration.HasValue)
        {
            return;
        }

        throw Operation(
            CoordinationOperationErrorCodes.InvalidArgument,
            "claim_id and fence_generation must be supplied together.",
            "Read the active claim and provide both values, or omit both for an unclaimed cancellation.");
    }

    private static (int Offset, int Limit) NormalizePage(int offset, int limit, int maxLimit = 100)
    {
        if (offset < 0 || limit < 1)
        {
            throw Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                "offset must be non-negative and limit must be positive.",
                "Use offset >= 0 and a positive limit.");
        }

        return (offset, Math.Min(limit, maxLimit));
    }

    private static CoordinationOperationException TranslateStoreException(CoordinationStoreException exception) =>
        exception.Code switch
        {
            CoordinationStoreErrorCodes.AccessDenied => Operation(
                CoordinationOperationErrorCodes.AccessDenied,
                "The coordination storage boundary denied the operation.",
                "Verify the configured vault is writable and retry."),
            CoordinationStoreErrorCodes.InvalidSequence or
            CoordinationStoreErrorCodes.CorruptHistory or
            CoordinationStoreErrorCodes.ProjectionCorrupt => Operation(
                CoordinationOperationErrorCodes.CorruptHistory,
                "Coordination history or its projection is corrupt.",
                "Stop competing writers, preserve the coordination files, and inspect the history before retrying."),
            CoordinationStoreErrorCodes.DuplicateTransition => Operation(
                CoordinationOperationErrorCodes.DuplicateTransition,
                "The transition_id conflicts with existing transition history.",
                "Reload the work item and use a new transition_id for different data."),
            CoordinationStoreErrorCodes.InvalidEvent or
            CoordinationStoreErrorCodes.UnsafeIdentifier => Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                "The coordination event was rejected as invalid.",
                "Check the identifiers and transition fields and retry."),
            _ => Operation(
                CoordinationOperationErrorCodes.Internal,
                "The coordination store could not complete the operation.",
                "Retry after checking the coordination storage boundary."),
        };

    private static CoordinationOperationException TranslateClaimException(CoordinationClaimException exception) =>
        exception.Code switch
        {
            CoordinationClaimErrorCodes.ClaimConflict => Operation(
                CoordinationOperationErrorCodes.ClaimConflict,
                "Another active claim owns the requested resource.",
                "Wait for the lease to expire or use the current owner information before retrying."),
            CoordinationClaimErrorCodes.ClaimExpired => Operation(
                CoordinationOperationErrorCodes.ClaimExpired,
                "The claim lease has expired.",
                "Acquire a new claim before retrying the operation."),
            CoordinationClaimErrorCodes.ClaimFenced or
            CoordinationClaimErrorCodes.ClaimReleased => Operation(
                CoordinationOperationErrorCodes.ClaimFenced,
                "The claim has been superseded or released.",
                "Read the current claim and retry with its fence generation."),
            CoordinationClaimErrorCodes.InvalidState => Operation(
                CoordinationOperationErrorCodes.InvalidState,
                "The work item is not in a state that permits this claim operation.",
                "Read the work-item projection and choose an allowed transition."),
            CoordinationClaimErrorCodes.WorkItemNotFound or
            CoordinationClaimErrorCodes.ClaimNotFound => Operation(
                CoordinationOperationErrorCodes.NotFound,
                "The coordination work item or claim was not found.",
                "Check the identifiers and retry."),
            CoordinationClaimErrorCodes.AccessDenied => Operation(
                CoordinationOperationErrorCodes.AccessDenied,
                "The coordination storage boundary denied the operation.",
                "Verify the configured vault is writable and retry."),
            _ => Operation(
                CoordinationOperationErrorCodes.InvalidArgument,
                "The coordination claim request was rejected.",
                "Check the identifiers, resource key, lease, and fence fields and retry."),
        };

    private static CoordinationOperationException TranslateConflictException(
        CoordinationConflictStoreException exception) =>
        exception.Code switch
        {
            CoordinationConflictStoreErrorCodes.ConflictNotFound => Operation(
                CoordinationOperationErrorCodes.ConflictNotFound,
                "The coordination conflict was not found.",
                "List current conflicts and retry with an existing conflict_id."),
            CoordinationConflictStoreErrorCodes.ConflictAlreadyResolved => Operation(
                CoordinationOperationErrorCodes.ConflictAlreadyResolved,
                "The coordination conflict has already been resolved.",
                "Reload the conflict before attempting another resolution."),
            CoordinationConflictStoreErrorCodes.AccessDenied => Operation(
                CoordinationOperationErrorCodes.AccessDenied,
                "The coordination storage boundary denied the operation.",
                "Verify the configured vault is writable and retry."),
            _ => Operation(
                CoordinationOperationErrorCodes.CorruptHistory,
                "The durable conflict record is invalid.",
                "Preserve the coordination files and inspect the conflict record before retrying."),
        };

    private static CoordinationOperationException Operation(string code, string message, string recovery) =>
        new(code, message, recovery);
}
