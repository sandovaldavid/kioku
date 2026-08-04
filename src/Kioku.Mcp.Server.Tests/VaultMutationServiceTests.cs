using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultMutationServiceTests : IDisposable
{
    private readonly string _vaultPath = Path.Combine(
        Path.GetTempPath(),
        $"kioku-mutations-{Guid.NewGuid():N}");

    private readonly VaultMutationService _mutations;

    public VaultMutationServiceTests()
    {
        Directory.CreateDirectory(_vaultPath);
        var config = new KiokuConfiguration { VaultPath = _vaultPath };
        var paths = new VaultPathPolicy(config);
        var fileSystem = new CoordinationFileSystem();
        var timeProvider = TimeProvider.System;
        var validator = new CoordinationContractValidator();
        var events = new CoordinationEventStore(paths, fileSystem, validator, timeProvider);
        var claims = new CoordinationClaimStore(paths, fileSystem, events, validator, timeProvider);
        _mutations = new VaultMutationService(
            paths,
            fileSystem,
            claims,
            new FakeIndexOperations(),
            timeProvider);
    }

    [Fact]
    public async Task CompetingExpectedRevisionsAllowExactlyOneWrite()
    {
        var path = Path.Combine(_vaultPath, "Notes", "Plan.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "initial");
        var expected = VaultRevision.Compute("initial");
        var first = new VaultMutationPreconditions { ExpectedRevision = expected };
        var second = new VaultMutationPreconditions { ExpectedRevision = expected };

        var outcomes = await Task.WhenAll(
            TryWriteAsync(path, "first", first),
            TryWriteAsync(path, "second", second));

        Assert.Single(outcomes, outcome => outcome.Receipt is not null);
        var conflict = Assert.Single(outcomes, outcome => outcome.Error is not null).Error!;
        Assert.Equal(VaultMutationErrorCodes.WriteConflict, conflict.Conflict!.Code);
        Assert.Equal(expected, conflict.Conflict.ExpectedRevision);
        Assert.Equal(VaultRevision.Compute(await File.ReadAllTextAsync(path)), conflict.Conflict.ActualRevision);
    }

    [Fact]
    public async Task ExternalEditInvalidatesAnOldRevisionWithoutOverwritingIt()
    {
        var path = Path.Combine(_vaultPath, "Notes", "External.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "server version");
        var expected = VaultRevision.Compute("server version");
        await File.WriteAllTextAsync(path, "Obsidian edit");

        var exception = await Assert.ThrowsAsync<VaultMutationException>(() =>
            _mutations.WriteTextAsync(
                path,
                "stale server write",
                new VaultMutationPreconditions { ExpectedRevision = expected }));

        Assert.Equal(VaultMutationErrorCodes.WriteConflict, exception.Conflict!.Code);
        Assert.Equal("Obsidian edit", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task UpsertTextAsync_CreatesMissingTargetAndUpdatesWithCas()
    {
        var path = Path.Combine(_vaultPath, "Notes", "Generated.md");

        var created = await _mutations.UpsertTextAsync(path, "initial");
        var updated = await _mutations.UpsertTextAsync(
            path,
            "updated",
            new VaultMutationPreconditions { ExpectedRevision = created.Revision });

        Assert.Equal(VaultRevision.Compute("updated"), updated.Revision);
        Assert.Equal("updated", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteTextAsync_StillRequiresAnExistingTarget()
    {
        var path = Path.Combine(_vaultPath, "Notes", "Missing.md");

        var exception = await Assert.ThrowsAsync<VaultMutationException>(() =>
            _mutations.WriteTextAsync(path, "must not create"));

        Assert.Equal("NOT_FOUND", exception.Code);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(-2147024864, true)]
    [InlineData(-2147024863, true)]
    [InlineData(-2147024891, false)]
    public void SharingViolationClassifier_DistinguishesWindowsFilesystemErrors(
        int hResult,
        bool expected)
    {
        Assert.Equal(expected, VaultMutationService.IsSharingViolation(new TestIOException(hResult)));
    }

    [Fact]
    public async Task FencingPreconditionWithoutCurrentClaimFailsBeforeWriting()
    {
        var path = Path.Combine(_vaultPath, "Notes", "Claimed.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "initial");
        var expected = VaultRevision.Compute("initial");

        var exception = await Assert.ThrowsAsync<VaultMutationException>(() =>
            _mutations.WriteTextAsync(
                path,
                "must not write",
                new VaultMutationPreconditions
                {
                    ExpectedRevision = expected,
                    ClaimId = "claim-01",
                    FenceGeneration = 1,
                }));

        Assert.Equal("STALE_FENCE", exception.Conflict!.Code);
        Assert.Equal("initial", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task MutationIdRetryReturnsTheCommittedRevision()
    {
        var path = Path.Combine(_vaultPath, "Notes", "Retry.md");
        var preconditions = new VaultMutationPreconditions { MutationId = "mutation-01" };

        var first = await _mutations.CreateTextAsync(path, "created", preconditions);
        var duplicate = await _mutations.CreateTextAsync(path, "created", preconditions);

        Assert.False(first.AlreadyApplied);
        Assert.True(duplicate.AlreadyApplied);
        Assert.Equal(first.Revision, duplicate.Revision);
        Assert.Equal("created", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ReusingMutationIdForAnotherTargetIsRejected()
    {
        var firstPath = Path.Combine(_vaultPath, "Notes", "First.md");
        var secondPath = Path.Combine(_vaultPath, "Notes", "Second.md");
        var preconditions = new VaultMutationPreconditions { MutationId = "mutation-global-01" };

        var first = await _mutations.CreateTextAsync(firstPath, "created", preconditions);
        var exception = await Assert.ThrowsAsync<VaultMutationException>(() =>
            _mutations.CreateTextAsync(secondPath, "different", preconditions));

        Assert.Equal(VaultMutationErrorCodes.MutationIdReused, exception.Code);
        Assert.Equal("Notes/First.md", first.Path);
        Assert.False(File.Exists(secondPath));
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

    private async Task<(VaultMutationReceipt? Receipt, VaultMutationException? Error)> TryWriteAsync(
        string path,
        string content,
        VaultMutationPreconditions preconditions)
    {
        try
        {
            return (await _mutations.WriteTextAsync(path, content, preconditions), null);
        }
        catch (VaultMutationException exception)
        {
            return (null, exception);
        }
    }

    private sealed class FakeIndexOperations : IVaultIndexOperations
    {
        public IReadOnlyCollection<Note> GetNotesSnapshot() => [];

        public void SetReady(bool ready)
        {
        }

        public Task ReindexAsync(string filePath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MoveAsync(string oldPath, string newPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Delete(string filePath)
        {
        }
    }

    private sealed class TestIOException : IOException
    {
        public TestIOException(int hResult)
        {
            HResult = hResult;
        }
    }
}
