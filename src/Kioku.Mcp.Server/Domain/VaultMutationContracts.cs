using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Stable failures returned when a vault mutation cannot satisfy its preconditions.
/// </summary>
public static class VaultMutationErrorCodes
{
    public const string WriteConflict = "WRITE_CONFLICT";
    public const string InvalidPrecondition = "INVALID_PRECONDITION";
    public const string MutationIdReused = "MUTATION_ID_REUSED";
}

/// <summary>
/// Optional optimistic-concurrency and claim preconditions for one vault mutation.
/// Empty preconditions preserve the legacy unconditional-write behavior.
/// </summary>
public sealed class VaultMutationPreconditions
{
    public static VaultMutationPreconditions FromToolArguments(
        string expectedRevision = "",
        string expectedHash = "",
        string claimId = "",
        long fenceGeneration = 0,
        string resourceKey = "",
        string mutationId = "") =>
        new()
        {
            ExpectedRevision = string.IsNullOrWhiteSpace(expectedRevision) ? null : expectedRevision,
            ExpectedHash = string.IsNullOrWhiteSpace(expectedHash) ? null : expectedHash,
            ClaimId = string.IsNullOrWhiteSpace(claimId) ? null : claimId,
            FenceGeneration = fenceGeneration > 0 ? fenceGeneration : null,
            ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? null : resourceKey,
            MutationId = string.IsNullOrWhiteSpace(mutationId) ? null : mutationId,
        };

    public string? ExpectedRevision { get; init; }

    public string? ExpectedHash { get; init; }

    public string? ResourceKey { get; init; }

    public string? ClaimId { get; init; }

    public long? FenceGeneration { get; init; }

    public string? MutationId { get; init; }

    public bool HasContentPrecondition =>
        !string.IsNullOrWhiteSpace(ExpectedRevision) || !string.IsNullOrWhiteSpace(ExpectedHash);

    public bool HasClaimPrecondition =>
        !string.IsNullOrWhiteSpace(ClaimId) || FenceGeneration.HasValue;
}

/// <summary>
/// Safe metadata describing a rejected mutation. It never contains note content.
/// </summary>
public sealed record VaultMutationConflict(
    string Code,
    string ResourceKey,
    string? ExpectedRevision,
    string? ActualRevision,
    string? ExpectedHash,
    string? ActualHash,
    long? CurrentFenceGeneration,
    string RecoveryAction);

/// <summary>
/// Exception used internally to preserve a stable mutation error at MCP boundaries.
/// </summary>
public sealed class VaultMutationException : InvalidOperationException
{
    public VaultMutationException(string code, string message, VaultMutationConflict? conflict = null)
        : base(message)
    {
        Code = code;
        Conflict = conflict;
    }

    public string Code { get; }

    public VaultMutationConflict? Conflict { get; }

    public string ToToolError()
    {
        if (Conflict is not null)
        {
            return $"[error:{Code}] {JsonSerializer.Serialize(Conflict)}";
        }

        return KiokuError.Format(Code, Message);
    }
}

/// <summary>
/// Safe result metadata returned after a committed vault mutation.
/// </summary>
public sealed record VaultMutationReceipt(
    string ResourceKey,
    string Path,
    string? Revision,
    bool AlreadyApplied = false);

/// <summary>
/// Computes the stable SHA-256 revision token exposed by read tools and accepted by CAS writes.
/// </summary>
public static class VaultRevision
{
    public static string Compute(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
