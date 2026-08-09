using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class EngineeringSpecReliabilityTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ConcurrentCreate_SameSpec_AllowsExactlyOneMutation()
    {
        var mutations = new AtomicCreateMutationService(_fixture.VaultPath, synchronizeCreates: true);
        var (service, workspace) = CreateService(mutations);

        var first = Task.Run(() => service.CreateSpecAsync(
            "demo", "Concurrent", "objective", "requirements"));
        var second = Task.Run(() => service.CreateSpecAsync(
            "demo", "Concurrent", "objective", "requirements"));

        var outcomes = new List<string>();
        var failures = new List<VaultMutationException>();
        foreach (var task in new[] { first, second })
        {
            try
            {
                outcomes.Add(await task);
            }
            catch (VaultMutationException exception)
            {
                failures.Add(exception);
            }
        }

        Assert.Single(outcomes);
        Assert.StartsWith("[ok]", outcomes[0]);
        Assert.Single(failures);
        Assert.Equal(VaultMutationErrorCodes.WriteConflict, failures[0].Code);
        Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md"));
    }

    [Fact]
    public async Task WriteFailure_IsPropagated_AndDoesNotReportSuccess()
    {
        var mutations = new FailingCreateMutationService();
        var (service, workspace) = CreateService(mutations);

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            service.CreateSpecAsync("demo", "Failure", "objective", "requirements"));

        Assert.Contains("simulated write failure", exception.Message);
        Assert.Empty(Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md"));
    }

    [Fact]
    public async Task SpecsContext_LimitIsDeterministic_WithoutChangingTotalCount()
    {
        var mutations = new AtomicCreateMutationService(_fixture.VaultPath);
        var (service, _) = CreateService(mutations);

        await service.CreateSpecAsync("demo", "Zulu", "zulu objective", "requirements", status: "approved");
        await service.CreateSpecAsync("demo", "Alpha", "alpha objective", "requirements", status: "approved");

        var first = await service.BuildSpecsSectionAsync("demo", includeContent: false, limit: 1);
        var second = await service.BuildSpecsSectionAsync("demo", includeContent: false, limit: 1);

        Assert.Equal(first, second);
        Assert.Contains("## Specs (2)", first);
        Assert.Equal(1, first.Split('\n').Count(line => line.StartsWith("- [", StringComparison.Ordinal)));
    }

    private (EngineeringSpecService Service, ProjectWorkspaceService Workspace) CreateService(
        IVaultMutationService mutations)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge, mutations);
        var service = new EngineeringSpecService(workspace, vaultConfig, _fixture.Index, bridge, mutations);
        return (service, workspace);
    }

    private sealed class AtomicCreateMutationService(
        string vaultPath,
        bool synchronizeCreates = false) : IVaultMutationService
    {
        private readonly object _gate = new();
        private readonly Barrier? _createBarrier = synchronizeCreates ? new Barrier(2) : null;

        public Task<VaultMutationReceipt> CreateTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(path).StartsWith("SPEC-", StringComparison.Ordinal))
            {
                _createBarrier?.SignalAndWait(cancellationToken);
            }

            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream, NoteHelpers.Utf8NoBom);
                    writer.Write(content);
                }
                catch (IOException)
                {
                    throw new VaultMutationException(
                        VaultMutationErrorCodes.WriteConflict,
                        "Concurrent create lost the existing-file race.");
                }

                var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
                return Task.FromResult(new VaultMutationReceipt(
                    $"note:{relative}",
                    relative,
                    VaultRevision.Compute(content)));
            }
        }

        public async Task<VaultMutationReceipt> WriteTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(path, content, NoteHelpers.Utf8NoBom, cancellationToken);
            var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
            return new VaultMutationReceipt($"note:{relative}", relative, VaultRevision.Compute(content));
        }

        public Task<VaultMutationReceipt> UpsertTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            File.Exists(path)
                ? WriteTextAsync(path, content, preconditions, cancellationToken)
                : CreateTextAsync(path, content, preconditions, cancellationToken);

        public Task<VaultMutationReceipt> DeleteAsync(
            string path,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            File.Delete(path);
            var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
            return Task.FromResult(new VaultMutationReceipt($"note:{relative}", relative, null));
        }

        public async Task<VaultMutationReceipt> MoveAsync(
            string sourcePath,
            string destinationPath,
            string? replacementContent = null,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(sourcePath, destinationPath);
            if (replacementContent is not null)
            {
                await File.WriteAllTextAsync(destinationPath, replacementContent, NoteHelpers.Utf8NoBom, cancellationToken);
            }

            var content = await File.ReadAllTextAsync(destinationPath, cancellationToken);
            var relative = Path.GetRelativePath(vaultPath, destinationPath).Replace('\\', '/');
            return new VaultMutationReceipt($"note:{relative}", relative, VaultRevision.Compute(content));
        }
    }

    private sealed class FailingCreateMutationService : IVaultMutationService
    {
        public Task<VaultMutationReceipt> CreateTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            throw new IOException("simulated write failure");

        public Task<VaultMutationReceipt> WriteTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<VaultMutationReceipt> UpsertTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<VaultMutationReceipt> DeleteAsync(
            string path,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<VaultMutationReceipt> MoveAsync(
            string sourcePath,
            string destinationPath,
            string? replacementContent = null,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
