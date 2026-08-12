using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultReconciliationRetryTests
{
    /// <summary>
    /// Reconciliation does not pre-remove the note, so an unreadable file keeps its previously
    /// indexed entry. The failure must still be reported instead of being hidden by that stale
    /// entry, otherwise the bounded retry path never runs for already indexed notes.
    /// </summary>
    [Fact]
    public async Task Unreadable_file_reports_reconciliation_failure_despite_stale_index_entry()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var vaultPath = Path.Combine(
            Path.GetTempPath(),
            $"kioku-reconciliation-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultPath);
        var notePath = Path.Combine(vaultPath, "Locked.md");

        try
        {
            await File.WriteAllTextAsync(notePath, "# Locked\n\nv1.");

            var configuration = new KiokuConfiguration { VaultPath = vaultPath };
            using var vault = new VaultIndexService(
                NullLogger<VaultIndexService>.Instance,
                configuration,
                embedding: null);
            using var pipeline = new VaultIndexingPipeline(
                new VaultIndexOperations(vault),
                new VaultPathPolicy(configuration),
                new VaultConfigService(configuration, NullLogger<VaultConfigService>.Instance),
                Options.Create(new KiokuOptions { VaultPath = vaultPath, IndexConcurrency = 2 }),
                new HttpReadinessState(),
                TimeProvider.System,
                new VaultIndexingMetrics(),
                NullLogger<VaultIndexingPipeline>.Instance);

            await pipeline.InitializeAsync(CancellationToken.None);
            Assert.NotNull(vault.GetNote(notePath));
            Assert.Equal(0, pipeline.Metrics.FailedChanges);

            File.SetUnixFileMode(notePath, UnixFileMode.None);
            if (CanStillRead(notePath))
            {
                // Running as root: the permission bits cannot make the read fail.
                return;
            }

            await pipeline.ReconcileAsync("unreadable_file_test", CancellationToken.None);

            Assert.True(pipeline.Metrics.FailedChanges >= 1);
            Assert.NotNull(vault.GetNote(notePath));
        }
        finally
        {
            if (!OperatingSystem.IsWindows() && File.Exists(notePath))
            {
                File.SetUnixFileMode(
                    notePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            try
            {
                Directory.Delete(vaultPath, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort test cleanup only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup only.
            }
        }
    }

    private static bool CanStillRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
