using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Domain.Coordination;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Serializes vault mutations across Kioku processes and applies optimistic concurrency and
/// optional claim/fencing checks before an atomic filesystem change.
/// </summary>
internal sealed class VaultMutationService(
    VaultPathPolicy paths,
    ICoordinationFileSystem fileSystem,
    ICoordinationClaimStore claims,
    IVaultIndexOperations index,
    TimeProvider timeProvider,
    ICoordinationFaultInjector? faultInjector = null,
    MetricsService? metrics = null,
    ILogger<VaultMutationService>? logger = null) : IVaultMutationService
{
    private const string MutationRecordRoot = ".kioku/coordination/mutations";

    public Task<VaultMutationReceipt> CreateTextAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(path, content, preconditions, requireAbsent: true, cancellationToken);

    public Task<VaultMutationReceipt> WriteTextAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(path, content, preconditions, requireAbsent: false, cancellationToken);

    public async Task<VaultMutationReceipt> DeleteAsync(
        string path,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveDeletePath(path);
        var normalized = NormalizePreconditions(preconditions);
        var resourceKey = ResolveResourceKey(source, normalized);
        var fingerprint = ComputeFingerprint("delete", resourceKey, source, null, normalized);
        using var activity = metrics?.StartCoordinationActivity(
            "coordination.mutation.delete",
            claimId: normalized.ClaimId);

        try
        {
            return await claims.ExecuteUnderResourceLocksAsync(
                    BuildLockKeys(normalized.MutationId, resourceKey),
                    async observed =>
                    {
                        var current = await ReadCurrentAsync(source, cancellationToken).ConfigureAwait(false);
                        var existing = await ReadMutationRecordAsync(normalized.MutationId, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing is not null)
                        {
                            return ConfirmDuplicate(existing, fingerprint, resourceKey, source);
                        }

                        ValidatePreconditions(
                            normalized,
                            resourceKey,
                            current,
                            observed[resourceKey],
                            timeProvider,
                            requireExists: true);
                        await InjectAsync(
                                CoordinationFaultPoint.AfterCasValidationBeforeWrite,
                                cancellationToken)
                            .ConfigureAwait(false);
                        var latest = await ReadCurrentAsync(source, cancellationToken).ConfigureAwait(false);
                        ValidatePreconditions(
                            normalized,
                            resourceKey,
                            latest,
                            observed[resourceKey],
                            timeProvider,
                            requireExists: true);
                        fileSystem.DeleteFile(source);
                        await DeleteFromIndexAsync(source, cancellationToken).ConfigureAwait(false);
                        await PersistMutationRecordAsync(
                            normalized.MutationId,
                            fingerprint,
                            new VaultMutationReceipt(resourceKey, RelativePath(source), null),
                            cancellationToken).ConfigureAwait(false);
                        metrics?.RecordCoordinationMutation("committed");
                        return new VaultMutationReceipt(resourceKey, RelativePath(source), null);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VaultMutationException exception)
        {
            RecordMutationFailure(exception.Code);
            throw;
        }
        catch (VaultAccessDeniedException)
        {
            RecordMutationFailure("ACCESS_DENIED");
            throw AccessDenied();
        }
        catch (IOException)
        {
            RecordMutationFailure("WRITE_CONFLICT");
            throw new VaultMutationException(VaultMutationErrorCodes.WriteConflict, "The vault mutation could not be committed.",
                new VaultMutationConflict(
                    VaultMutationErrorCodes.WriteConflict,
                    resourceKey,
                    normalized.ExpectedRevision,
                    null,
                    normalized.ExpectedHash,
                    null,
                    null,
                    "Retry after checking the target resource."));
        }
    }

    public async Task<VaultMutationReceipt> MoveAsync(
        string sourcePath,
        string destinationPath,
        string? replacementContent = null,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveDeletePath(sourcePath);
        var destination = ResolveWritePath(destinationPath);
        var normalized = NormalizePreconditions(preconditions);
        var sourceResource = ResolveResourceKey(source, normalized);
        var destinationResource = ResolveResourceKey(destination, null);
        var fingerprint = ComputeFingerprint(
            "move", sourceResource, source, destination + "\n" + replacementContent, normalized);
        using var activity = metrics?.StartCoordinationActivity(
            "coordination.mutation.move",
            claimId: normalized.ClaimId);

        try
        {
            return await claims.ExecuteUnderResourceLocksAsync(
                    BuildLockKeys(normalized.MutationId, sourceResource, destinationResource),
                    async observed =>
                    {
                        var current = await ReadCurrentAsync(source, cancellationToken).ConfigureAwait(false);
                        var existing = await ReadMutationRecordAsync(normalized.MutationId, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing is not null)
                        {
                            return ConfirmDuplicate(existing, fingerprint, sourceResource, RelativePath(destination));
                        }

                        ValidatePreconditions(
                            normalized,
                            sourceResource,
                            current,
                            observed[sourceResource],
                            timeProvider,
                            requireExists: true);
                        if (fileSystem.FileExists(destination))
                        {
                            throw Conflict(
                                "DESTINATION_EXISTS",
                                sourceResource,
                                normalized,
                                current,
                                observed[sourceResource],
                                "Choose a different destination or re-read the target resource.");
                        }

                        await InjectAsync(
                                CoordinationFaultPoint.AfterCasValidationBeforeWrite,
                                cancellationToken)
                            .ConfigureAwait(false);
                        current = await ReadCurrentAsync(source, cancellationToken).ConfigureAwait(false);
                        ValidatePreconditions(
                            normalized,
                            sourceResource,
                            current,
                            observed[sourceResource],
                            timeProvider,
                            requireExists: true);
                        if (fileSystem.FileExists(destination))
                        {
                            throw Conflict(
                                "DESTINATION_EXISTS",
                                sourceResource,
                                normalized,
                                current,
                                observed[sourceResource],
                                "Choose a different destination or re-read the target resource.");
                        }

                        var directory = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            fileSystem.CreateDirectory(directory);
                        }

                        fileSystem.MoveFile(source, destination, overwrite: false);
                        if (replacementContent is not null)
                        {
                            await fileSystem.WriteAtomicallyAsync(
                                    destination, replacementContent, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        await MoveInIndexAsync(source, destination, cancellationToken).ConfigureAwait(false);
                        var receipt = new VaultMutationReceipt(
                            sourceResource,
                            RelativePath(destination),
                            replacementContent is null
                                ? current is null ? null : VaultRevision.Compute(current)
                                : VaultRevision.Compute(replacementContent));
                        await PersistMutationRecordAsync(
                            normalized.MutationId, fingerprint, receipt, cancellationToken).ConfigureAwait(false);
                        metrics?.RecordCoordinationMutation("committed");
                        return receipt;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VaultMutationException exception)
        {
            RecordMutationFailure(exception.Code);
            throw;
        }
        catch (VaultAccessDeniedException)
        {
            RecordMutationFailure("ACCESS_DENIED");
            throw AccessDenied();
        }
        catch (IOException)
        {
            RecordMutationFailure("WRITE_CONFLICT");
            throw new VaultMutationException(
                VaultMutationErrorCodes.WriteConflict, "The vault move could not be committed.");
        }
    }

    private async Task<VaultMutationReceipt> WriteCoreAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions,
        bool requireAbsent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var target = ResolveWritePath(path);
        var normalized = NormalizePreconditions(preconditions);
        var resourceKey = ResolveResourceKey(target, normalized);
        var fingerprint = ComputeFingerprint(
            requireAbsent ? "create" : "write", resourceKey, target, content, normalized);
        using var activity = metrics?.StartCoordinationActivity(
            requireAbsent ? "coordination.mutation.create" : "coordination.mutation.write",
            claimId: normalized.ClaimId);

        try
        {
            return await claims.ExecuteUnderResourceLocksAsync(
                    BuildLockKeys(normalized.MutationId, resourceKey),
                    async observed =>
                    {
                        var current = await ReadCurrentAsync(target, cancellationToken).ConfigureAwait(false);
                        var existing = await ReadMutationRecordAsync(normalized.MutationId, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing is not null)
                        {
                            return ConfirmDuplicate(existing, fingerprint, resourceKey, RelativePath(target));
                        }

                        ValidatePreconditions(
                            normalized,
                            resourceKey,
                            current,
                            observed[resourceKey],
                            timeProvider,
                            requireExists: !requireAbsent);
                        if (requireAbsent && current is not null)
                        {
                            throw new VaultMutationException(
                                "INVALID_ARGUMENT",
                                $"The target already exists: '{RelativePath(target)}'.");
                        }

                        await InjectAsync(
                                CoordinationFaultPoint.AfterCasValidationBeforeWrite,
                                cancellationToken)
                            .ConfigureAwait(false);
                        current = await ReadCurrentAsync(target, cancellationToken).ConfigureAwait(false);
                        ValidatePreconditions(
                            normalized,
                            resourceKey,
                            current,
                            observed[resourceKey],
                            timeProvider,
                            requireExists: !requireAbsent);
                        if (requireAbsent && current is not null)
                        {
                            throw new VaultMutationException(
                                "INVALID_ARGUMENT",
                                $"The target already exists: '{RelativePath(target)}'.");
                        }

                        await fileSystem.WriteAtomicallyAsync(target, content, cancellationToken)
                            .ConfigureAwait(false);
                        await InjectAsync(
                                CoordinationFaultPoint.AfterTargetWriteBeforeReindex,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await ReindexAsync(target, cancellationToken).ConfigureAwait(false);
                        var receipt = new VaultMutationReceipt(
                            resourceKey,
                            RelativePath(target),
                            VaultRevision.Compute(content));
                        await PersistMutationRecordAsync(
                            normalized.MutationId, fingerprint, receipt, cancellationToken).ConfigureAwait(false);
                        metrics?.RecordCoordinationMutation("committed");
                        return receipt;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VaultMutationException exception)
        {
            RecordMutationFailure(exception.Code);
            throw;
        }
        catch (VaultAccessDeniedException)
        {
            RecordMutationFailure("ACCESS_DENIED");
            throw AccessDenied();
        }
        catch (IOException)
        {
            RecordMutationFailure("WRITE_CONFLICT");
            throw new VaultMutationException(
                "INTERNAL", "The vault mutation could not be committed.");
        }
    }

    private async Task<string?> ReadCurrentAsync(string path, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        return await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePreconditions(
        VaultMutationPreconditions preconditions,
        string resourceKey,
        string? currentContent,
        CoordinationClaim? currentClaim,
        TimeProvider timeProvider,
        bool requireExists)
    {
        if (requireExists && currentContent is null)
        {
            throw new VaultMutationException(
                "NOT_FOUND", "The target resource does not exist.");
        }

        var actualRevision = currentContent is null ? null : VaultRevision.Compute(currentContent);
        if (!string.IsNullOrWhiteSpace(preconditions.ExpectedRevision) &&
            !string.Equals(preconditions.ExpectedRevision, actualRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(
                VaultMutationErrorCodes.WriteConflict,
                resourceKey,
                preconditions,
                currentContent,
                currentClaim,
                "Re-read the resource and retry with its current revision.");
        }

        if (!string.IsNullOrWhiteSpace(preconditions.ExpectedHash) &&
            !string.Equals(preconditions.ExpectedHash, actualRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict(
                VaultMutationErrorCodes.WriteConflict,
                resourceKey,
                preconditions,
                currentContent,
                currentClaim,
                "Re-read the resource and retry with its current hash.");
        }

        if (!preconditions.HasClaimPrecondition)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(preconditions.ClaimId) || !preconditions.FenceGeneration.HasValue)
        {
            throw new VaultMutationException(
                VaultMutationErrorCodes.InvalidPrecondition,
                "claim_id and fence_generation must be supplied together.");
        }

        if (currentClaim is null ||
            currentClaim.Status != CoordinationClaimStatuses.Active ||
            timeProvider.GetUtcNow() >= currentClaim.LeaseExpiresAt ||
            !string.Equals(currentClaim.ClaimId, preconditions.ClaimId, StringComparison.Ordinal) ||
            currentClaim.FenceGeneration != preconditions.FenceGeneration.Value)
        {
            throw Conflict(
                "STALE_FENCE",
                resourceKey,
                preconditions,
                currentContent,
                currentClaim,
                "Acquire or renew the current claim before retrying the mutation.");
        }
    }

    private string ResolveResourceKey(string path, VaultMutationPreconditions? preconditions)
    {
        var relative = RelativePath(path);
        var defaultKey = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? $"note:{relative}"
            : $"logical:vault/{relative}";
        if (string.IsNullOrWhiteSpace(preconditions?.ResourceKey))
        {
            return defaultKey;
        }

        var supplied = preconditions.ResourceKey.Trim().Replace('\\', '/');
        if (!string.Equals(supplied, defaultKey, StringComparison.Ordinal))
        {
            throw new VaultMutationException(
                VaultMutationErrorCodes.InvalidPrecondition,
                "resource_key does not match the target vault path.");
        }

        return supplied;
    }

    private static VaultMutationPreconditions NormalizePreconditions(VaultMutationPreconditions? preconditions)
    {
        preconditions ??= new VaultMutationPreconditions();
        ValidateToken(preconditions.ExpectedRevision, "expected_revision", 128);
        ValidateToken(preconditions.ExpectedHash, "expected_hash", 128);
        ValidateToken(preconditions.ClaimId, "claim_id", 128);
        ValidateToken(preconditions.MutationId, "mutation_id", 128);
        if (preconditions.FenceGeneration is < 1)
        {
            throw new VaultMutationException(
                VaultMutationErrorCodes.InvalidPrecondition,
                "fence_generation must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(preconditions.ExpectedRevision) &&
            !string.IsNullOrWhiteSpace(preconditions.ExpectedHash) &&
            !string.Equals(preconditions.ExpectedRevision, preconditions.ExpectedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultMutationException(
                VaultMutationErrorCodes.InvalidPrecondition,
                "expected_revision and expected_hash must match when both are supplied.");
        }

        return preconditions;
    }

    private static void ValidateToken(string? value, string name, int maxLength)
    {
        if (value is not { Length: > 0 } || value.Length <= maxLength)
        {
            return;
        }

        throw new VaultMutationException(
            VaultMutationErrorCodes.InvalidPrecondition,
            $"{name} exceeds the maximum supported length.");
    }

    private async Task<MutationRecord?> ReadMutationRecordAsync(
        string? mutationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mutationId))
        {
            return null;
        }

        var path = GetMutationRecordPath(mutationId);
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<MutationRecord>(json)
                ?? throw new VaultMutationException(
                    VaultMutationErrorCodes.MutationIdReused,
                    "The mutation id record is invalid and cannot be reused safely.");
        }
        catch (JsonException)
        {
            throw new VaultMutationException(
                VaultMutationErrorCodes.MutationIdReused,
                "The mutation id record is invalid and cannot be reused safely.");
        }
    }

    private async Task PersistMutationRecordAsync(
        string? mutationId,
        string fingerprint,
        VaultMutationReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mutationId))
        {
            return;
        }

        var record = new MutationRecord(
            fingerprint,
            receipt.ResourceKey,
            receipt.Path,
            receipt.Revision,
            receipt.AlreadyApplied);
        await fileSystem.WriteAtomicallyAsync(
                GetMutationRecordPath(mutationId),
                JsonSerializer.Serialize(record),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static VaultMutationReceipt ConfirmDuplicate(
        MutationRecord record,
        string fingerprint,
        string resourceKey,
        string path)
    {
        if (!string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new VaultMutationException(
                VaultMutationErrorCodes.MutationIdReused,
                "mutation_id was already used for a different mutation.");
        }

        return new VaultMutationReceipt(
            string.IsNullOrWhiteSpace(record.ResourceKey) ? resourceKey : record.ResourceKey,
            string.IsNullOrWhiteSpace(record.Path) ? path : record.Path,
            record.Revision,
            AlreadyApplied: true);
    }

    private string GetMutationRecordPath(string mutationId) =>
        paths.ResolveVaultWritePath(Path.Combine(
            MutationRecordRoot,
            $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mutationId)))}.json"));

    private static string[] BuildLockKeys(
        string? mutationId,
        params string[] resourceKeys)
    {
        if (string.IsNullOrWhiteSpace(mutationId))
        {
            return resourceKeys;
        }

        var mutationKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(mutationId)));
        return [.. resourceKeys, $"logical:mutation/{mutationKey}"];
    }

    private static string ComputeFingerprint(
        string operation,
        string resourceKey,
        string primaryPath,
        string? payload,
        VaultMutationPreconditions preconditions)
    {
        var input = string.Join(
            "\n",
            operation,
            resourceKey,
            primaryPath,
            payload ?? string.Empty,
            preconditions.ExpectedRevision ?? string.Empty,
            preconditions.ExpectedHash ?? string.Empty,
            preconditions.ClaimId ?? string.Empty,
            preconditions.FenceGeneration?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private string ResolveWritePath(string path)
    {
        try
        {
            return paths.ResolveVaultWritePath(path);
        }
        catch (VaultAccessDeniedException)
        {
            throw AccessDenied();
        }
    }

    private string ResolveDeletePath(string path)
    {
        try
        {
            return paths.ResolveVaultDeletePath(path);
        }
        catch (VaultAccessDeniedException)
        {
            throw AccessDenied();
        }
    }

    private string RelativePath(string path) =>
        Path.GetRelativePath(paths.VaultRoot, path).Replace('\\', '/');

    private async Task ReindexAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsMarkdown(path))
        {
            return;
        }

        try
        {
            await index.ReindexAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The committed file remains authoritative; the watcher can repair a transient index
            // failure. Precondition validation always completed before this point.
        }
    }

    private async Task MoveInIndexAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!IsMarkdown(source) && !IsMarkdown(destination))
        {
            return;
        }

        try
        {
            await index.MoveAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // See ReindexAsync: the file move has already committed atomically.
        }
    }

    private async Task DeleteFromIndexAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsMarkdown(path))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        index.Delete(path);
        await Task.CompletedTask;
    }

    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static VaultMutationException Conflict(
        string code,
        string resourceKey,
        VaultMutationPreconditions preconditions,
        string? currentContent,
        CoordinationClaim? currentClaim,
        string recoveryAction) =>
        new(
            VaultMutationErrorCodes.WriteConflict,
            "The resource changed or is not owned by the supplied mutation preconditions.",
            new VaultMutationConflict(
                code,
                resourceKey,
                preconditions.ExpectedRevision,
                currentContent is null ? null : VaultRevision.Compute(currentContent),
                preconditions.ExpectedHash,
                currentContent is null ? null : VaultRevision.Compute(currentContent),
                currentClaim?.FenceGeneration,
                recoveryAction));

    private static VaultMutationException AccessDenied() =>
        new("ACCESS_DENIED", "The requested filesystem operation is outside Kioku's configured security boundary.");

    private void RecordMutationFailure(string code)
    {
        metrics?.RecordCoordinationMutation(code);
        logger?.Warn("Vault mutation rejected. Code={Code}.", code);
    }

    private Task InjectAsync(
        CoordinationFaultPoint point,
        CancellationToken cancellationToken) =>
        (faultInjector ?? NoOpCoordinationFaultInjector.Instance).InjectAsync(point, cancellationToken);

    private sealed record MutationRecord(
        string Fingerprint,
        string ResourceKey,
        string Path,
        string? Revision,
        bool AlreadyApplied);
}
