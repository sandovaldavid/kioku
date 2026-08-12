using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultReconciliationEmbeddingTests
{
    [Fact]
    public async Task Full_reconciliation_preserves_unchanged_embeddings_and_rebuilds_backlinks()
    {
        var vaultPath = Path.Combine(
            Path.GetTempPath(),
            $"kioku-reconciliation-embedding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultPath);

        try
        {
            var targetPath = Path.Combine(vaultPath, "Target.md");
            await File.WriteAllTextAsync(targetPath, "# Target\n\nCanonical target.");

            const int sourceCount = 12;
            var firstSourcePath = string.Empty;
            for (var index = 0; index < sourceCount; index++)
            {
                var sourcePath = Path.Combine(vaultPath, $"Source-{index:D2}.md");
                if (index == 0)
                {
                    firstSourcePath = sourcePath;
                }

                await File.WriteAllTextAsync(
                    sourcePath,
                    $"# Source {index}\n\nLinks to [[Target]].\n\nStable content {index}.");
            }

            var embedCalls = 0;
            var configuration = new KiokuConfiguration
            {
                VaultPath = vaultPath,
                EmbeddingModel = "nomic-embed-text",
            };
            using var embedding = new EmbeddingService(
                configuration,
                NullLogger<EmbeddingService>.Instance,
                new FakeHttpClientFactory(
                    new FakeHttpMessageHandler(
                        DeterministicEmbedding.Responder(_ => Interlocked.Increment(ref embedCalls)))));
            using var vault = new VaultIndexService(
                NullLogger<VaultIndexService>.Instance,
                configuration,
                embedding);
            using var pipeline = new VaultIndexingPipeline(
                new VaultIndexOperations(vault),
                new VaultPathPolicy(configuration),
                new VaultConfigService(
                    configuration,
                    NullLogger<VaultConfigService>.Instance),
                Options.Create(new KiokuOptions
                {
                    VaultPath = vaultPath,
                    IndexConcurrency = 4,
                }),
                new HttpReadinessState(),
                TimeProvider.System,
                new VaultIndexingMetrics(),
                NullLogger<VaultIndexingPipeline>.Instance);

            await pipeline.InitializeAsync(CancellationToken.None);
            Assert.Equal(sourceCount, vault.GetBacklinks("Target").Count);

            await embedding.InitializeAsync(vault.GetAllNotes(), CancellationToken.None);
            Assert.True(await embedding.WaitForInitialBacklogAsync(TimeSpan.FromSeconds(10)));
            var callsAfterWarmup = Volatile.Read(ref embedCalls);
            Assert.True(callsAfterWarmup > 0);

            await pipeline.ReconcileAsync("unchanged_embedding_test", CancellationToken.None);

            Assert.Equal(callsAfterWarmup, Volatile.Read(ref embedCalls));
            Assert.Equal(sourceCount, vault.GetBacklinks("Target").Count);

            await File.AppendAllTextAsync(firstSourcePath, "\n\nChanged after warm-up.");
            await pipeline.ReconcileAsync("changed_embedding_test", CancellationToken.None);

            Assert.True(Volatile.Read(ref embedCalls) > callsAfterWarmup);
            Assert.Equal(sourceCount, vault.GetBacklinks("Target").Count);
        }
        finally
        {
            try
            {
                Directory.Delete(vaultPath, recursive: true);
            }
            catch (IOException)
            {
                // FileSystemWatcher handles can be released shortly after disposal on Windows.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup only.
            }
        }
    }
}
