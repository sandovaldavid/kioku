using System.Diagnostics;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Times VaultIndexService.InitializeAsync() (keyword/metadata indexing only, no embedding
/// service attached — embedding runs in the background and is measured separately by
/// EmbeddingCacheAndSearchBenchmark) against synthetic vaults of increasing size, and captures
/// managed-heap and working-set memory before/after each run.
/// </summary>
public static class IndexingMemoryBenchmark
{
    public sealed record SizeResult(
        int RequestedSize,
        int IndexedCount,
        double GenerateVaultMs,
        double InitializeMs,
        long ManagedHeapBeforeBytes,
        long ManagedHeapAfterBytes,
        long ManagedHeapDeltaBytes,
        long WorkingSetBeforeBytes,
        long WorkingSetAfterBytes,
        long WorkingSetDeltaBytes);

    public static async Task<IReadOnlyList<SizeResult>> RunAsync(
        IReadOnlyList<int> sizes, string tempRoot, CancellationToken cancellationToken)
    {
        var results = new List<SizeResult>();
        foreach (var size in sizes)
        {
            Console.WriteLine($"[loading] Indexing benchmark: generating {size} synthetic notes...");
            var vaultPath = Path.Combine(tempRoot, $"kioku-bench-index-{size}-{Guid.NewGuid():N}");

            var genStopwatch = Stopwatch.StartNew();
            SyntheticVaultGenerator.Generate(vaultPath, size);
            var generateMs = genStopwatch.Elapsed.TotalMilliseconds;

            var config = new KiokuConfiguration { VaultPath = vaultPath };

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var workingSetBefore = process.WorkingSet64;

            using var vault = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config);
            var stopwatch = Stopwatch.StartNew();
            await vault.InitializeAsync(cancellationToken);
            var initializeMs = stopwatch.Elapsed.TotalMilliseconds;

            var heapAfter = GC.GetTotalMemory(forceFullCollection: true);
            process.Refresh();
            var workingSetAfter = process.WorkingSet64;

            Console.WriteLine(
                $"[ok] {vault.IndexedCount} notes indexed in {initializeMs:F1} ms " +
                $"(heap delta {(heapAfter - heapBefore) / 1024.0 / 1024.0:F1} MB).");

            results.Add(new SizeResult(
                size,
                vault.IndexedCount,
                generateMs,
                initializeMs,
                heapBefore,
                heapAfter,
                heapAfter - heapBefore,
                workingSetBefore,
                workingSetAfter,
                workingSetAfter - workingSetBefore));

            try
            {
                Directory.Delete(vaultPath, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp vaults do not affect subsequent runs.
            }
        }

        return results;
    }
}
