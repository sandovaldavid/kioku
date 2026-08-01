using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for durable, cross-process coordination claims.
/// </summary>
public interface ICoordinationClaimStore
{
    /// <summary>
    /// Runs an operation while holding every canonical resource lock in a stable order.
    /// The callback receives the claims observed while those locks are held.
    /// </summary>
    Task<TResult> ExecuteUnderResourceLocksAsync<TResult>(
        IReadOnlyList<string> resourceKeys,
        Func<IReadOnlyDictionary<string, CoordinationClaim?>, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<CoordinationClaim>> ListAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        CancellationToken cancellationToken = default);
}
