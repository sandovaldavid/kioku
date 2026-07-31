using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

public static class CoordinationConflictStoreErrorCodes
{
    public const string AccessDenied = "access-denied";
    public const string ConflictAlreadyResolved = "conflict-already-resolved";
    public const string ConflictNotFound = "conflict-not-found";
    public const string CorruptConflict = "corrupt-conflict";
    public const string DuplicateConflict = "duplicate-conflict";
    public const string InvalidConflict = "invalid-conflict";
    public const string UnsafeIdentifier = "unsafe-identifier";
}

public sealed class CoordinationConflictStoreException(string code)
    : InvalidOperationException($"Coordination conflict operation failed: {code}.")
{
    public string Code { get; } = code;
}

/// <summary>
/// Stores conflict records separately from immutable event history so acknowledgement does not
/// rewrite or delete the original transition evidence.
/// </summary>
internal sealed class CoordinationConflictStore(
    VaultPathPolicy paths,
    ICoordinationFileSystem fileSystem,
    CoordinationContractValidator validator,
    TimeProvider timeProvider) : ICoordinationConflictStore
{
    private const string ConflictRoot = ".kioku/coordination/conflicts";
    private const string LockRoot = ".kioku/coordination/runtime/locks/conflicts";

    public async Task<CoordinationConflict> RecordAsync(
        CoordinationConflict conflict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ValidateIdentifier(conflict.ConflictId);
        var candidate = WithContentHash(conflict);
        await ValidateAsync(candidate, cancellationToken).ConfigureAwait(false);

        await using var gate = await AcquireLockAsync(candidate.ConflictId, cancellationToken).ConfigureAwait(false);
        var path = GetConflictPath(candidate.ConflictId);
        var existing = await ReadAtPathAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (string.Equals(
                    CoordinationContractSerializer.Serialize(existing),
                    CoordinationContractSerializer.Serialize(candidate),
                    StringComparison.Ordinal))
            {
                return existing;
            }

            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.DuplicateConflict);
        }

        try
        {
            await fileSystem.WriteNewAtomicallyAsync(
                path,
                CoordinationContractSerializer.Serialize(candidate),
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (fileSystem.FileExists(path))
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.DuplicateConflict);
        }

        return candidate;
    }

    public async Task<IReadOnlyList<CoordinationConflict>> ListAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var root = paths.ResolveVaultReadPath(ConflictRoot);
        var conflicts = new List<CoordinationConflict>();
        foreach (var candidatePath in fileSystem.EnumerateJsonFiles(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conflict = await ReadAtPathAsync(candidatePath, cancellationToken).ConfigureAwait(false);
            if (conflict is null ||
                (runId is not null && !string.Equals(conflict.RunId, runId, StringComparison.Ordinal)) ||
                (workItemId is not null && !string.Equals(conflict.WorkItemId, workItemId, StringComparison.Ordinal)) ||
                (status is not null && !string.Equals(conflict.Status, status, StringComparison.Ordinal)))
            {
                continue;
            }

            conflicts.Add(conflict);
        }

        return conflicts
            .OrderByDescending(conflict => conflict.DetectedAt)
            .ThenBy(conflict => conflict.ConflictId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<CoordinationConflict> ResolveAsync(
        string conflictId,
        string status,
        string resolution,
        CoordinationActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(conflictId);
        if (status is not (CoordinationConflictStatuses.Resolved or CoordinationConflictStatuses.Ignored) ||
            string.IsNullOrWhiteSpace(resolution) ||
            resolution.Length > 2000)
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.InvalidConflict);
        }

        ArgumentNullException.ThrowIfNull(actor);
        await using var gate = await AcquireLockAsync(conflictId, cancellationToken).ConfigureAwait(false);
        var current = await ReadAtPathAsync(GetConflictPath(conflictId), cancellationToken).ConfigureAwait(false)
            ?? throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.ConflictNotFound);
        if (current.Status != CoordinationConflictStatuses.Open)
        {
            if (current.Status == status && string.Equals(current.Resolution, resolution, StringComparison.Ordinal))
            {
                return current;
            }

            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.ConflictAlreadyResolved);
        }

        var resolved = WithContentHash(new CoordinationConflict
        {
            ConflictId = current.ConflictId,
            RunId = current.RunId,
            WorkItemId = current.WorkItemId,
            AttemptId = current.AttemptId,
            ResourceKey = current.ResourceKey,
            Kind = current.Kind,
            Status = status,
            DetectedAt = current.DetectedAt,
            ExpectedRevision = current.ExpectedRevision,
            ActualRevision = current.ActualRevision,
            ExpectedHash = current.ExpectedHash,
            ActualHash = current.ActualHash,
            Description = current.Description,
            Resolution = resolution,
            ResolvedAt = timeProvider.GetUtcNow().ToUniversalTime(),
            ResolvedBy = actor,
            ContentHash = string.Empty,
        });
        await ValidateAsync(resolved, cancellationToken).ConfigureAwait(false);
        await fileSystem.WriteAtomicallyAsync(
            GetConflictPath(conflictId),
            CoordinationContractSerializer.Serialize(resolved),
            cancellationToken).ConfigureAwait(false);
        return resolved;
    }

    private async Task<CoordinationConflict?> ReadAtPathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var validation = await validator.ValidateAsync(
                CoordinationContractKind.CoordinationConflict,
                json,
                cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.CorruptConflict);
            }

            return JsonSerializer.Deserialize(
                json,
                CoordinationJsonContext.Default.CoordinationConflict)
                ?? throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.CorruptConflict);
        }
        catch (CoordinationConflictStoreException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.CorruptConflict);
        }
        catch (VaultAccessDeniedException)
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.AccessDenied);
        }
        catch (IOException)
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.CorruptConflict);
        }
    }

    private async Task ValidateAsync(
        CoordinationConflict conflict,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(
            CoordinationContractKind.CoordinationConflict,
            conflict,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.InvalidConflict);
        }
    }

    private async Task<FileStream> AcquireLockAsync(string conflictId, CancellationToken cancellationToken)
    {
        try
        {
            var lockName = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(conflictId)));
            return await fileSystem.AcquireExclusiveLockAsync(
                paths.ResolveVaultWritePath(Path.Combine(LockRoot, $"{lockName}.lock")),
                cancellationToken).ConfigureAwait(false);
        }
        catch (VaultAccessDeniedException)
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.AccessDenied);
        }
    }

    private string GetConflictPath(string conflictId) =>
        paths.ResolveVaultWritePath(Path.Combine(ConflictRoot, $"{conflictId}.json"));

    private static CoordinationConflict WithContentHash(CoordinationConflict conflict)
    {
        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(conflict))!.AsObject();
        using var document = JsonDocument.Parse(node.ToJsonString());
        node[CoordinationContract.ContentHashPropertyName] = CanonicalJson.ComputeSha256Hex(
            document.RootElement,
            CoordinationContract.ContentHashPropertyName);
        return JsonSerializer.Deserialize(
            node.ToJsonString(),
            CoordinationJsonContext.Default.CoordinationConflict)
            ?? throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.CorruptConflict);
    }

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new CoordinationConflictStoreException(CoordinationConflictStoreErrorCodes.UnsafeIdentifier);
        }
    }
}
