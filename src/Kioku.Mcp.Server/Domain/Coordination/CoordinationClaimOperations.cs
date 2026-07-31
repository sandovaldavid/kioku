namespace Kioku.Mcp.Server.Domain.Coordination;

/// <summary>
/// Bounded lease durations accepted by the local coordination claim service.
/// </summary>
public static class CoordinationClaimLeasePolicy
{
    public static TimeSpan MinimumDuration => TimeSpan.FromSeconds(1);

    public static TimeSpan MaximumDuration => TimeSpan.FromHours(1);

    public static TimeSpan DefaultDuration => TimeSpan.FromSeconds(30);
}

/// <summary>
/// Stable outcomes returned by claim lifecycle operations.
/// </summary>
public enum CoordinationClaimDisposition
{
    Acquired,
    Renewed,
    Released,
    Expired,
    TakenOver,
    Completed,
    Canceled,
    Duplicate,
}

/// <summary>
/// Stable, content-safe claim operation failures.
/// </summary>
public static class CoordinationClaimErrorCodes
{
    public const string AccessDenied = "access-denied";
    public const string AuthorityScopeDenied = "authority-scope-denied";
    public const string ClaimConflict = "claim-conflict";
    public const string ClaimExpired = "claim-expired";
    public const string ClaimNotFound = "claim-not-found";
    public const string ClaimNotExpired = "claim-not-expired";
    public const string ClaimReleased = "claim-released";
    public const string ClaimFenced = "claim-fenced";
    public const string CorruptClaimState = "corrupt-claim-state";
    public const string InvalidDuration = "invalid-duration";
    public const string InvalidResource = "invalid-resource";
    public const string InvalidState = "invalid-state";
    public const string NotOwner = "not-owner";
    public const string UnsafeIdentifier = "unsafe-identifier";
    public const string WorkItemNotFound = "work-item-not-found";
}

/// <summary>
/// Content-safe exception raised by claim lifecycle operations.
/// </summary>
public sealed class CoordinationClaimException(string code)
    : InvalidOperationException($"Coordination claim operation failed: {code}.")
{
    public string Code { get; } = code;
}

/// <summary>
/// Server request to acquire or take over one canonical resource claim.
/// </summary>
public sealed class CoordinationClaimAcquireRequest
{
    public required string RunId { get; init; }

    public required string WorkItemId { get; init; }

    public required string AttemptId { get; init; }

    public required string SessionId { get; init; }

    public required string ResourceKey { get; init; }

    public required string TransitionId { get; init; }

    public TimeSpan LeaseDuration { get; init; } = CoordinationClaimLeasePolicy.DefaultDuration;

    public IReadOnlyList<string> AuthorityScope { get; init; } = [];

    public string? Agent { get; init; }

    public string? ClientName { get; init; }
}

/// <summary>
/// Owner-authenticated request for a current claim mutation.
/// </summary>
public sealed class CoordinationClaimMutationRequest
{
    public required string RunId { get; init; }

    public required string WorkItemId { get; init; }

    public required string AttemptId { get; init; }

    public required string SessionId { get; init; }

    public required string ClaimId { get; init; }

    public required string ResourceKey { get; init; }

    public required string TransitionId { get; init; }

    public required long FenceGeneration { get; init; }

    public TimeSpan LeaseDuration { get; init; } = CoordinationClaimLeasePolicy.DefaultDuration;

    public string? Agent { get; init; }

    public string? ClientName { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Server observation request used to persist an expired claim.
/// </summary>
public sealed class CoordinationClaimExpiryRequest
{
    public required string RunId { get; init; }

    public required string WorkItemId { get; init; }

    public required string AttemptId { get; init; }

    public required string ClaimId { get; init; }

    public required string ResourceKey { get; init; }

    public required string TransitionId { get; init; }

    public required long FenceGeneration { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Result of a claim lifecycle operation.
/// </summary>
public sealed record CoordinationClaimResult(
    CoordinationClaimDisposition Disposition,
    CoordinationClaim Claim);
