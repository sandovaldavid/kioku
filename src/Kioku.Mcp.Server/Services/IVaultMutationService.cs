using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Central application boundary for vault writes. Implementations acquire the durable resource
/// lock before reading the current revision, validating preconditions, and committing a mutation.
/// </summary>
public interface IVaultMutationService
{
    Task<VaultMutationReceipt> CreateTextAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default);

    Task<VaultMutationReceipt> WriteTextAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default);

    Task<VaultMutationReceipt> UpsertTextAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default);

    Task<VaultMutationReceipt> DeleteAsync(
        string path,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default);

    Task<VaultMutationReceipt> MoveAsync(
        string sourcePath,
        string destinationPath,
        string? replacementContent = null,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default);
}
