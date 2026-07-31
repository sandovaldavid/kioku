using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Persists one rebuildable lease projection per canonical resource and records claim
/// lifecycle transitions through the append-only coordination event store.
/// </summary>
internal sealed class CoordinationClaimStore(
    VaultPathPolicy paths,
    ICoordinationFileSystem fileSystem,
    ICoordinationEventStore eventStore,
    CoordinationContractValidator validator,
    TimeProvider timeProvider) : ICoordinationClaimStore
{
    private const string CoordinationRoot = ".kioku/coordination";
    private const string LeaseRoot = "leases";
    private const string RuntimeLockRoot = "runtime/locks/resources";

    public Task<CoordinationClaimResult> AcquireAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken = default) =>
        AcquireCoreAsync(request, cancellationToken);

    public Task<CoordinationClaimResult> TakeoverAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken = default) =>
        AcquireCoreAsync(request, cancellationToken);

    public async Task<CoordinationClaimResult> RenewAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = ValidateMutationRequest(request, requireDuration: true);
        await using var gate = await AcquireResourceLockAsync(resourceKey, cancellationToken).ConfigureAwait(false);
        var current = await ReadClaimAsync(resourceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimNotFound);

        if (IsSameOperation(current, request.TransitionId) && MatchesOwner(current, request, resourceKey))
        {
            return new(CoordinationClaimDisposition.Duplicate, current);
        }

        EnsureCurrentOwner(current, request, resourceKey);
        var now = GetUtcNow();
        if (now >= current.LeaseExpiresAt)
        {
            await ObserveExpiryLockedAsync(
                current,
                BuildDerivedTransitionId(request.TransitionId, "expire"),
                "The lease expired before renewal.",
                cancellationToken).ConfigureAwait(false);
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimExpired);
        }

        var replay = await ReadWorkItemAsync(current, cancellationToken).ConfigureAwait(false);
        EnsureClaimHistory(replay, current);
        if (!string.Equals(replay.Projection!.State, CoordinationStates.Claimed, StringComparison.Ordinal))
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidState);
        }

        var renewedUntil = now.Add(request.LeaseDuration);
        await AppendStateTransitionAsync(
            current.RunId,
            current.WorkItemId,
            current.AttemptId,
            current.SessionId,
            current.ClaimId,
            current.ResourceKey,
            CoordinationEventTypes.WorkItemClaimRenewed,
            CoordinationStates.Claimed,
            request.TransitionId,
            renewedUntil,
            current.FenceGeneration,
            request.Reason ?? "Claim lease renewed.",
            request.Agent,
            request.ClientName,
            cancellationToken).ConfigureAwait(false);

        var renewed = CopyClaim(
            current,
            transitionId: request.TransitionId,
            revision: current.Revision + 1,
            leaseExpiresAt: renewedUntil,
            releasedAt: null,
            releaseReason: null,
            status: CoordinationClaimStatuses.Active);
        await WriteClaimAsync(renewed, cancellationToken).ConfigureAwait(false);
        return new(CoordinationClaimDisposition.Renewed, renewed);
    }

    public Task<CoordinationClaimResult> ReleaseAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default) =>
        EndClaimAsync(
            request,
            CoordinationEventTypes.WorkItemClaimReleased,
            CoordinationStates.Pending,
            CoordinationClaimDisposition.Released,
            request.Reason ?? "Claim released.",
            cancellationToken);

    public async Task<CoordinationClaimResult> ExpireAsync(
        CoordinationClaimExpiryRequest request,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = ValidateExpiryRequest(request);
        await using var gate = await AcquireResourceLockAsync(resourceKey, cancellationToken).ConfigureAwait(false);
        var current = await ReadClaimAsync(resourceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimNotFound);

        if (current.Status != CoordinationClaimStatuses.Active)
        {
            if (current.Status == CoordinationClaimStatuses.Expired &&
                MatchesExpiry(current, request, resourceKey))
            {
                return new(CoordinationClaimDisposition.Duplicate, current);
            }

            throw StatusError(current.Status);
        }

        EnsureCurrentClaim(current, request.RunId, request.WorkItemId, request.AttemptId, request.ClaimId,
            resourceKey, request.FenceGeneration);
        var now = GetUtcNow();
        if (now < current.LeaseExpiresAt)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimNotExpired);
        }

        var expired = await ObserveExpiryLockedAsync(
            current,
            request.TransitionId,
            request.Reason ?? "The claim lease expired.",
            cancellationToken).ConfigureAwait(false);
        return new(CoordinationClaimDisposition.Expired, expired);
    }

    public Task<CoordinationClaimResult> CompleteAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default) =>
        EndClaimAsync(
            request,
            CoordinationEventTypes.WorkItemCompleted,
            CoordinationStates.Completed,
            CoordinationClaimDisposition.Completed,
            request.Reason ?? "Work completed while the claim was active.",
            cancellationToken);

    public Task<CoordinationClaimResult> CancelAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default) =>
        EndClaimAsync(
            request,
            CoordinationEventTypes.WorkItemCanceled,
            CoordinationStates.Canceled,
            CoordinationClaimDisposition.Canceled,
            request.Reason ?? "Work canceled while the claim was active.",
            cancellationToken);

    public async Task<CoordinationClaim?> ReadAsync(
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedResourceKey = NormalizeResourceKey(resourceKey);
        await using var gate = await AcquireResourceLockAsync(normalizedResourceKey, cancellationToken)
            .ConfigureAwait(false);
        var current = await ReadClaimAsync(normalizedResourceKey, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        var replay = await ReadWorkItemAsync(current, cancellationToken).ConfigureAwait(false);
        EnsureClaimHistory(replay, current);
        if (current.Status != CoordinationClaimStatuses.Active)
        {
            return current;
        }

        if (GetUtcNow() < current.LeaseExpiresAt)
        {
            return current;
        }

        return await ObserveExpiryLockedAsync(
            current,
            BuildDerivedTransitionId(current.TransitionId ?? current.ClaimId, "read-expiry"),
            "The expired claim was observed during a read.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CoordinationClaimResult> AcquireCoreAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken)
    {
        ValidateAcquireRequest(request);
        var resourceKey = NormalizeResourceKey(request.ResourceKey);
        await using var gate = await AcquireResourceLockAsync(resourceKey, cancellationToken).ConfigureAwait(false);
        var current = await ReadClaimAsync(resourceKey, cancellationToken).ConfigureAwait(false);
        var disposition = CoordinationClaimDisposition.Acquired;

        if (current is not null)
        {
            var currentReplay = await ReadWorkItemAsync(current, cancellationToken).ConfigureAwait(false);
            EnsureClaimHistory(currentReplay, current);
            if (IsSameOperation(current, request.TransitionId) && MatchesOwner(current, request, resourceKey))
            {
                return new(CoordinationClaimDisposition.Duplicate, current);
            }

            if (current.Status == CoordinationClaimStatuses.Active)
            {
                if (GetUtcNow() < current.LeaseExpiresAt)
                {
                    throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimConflict);
                }

                current = await ObserveExpiryLockedAsync(
                    current,
                    BuildDerivedTransitionId(request.TransitionId, "expire"),
                    "The expired claim was superseded by a new owner.",
                    cancellationToken).ConfigureAwait(false);
                disposition = CoordinationClaimDisposition.TakenOver;
            }
            else if (current.Status == CoordinationClaimStatuses.Expired)
            {
                disposition = CoordinationClaimDisposition.TakenOver;
            }
            else
            {
                disposition = CoordinationClaimDisposition.Acquired;
            }
        }

        var replay = await eventStore.ReplayAsync(request.RunId, request.WorkItemId, cancellationToken)
            .ConfigureAwait(false);
        if (replay.Projection is null)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.WorkItemNotFound);
        }

        if (replay.Projection.State == CoordinationStates.Stale)
        {
            if (current is null || current.Status != CoordinationClaimStatuses.Expired)
            {
                throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
            }

            await AppendStateTransitionAsync(
                request.RunId,
                request.WorkItemId,
                current.AttemptId,
                current.SessionId,
                current.ClaimId,
                resourceKey,
                CoordinationEventTypes.WorkItemReopened,
                CoordinationStates.Pending,
                BuildDerivedTransitionId(request.TransitionId, "reopen"),
                null,
                current.FenceGeneration,
                "The stale work item was reopened for a new attempt.",
                request.Agent,
                request.ClientName,
                cancellationToken).ConfigureAwait(false);
        }
        else if (replay.Projection.State != CoordinationStates.Pending)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidState);
        }

        var fenceGeneration = current is null ? 1 : current.FenceGeneration + 1;
        var claim = CreateClaim(request, resourceKey, fenceGeneration, GetUtcNow());
        await AppendStateTransitionAsync(
            request.RunId,
            request.WorkItemId,
            request.AttemptId,
            request.SessionId,
            claim.ClaimId,
            resourceKey,
            CoordinationEventTypes.WorkItemClaimed,
            CoordinationStates.Claimed,
            request.TransitionId,
            claim.LeaseExpiresAt,
            claim.FenceGeneration,
            "The resource claim was acquired.",
            request.Agent,
            request.ClientName,
            cancellationToken).ConfigureAwait(false);
        await WriteClaimAsync(claim, cancellationToken).ConfigureAwait(false);
        return new(disposition, claim);
    }

    private async Task<CoordinationClaimResult> EndClaimAsync(
        CoordinationClaimMutationRequest request,
        string eventType,
        string nextState,
        CoordinationClaimDisposition disposition,
        string reason,
        CancellationToken cancellationToken)
    {
        var resourceKey = ValidateMutationRequest(request, requireDuration: false);
        await using var gate = await AcquireResourceLockAsync(resourceKey, cancellationToken).ConfigureAwait(false);
        var current = await ReadClaimAsync(resourceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimNotFound);

        if (IsSameOperation(current, request.TransitionId) && MatchesOwner(current, request, resourceKey))
        {
            return new(CoordinationClaimDisposition.Duplicate, current);
        }

        EnsureCurrentOwner(current, request, resourceKey);
        if (GetUtcNow() >= current.LeaseExpiresAt)
        {
            await ObserveExpiryLockedAsync(
                current,
                BuildDerivedTransitionId(request.TransitionId, "expire"),
                "The lease expired before the claim could be completed.",
                cancellationToken).ConfigureAwait(false);
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimExpired);
        }

        var replay = await ReadWorkItemAsync(current, cancellationToken).ConfigureAwait(false);
        EnsureClaimHistory(replay, current);
        if (!string.Equals(replay.Projection!.State, CoordinationStates.Claimed, StringComparison.Ordinal))
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidState);
        }

        await AppendStateTransitionAsync(
            current.RunId,
            current.WorkItemId,
            current.AttemptId,
            current.SessionId,
            current.ClaimId,
            current.ResourceKey,
            eventType,
            nextState,
            request.TransitionId,
            null,
            current.FenceGeneration,
            reason,
            request.Agent,
            request.ClientName,
            cancellationToken).ConfigureAwait(false);

        var released = CopyClaim(
            current,
            transitionId: request.TransitionId,
            revision: current.Revision + 1,
            leaseExpiresAt: current.LeaseExpiresAt,
            releasedAt: GetUtcNow(),
            releaseReason: reason,
            status: CoordinationClaimStatuses.Released);
        await WriteClaimAsync(released, cancellationToken).ConfigureAwait(false);
        return new(disposition, released);
    }

    private async Task<CoordinationClaim> ObserveExpiryLockedAsync(
        CoordinationClaim current,
        string transitionId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (current.Status != CoordinationClaimStatuses.Active)
        {
            return current;
        }

        var replay = await ReadWorkItemAsync(current, cancellationToken).ConfigureAwait(false);
        if (replay.Projection is null)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }

        if (string.Equals(replay.Projection.State, CoordinationStates.Claimed, StringComparison.Ordinal))
        {
            EnsureClaimHistory(replay, current);
            await AppendStateTransitionAsync(
                current.RunId,
                current.WorkItemId,
                current.AttemptId,
                current.SessionId,
                current.ClaimId,
                current.ResourceKey,
                CoordinationEventTypes.WorkItemStale,
                CoordinationStates.Stale,
                transitionId,
                current.LeaseExpiresAt,
                current.FenceGeneration,
                reason,
                current.Agent,
                current.ClientName,
                cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(replay.Projection.State, CoordinationStates.Stale, StringComparison.Ordinal))
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }

        var expired = CopyClaim(
            current,
            transitionId: transitionId,
            revision: current.Revision + 1,
            leaseExpiresAt: current.LeaseExpiresAt,
            releasedAt: GetUtcNow(),
            releaseReason: reason,
            status: CoordinationClaimStatuses.Expired);
        await WriteClaimAsync(expired, cancellationToken).ConfigureAwait(false);
        return expired;
    }

    private async Task<CoordinationReplayResult> ReadWorkItemAsync(
        CoordinationClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            return await eventStore.ReplayAsync(claim.RunId, claim.WorkItemId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CoordinationStoreException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }
    }

    private async Task AppendStateTransitionAsync(
        string runId,
        string workItemId,
        string attemptId,
        string? sessionId,
        string claimId,
        string resourceKey,
        string eventType,
        string nextState,
        string transitionId,
        DateTimeOffset? leaseExpiresAt,
        long fenceGeneration,
        string reason,
        string? agent,
        string? clientName,
        CancellationToken cancellationToken)
    {
        CoordinationReplayResult replay;
        try
        {
            replay = await eventStore.ReplayAsync(runId, workItemId, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationStoreException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }

        if (replay.Projection is null || replay.Events.Count == 0)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.WorkItemNotFound);
        }

        var now = GetUtcNow();
        var previous = replay.Events[^1];
        var transition = new CoordinationEvent
        {
            EventId = Guid.CreateVersion7().ToString("D"),
            RunId = runId,
            WorkItemId = workItemId,
            Project = replay.Projection.Project,
            ResourceScope = [resourceKey],
            AttemptId = attemptId,
            SessionId = sessionId,
            ClaimId = claimId,
            SequenceNumber = previous.SequenceNumber + 1,
            EventType = eventType,
            TransitionId = transitionId,
            OccurredAt = now,
            RecordedAt = now,
            Actor = new CoordinationActor
            {
                Agent = agent,
                ClientName = clientName,
                SessionId = sessionId,
            },
            Payload = new CoordinationTransitionPayload
            {
                PreviousState = replay.Projection.State,
                NextState = nextState,
                ExpectedStateVersion = replay.Projection.StateVersion,
                StateVersion = replay.Projection.StateVersion + 1,
                Reason = reason,
                LeaseExpiresAt = leaseExpiresAt,
                ResourceKey = resourceKey,
                FenceGeneration = fenceGeneration,
            },
            PreviousHash = previous.ContentHash,
            ContentHash = string.Empty,
        };

        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(transition))!.AsObject();
        using var document = JsonDocument.Parse(node.ToJsonString());
        node[CoordinationContract.ContentHashPropertyName] = CanonicalJson.ComputeSha256Hex(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName);
        var hashed = JsonSerializer.Deserialize(
            node.ToJsonString(),
            CoordinationJsonContext.Default.CoordinationEvent)
            ?? throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);

        try
        {
            await eventStore.AppendAsync(hashed, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationStoreException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidState);
        }
    }

    private async Task<CoordinationClaim?> ReadClaimAsync(
        string resourceKey,
        CancellationToken cancellationToken)
    {
        var path = GetLeasePath(resourceKey);
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var validation = await validator.ValidateAsync(
                CoordinationContractKind.CoordinationClaim,
                json,
                cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
            }

            var claim = JsonSerializer.Deserialize(
                json,
                CoordinationJsonContext.Default.CoordinationClaim)
                ?? throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
            ValidatePersistedClaim(claim, resourceKey);
            return claim;
        }
        catch (CoordinationClaimException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }
        catch (VaultAccessDeniedException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.AccessDenied);
        }
        catch (IOException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }
    }

    private async Task WriteClaimAsync(
        CoordinationClaim claim,
        CancellationToken cancellationToken)
    {
        var hashed = WithContentHash(claim);
        var validation = await validator.ValidateAsync(
            CoordinationContractKind.CoordinationClaim,
            hashed,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }

        try
        {
            await fileSystem.WriteAtomicallyAsync(
                GetLeasePath(claim.ResourceKey),
                CoordinationContractSerializer.Serialize(hashed),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VaultAccessDeniedException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.AccessDenied);
        }
        catch (IOException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.AccessDenied);
        }
    }

    private async Task<FileStream> AcquireResourceLockAsync(
        string resourceKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileSystem.AcquireExclusiveLockAsync(
                GetResourceLockPath(resourceKey),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VaultAccessDeniedException)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.AccessDenied);
        }
    }

    private string NormalizeResourceKey(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey) || resourceKey.Length > 512)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidResource);
        }

        if (resourceKey.StartsWith("note:", StringComparison.Ordinal))
        {
            var relative = resourceKey["note:".Length..].Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            {
                throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidResource);
            }

            try
            {
                var operationalPath = paths.ResolveVaultReadPath(relative);
                var canonicalRelative = Path.GetRelativePath(paths.VaultRoot, operationalPath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (canonicalRelative == "." || canonicalRelative.StartsWith("../", StringComparison.Ordinal) ||
                    Path.IsPathRooted(canonicalRelative))
                {
                    throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidResource);
                }

                return $"note:{canonicalRelative}";
            }
            catch (VaultAccessDeniedException)
            {
                throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidResource);
            }
        }

        if (resourceKey.StartsWith("logical:", StringComparison.Ordinal) &&
            IsSafeLogicalResource(resourceKey["logical:".Length..]))
        {
            return resourceKey;
        }

        throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidResource);
    }

    private static bool IsSafeLogicalResource(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 504 &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/');

    private static string ValidateAcquireRequest(CoordinationClaimAcquireRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentifiers(request.RunId, request.WorkItemId, request.AttemptId, request.SessionId,
            request.TransitionId);
        ValidateLeaseDuration(request.LeaseDuration);
        if (request.AuthorityScope is { Count: > 0 })
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.AuthorityScopeDenied);
        }

        ValidateActor(request.Agent, request.ClientName);
        return request.ResourceKey;
    }

    private string ValidateMutationRequest(
        CoordinationClaimMutationRequest request,
        bool requireDuration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentifiers(request.RunId, request.WorkItemId, request.AttemptId, request.SessionId,
            request.ClaimId, request.TransitionId);
        ValidateActor(request.Agent, request.ClientName);
        if (request.FenceGeneration < 1)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.UnsafeIdentifier);
        }

        if (requireDuration)
        {
            ValidateLeaseDuration(request.LeaseDuration);
        }

        return NormalizeResourceKey(request.ResourceKey);
    }

    private string ValidateExpiryRequest(CoordinationClaimExpiryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentifiers(request.RunId, request.WorkItemId, request.AttemptId, request.ClaimId,
            request.TransitionId);
        if (request.FenceGeneration < 1)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.UnsafeIdentifier);
        }

        return NormalizeResourceKey(request.ResourceKey);
    }

    private static void ValidateLeaseDuration(TimeSpan duration)
    {
        if (duration < CoordinationClaimLeasePolicy.MinimumDuration ||
            duration > CoordinationClaimLeasePolicy.MaximumDuration)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.InvalidDuration);
        }
    }

    private static void ValidateIdentifiers(params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
                value.Any(character =>
                    !char.IsLetterOrDigit(character) &&
                    character is not '-' and not '_' and not '.' and not ':'))
            {
                throw new CoordinationClaimException(CoordinationClaimErrorCodes.UnsafeIdentifier);
            }
        }
    }

    private static void ValidateActor(string? agent, string? clientName)
    {
        if (agent is { Length: > 128 } || clientName is { Length: > 128 })
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.UnsafeIdentifier);
        }
    }

    private static void ValidatePersistedClaim(CoordinationClaim claim, string resourceKey)
    {
        if (claim.FenceGeneration < 1 || claim.Revision < 0 ||
            !string.Equals(claim.ResourceKey, resourceKey, StringComparison.Ordinal) ||
            claim.Status is not (CoordinationClaimStatuses.Active or CoordinationClaimStatuses.Released or
                CoordinationClaimStatuses.Expired or CoordinationClaimStatuses.Fenced))
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }
    }

    private static bool IsSameOperation(CoordinationClaim claim, string transitionId) =>
        claim.TransitionId is not null &&
        string.Equals(claim.TransitionId, transitionId, StringComparison.Ordinal);

    private static bool MatchesOwner(
        CoordinationClaim claim,
        CoordinationClaimAcquireRequest request,
        string resourceKey) =>
        string.Equals(claim.RunId, request.RunId, StringComparison.Ordinal) &&
        string.Equals(claim.WorkItemId, request.WorkItemId, StringComparison.Ordinal) &&
        string.Equals(claim.AttemptId, request.AttemptId, StringComparison.Ordinal) &&
        string.Equals(claim.SessionId, request.SessionId, StringComparison.Ordinal) &&
        string.Equals(claim.ResourceKey, resourceKey, StringComparison.Ordinal);

    private static bool MatchesOwner(
        CoordinationClaim claim,
        CoordinationClaimMutationRequest request,
        string resourceKey) =>
        string.Equals(claim.RunId, request.RunId, StringComparison.Ordinal) &&
        string.Equals(claim.WorkItemId, request.WorkItemId, StringComparison.Ordinal) &&
        string.Equals(claim.AttemptId, request.AttemptId, StringComparison.Ordinal) &&
        string.Equals(claim.SessionId, request.SessionId, StringComparison.Ordinal) &&
        string.Equals(claim.ClaimId, request.ClaimId, StringComparison.Ordinal) &&
        string.Equals(claim.ResourceKey, resourceKey, StringComparison.Ordinal) &&
        claim.FenceGeneration == request.FenceGeneration;

    private static bool MatchesExpiry(
        CoordinationClaim claim,
        CoordinationClaimExpiryRequest request,
        string resourceKey) =>
        string.Equals(claim.RunId, request.RunId, StringComparison.Ordinal) &&
        string.Equals(claim.WorkItemId, request.WorkItemId, StringComparison.Ordinal) &&
        string.Equals(claim.AttemptId, request.AttemptId, StringComparison.Ordinal) &&
        string.Equals(claim.ClaimId, request.ClaimId, StringComparison.Ordinal) &&
        string.Equals(claim.ResourceKey, resourceKey, StringComparison.Ordinal) &&
        claim.FenceGeneration == request.FenceGeneration;

    private static void EnsureCurrentOwner(
        CoordinationClaim current,
        CoordinationClaimMutationRequest request,
        string resourceKey)
    {
        if (current.Status != CoordinationClaimStatuses.Active)
        {
            throw StatusError(current.Status);
        }

        EnsureCurrentClaim(current, request.RunId, request.WorkItemId, request.AttemptId, request.ClaimId,
            resourceKey, request.FenceGeneration, request.SessionId);
    }

    private static void EnsureCurrentClaim(
        CoordinationClaim current,
        string runId,
        string workItemId,
        string attemptId,
        string claimId,
        string resourceKey,
        long fenceGeneration,
        string? sessionId = null)
    {
        if (!string.Equals(current.ResourceKey, resourceKey, StringComparison.Ordinal) ||
            !string.Equals(current.RunId, runId, StringComparison.Ordinal) ||
            !string.Equals(current.WorkItemId, workItemId, StringComparison.Ordinal))
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.NotOwner);
        }

        if (!string.Equals(current.ClaimId, claimId, StringComparison.Ordinal) ||
            current.FenceGeneration != fenceGeneration ||
            !string.Equals(current.AttemptId, attemptId, StringComparison.Ordinal) ||
            (sessionId is not null && !string.Equals(current.SessionId, sessionId, StringComparison.Ordinal)))
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.ClaimFenced);
        }
    }

    private static void EnsureClaimHistory(
        CoordinationReplayResult replay,
        CoordinationClaim claim)
    {
        if (replay.Events.Count == 0 || replay.Projection is null)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }

        var last = replay.Events[^1];
        if (!string.Equals(last.ClaimId, claim.ClaimId, StringComparison.Ordinal) ||
            !string.Equals(last.Payload.ResourceKey, claim.ResourceKey, StringComparison.Ordinal) ||
            last.Payload.FenceGeneration != claim.FenceGeneration)
        {
            throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
        }
    }

    private static CoordinationClaimException StatusError(string status) => status switch
    {
        CoordinationClaimStatuses.Expired => new(CoordinationClaimErrorCodes.ClaimExpired),
        CoordinationClaimStatuses.Released => new(CoordinationClaimErrorCodes.ClaimReleased),
        CoordinationClaimStatuses.Fenced => new(CoordinationClaimErrorCodes.ClaimFenced),
        _ => new(CoordinationClaimErrorCodes.CorruptClaimState),
    };

    private static CoordinationClaim CreateClaim(
        CoordinationClaimAcquireRequest request,
        string resourceKey,
        long fenceGeneration,
        DateTimeOffset now) =>
        WithContentHash(new CoordinationClaim
        {
            ClaimId = Guid.CreateVersion7().ToString("D"),
            RunId = request.RunId,
            WorkItemId = request.WorkItemId,
            AttemptId = request.AttemptId,
            ResourceKey = resourceKey,
            TransitionId = request.TransitionId,
            Revision = 1,
            FenceGeneration = fenceGeneration,
            Status = CoordinationClaimStatuses.Active,
            AcquiredAt = now,
            LeaseExpiresAt = now.Add(request.LeaseDuration),
            ReleasedAt = null,
            ReleaseReason = null,
            SessionId = request.SessionId,
            Agent = request.Agent,
            ClientName = request.ClientName,
            ContentHash = string.Empty,
        });

    private static CoordinationClaim CopyClaim(
        CoordinationClaim current,
        string transitionId,
        long revision,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset? releasedAt,
        string? releaseReason,
        string status) =>
        WithContentHash(new CoordinationClaim
        {
            ClaimId = current.ClaimId,
            RunId = current.RunId,
            WorkItemId = current.WorkItemId,
            AttemptId = current.AttemptId,
            ResourceKey = current.ResourceKey,
            TransitionId = transitionId,
            Revision = revision,
            FenceGeneration = current.FenceGeneration,
            Status = status,
            AcquiredAt = current.AcquiredAt,
            LeaseExpiresAt = leaseExpiresAt,
            ReleasedAt = releasedAt,
            ReleaseReason = releaseReason,
            SessionId = current.SessionId,
            Agent = current.Agent,
            ClientName = current.ClientName,
            ContentHash = string.Empty,
        });

    private static CoordinationClaim WithContentHash(CoordinationClaim claim)
    {
        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(claim))!.AsObject();
        using var document = JsonDocument.Parse(node.ToJsonString());
        node[CoordinationContract.ContentHashPropertyName] = CanonicalJson.ComputeSha256Hex(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName);
        return JsonSerializer.Deserialize(
            node.ToJsonString(),
            CoordinationJsonContext.Default.CoordinationClaim)
            ?? throw new CoordinationClaimException(CoordinationClaimErrorCodes.CorruptClaimState);
    }

    private string GetLeasePath(string resourceKey) =>
        paths.ResolveVaultWritePath(Path.Combine(
            CoordinationRoot,
            LeaseRoot,
            $"{HashResourceKey(resourceKey)}.json"));

    private string GetResourceLockPath(string resourceKey) =>
        paths.ResolveVaultWritePath(Path.Combine(
            CoordinationRoot,
            RuntimeLockRoot,
            $"{HashResourceKey(resourceKey)}.lock"));

    private static string HashResourceKey(string resourceKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey)));

    private static string BuildDerivedTransitionId(string source, string suffix) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{suffix}")));

    private DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow().ToUniversalTime();
}
