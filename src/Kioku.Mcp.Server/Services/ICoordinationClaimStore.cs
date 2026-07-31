using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for durable, cross-process coordination claims.
/// </summary>
public interface ICoordinationClaimStore
{
    Task<CoordinationClaimResult> AcquireAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> TakeoverAsync(
        CoordinationClaimAcquireRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> RenewAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> ReleaseAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> ExpireAsync(
        CoordinationClaimExpiryRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> CompleteAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaimResult> CancelAsync(
        CoordinationClaimMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinationClaim?> ReadAsync(
        string resourceKey,
        CancellationToken cancellationToken = default);
}
