using System.Diagnostics;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Runs a single "scenario vault" through the full stack — index, wait for embeddings, measure
/// the persisted cache and cache-reuse speedup, sample search latency across all three modes,
/// then write a new note into the still-watched vault and time how long until it is queryable.
/// Combined into one benchmark because all four measurements need the same indexed-and-embedded
/// vault; running them separately would mean re-embedding from scratch for each.
/// </summary>
public static class EmbeddingCacheAndSearchBenchmark
{
    public sealed record ModeLatency(
        string Mode,
        int SampleCount,
        double MeanMs,
        double P50Ms,
        double P95Ms,
        double MinMs,
        double MaxMs);

    public sealed record EmbeddingCacheResult(
        int NoteCount,
        bool OllamaAvailable,
        double FirstIndexMs,
        double FirstEmbeddingWaitMs,
        bool FirstEmbeddingBacklogCompleted,
        int EmbeddedCount,
        int FailedEmbeddedCount,
        long CacheFileBytes,
        double SecondIndexMs,
        double SecondEmbeddingWaitMs,
        bool SecondEmbeddingBacklogCompleted,
        double SpeedupFactor);

    public sealed record UpdateToQueryableResult(string UniqueTerm, double ElapsedMs, bool Found, double TimeoutMs);

    public sealed record Result(
        EmbeddingCacheResult EmbeddingCache,
        IReadOnlyList<ModeLatency> SearchLatencyByMode,
        UpdateToQueryableResult UpdateToQueryable);

    public static async Task<Result> RunAsync(
        string tempRoot,
        int noteCount,
        int searchQueryCount,
        string embeddingModel,
        string ollamaUrl,
        CancellationToken cancellationToken)
    {
        var vaultPath = Path.Combine(tempRoot, $"kioku-bench-scenario-{Guid.NewGuid():N}");
        Console.WriteLine($"[loading] Scenario vault: generating {noteCount} synthetic notes at {vaultPath}...");
        var vaultInfo = SyntheticVaultGenerator.Generate(vaultPath, noteCount);

        var config = new KiokuConfiguration
        {
            VaultPath = vaultPath,
            EmbeddingModel = embeddingModel,
            OllamaUrl = ollamaUrl,
            MaxSearchResults = 20,
        };

        var cacheResult = await RunEmbeddingCacheAsync(config, vaultInfo.NoteCount, cancellationToken);

        IReadOnlyList<ModeLatency> searchLatency;
        UpdateToQueryableResult updateResult;
        {
            using var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance, new SimpleHttpClientFactory());
            using var vault = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config, embedding);
            await vault.InitializeAsync(cancellationToken);
            if (embedding.IsAvailable)
            {
                await embedding.WaitForInitialBacklogAsync(TimeSpan.FromMinutes(5), cancellationToken);
            }

            var hybrid = new HybridSearchService(vault, embedding);
            var queries = GenerateQueries(vaultInfo, searchQueryCount);
            searchLatency = await MeasureSearchLatencyAsync(vault, embedding, hybrid, queries, cancellationToken);
            updateResult = await MeasureUpdateToQueryableAsync(vault, vaultPath, cancellationToken);
            // vault/embedding (and the vault's FileSystemWatcher) are disposed here, before the
            // directory they watch is deleted below.
        }

        try
        {
            Directory.Delete(vaultPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        return new Result(cacheResult, searchLatency, updateResult);
    }

    private static async Task<EmbeddingCacheResult> RunEmbeddingCacheAsync(
        KiokuConfiguration config, int noteCount, CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(config.VaultPath, ".kioku", "embeddings.bin");

        Console.WriteLine("[loading] First index + embed pass...");
        double firstIndexMs, firstEmbedMs;
        bool firstBacklogCompleted;
        int embeddedCount, failedCount;
        bool ollamaAvailable;
        {
            using var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance, new SimpleHttpClientFactory());
            using var vault = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config, embedding);

            var sw = Stopwatch.StartNew();
            await vault.InitializeAsync(cancellationToken);
            firstIndexMs = sw.Elapsed.TotalMilliseconds;

            ollamaAvailable = embedding.IsAvailable;
            var embedSw = Stopwatch.StartNew();
            firstBacklogCompleted = !ollamaAvailable
                || await embedding.WaitForInitialBacklogAsync(TimeSpan.FromMinutes(5), cancellationToken);
            firstEmbedMs = embedSw.Elapsed.TotalMilliseconds;

            await embedding.SaveAsync(cancellationToken);
            embeddedCount = embedding.CachedEmbeddingCount;
            failedCount = embedding.FailedEmbeddingCount;
        }

        var cacheBytes = File.Exists(cachePath) ? new FileInfo(cachePath).Length : 0L;
        Console.WriteLine(
            $"[ok] First pass: {embeddedCount} embedded ({failedCount} failed), " +
            $"cache = {cacheBytes / 1024.0:F1} KB.");

        Console.WriteLine("[loading] Second index + embed pass against the SAME unchanged vault (cache reuse)...");
        double secondIndexMs, secondEmbedMs;
        bool secondBacklogCompleted;
        {
            using var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance, new SimpleHttpClientFactory());
            using var vault = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config, embedding);

            var sw = Stopwatch.StartNew();
            await vault.InitializeAsync(cancellationToken);
            secondIndexMs = sw.Elapsed.TotalMilliseconds;

            var embedSw = Stopwatch.StartNew();
            secondBacklogCompleted = !ollamaAvailable
                || await embedding.WaitForInitialBacklogAsync(TimeSpan.FromMinutes(5), cancellationToken);
            secondEmbedMs = embedSw.Elapsed.TotalMilliseconds;
        }

        var firstTotal = firstIndexMs + firstEmbedMs;
        var secondTotal = secondIndexMs + secondEmbedMs;
        var speedup = secondTotal > 0 ? firstTotal / secondTotal : 0;
        Console.WriteLine(
            $"[ok] Second pass total {secondTotal:F1} ms vs first pass {firstTotal:F1} ms " +
            $"({speedup:F1}x faster; cache hit avoids re-embedding).");

        return new EmbeddingCacheResult(
            NoteCount: noteCount,
            OllamaAvailable: ollamaAvailable,
            FirstIndexMs: firstIndexMs,
            FirstEmbeddingWaitMs: firstEmbedMs,
            FirstEmbeddingBacklogCompleted: firstBacklogCompleted,
            EmbeddedCount: embeddedCount,
            FailedEmbeddedCount: failedCount,
            CacheFileBytes: cacheBytes,
            SecondIndexMs: secondIndexMs,
            SecondEmbeddingWaitMs: secondEmbedMs,
            SecondEmbeddingBacklogCompleted: secondBacklogCompleted,
            SpeedupFactor: speedup);
    }

    private static async Task<IReadOnlyList<ModeLatency>> MeasureSearchLatencyAsync(
        VaultIndexService vault,
        EmbeddingService embedding,
        HybridSearchService hybrid,
        List<string> queries,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[loading] Search latency: {queries.Count} queries x keyword/semantic/hybrid...");
        var keywordMs = new List<double>(queries.Count);
        var semanticMs = new List<double>(queries.Count);
        var hybridMs = new List<double>(queries.Count);

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            _ = vault.Search(query, 10).ToList();
            keywordMs.Add(sw.Elapsed.TotalMilliseconds);

            if (!embedding.IsAvailable)
            {
                continue;
            }

            var semanticSw = Stopwatch.StartNew();
            var vector = await embedding.EmbedQueryAsync(query, cancellationToken);
            if (vector is not null)
            {
                var notesByPath = vault.GetAllNotes().ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);
                _ = embedding.SearchByVector(vector, 10, string.Empty, notesByPath).ToList();
            }

            semanticMs.Add(semanticSw.Elapsed.TotalMilliseconds);

            var hybridSw = Stopwatch.StartNew();
            var hybridVector = await embedding.EmbedQueryAsync(query, cancellationToken);
            _ = hybrid.Search(query, 10, queryVector: hybridVector).ToList();
            hybridMs.Add(hybridSw.Elapsed.TotalMilliseconds);
        }

        var results = new List<ModeLatency> { Summarize("keyword", keywordMs) };
        if (semanticMs.Count > 0)
        {
            results.Add(Summarize("semantic", semanticMs));
        }

        if (hybridMs.Count > 0)
        {
            results.Add(Summarize("hybrid", hybridMs));
        }

        return results;
    }

    private static ModeLatency Summarize(string mode, List<double> samples)
    {
        var sorted = samples.OrderBy(v => v).ToList();
        return new ModeLatency(
            mode,
            sorted.Count,
            sorted.Average(),
            Stats.Percentile(sorted, 0.5),
            Stats.Percentile(sorted, 0.95),
            sorted.Min(),
            sorted.Max());
    }

    private static async Task<UpdateToQueryableResult> MeasureUpdateToQueryableAsync(
        VaultIndexService vault, string vaultPath, CancellationToken cancellationToken)
    {
        var uniqueTerm = $"zzupdateprobe{Guid.NewGuid():N}";
        var notePath = Path.Combine(vaultPath, "update-probe-note.md");
        var content = $"# Update probe\n\nThis note was written after initial indexing to measure " +
            $"update-to-queryable latency. Unique marker: {uniqueTerm}\n";

        Console.WriteLine("[loading] Update-to-queryable: writing a new note and polling search...");
        var timeout = TimeSpan.FromSeconds(15);
        var sw = Stopwatch.StartNew();
        await File.WriteAllTextAsync(notePath, content, cancellationToken);

        var found = false;
        while (sw.Elapsed < timeout)
        {
            if (vault.Search(uniqueTerm, 5).Any())
            {
                found = true;
                break;
            }

            await Task.Delay(50, cancellationToken);
        }

        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine(found
            ? $"[ok] Update queryable after {elapsedMs:F0} ms."
            : $"[warn] Update not queryable within {timeout.TotalSeconds:F0}s timeout.");

        return new UpdateToQueryableResult(uniqueTerm, elapsedMs, found, timeout.TotalMilliseconds);
    }

    private static List<string> GenerateQueries(SyntheticVaultGenerator.VaultInfo vaultInfo, int count)
    {
        var random = new Random(1234);
        var queries = new List<string>(vaultInfo.Topics);
        while (queries.Count < count)
        {
            var topic = vaultInfo.Topics[random.Next(vaultInfo.Topics.Count)];
            var tag = vaultInfo.Tags[random.Next(vaultInfo.Tags.Count)];
            queries.Add($"{topic} {tag}");
        }

        return queries.Take(count).ToList();
    }
}
