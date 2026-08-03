using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

// Retrieval-quality evaluation runner: boots the real retrieval stack (index + Ollama
// embeddings + hybrid RRF) against a vault and a golden set, and prints Precision@k,
// Recall@k, MRR and NDCG@k per search mode as Markdown tables.
//
// Usage (from the repo root):
//   dotnet run --project scripts/Kioku.Eval -- [options]
//
// Options:
//   --vault <path>      Vault to index (default: src/Kioku.Mcp.Server.Tests/Fixtures/EvalVault)
//   --golden <file>     Golden set JSON (default: src/Kioku.Mcp.Server.Tests/Fixtures/golden-set.json)
//   --modes <list>      Comma-separated: keyword,semantic,hybrid (default: all)
//   --k <list>          Comma-separated cutoffs (default: 5,10)
//   --min-score <f>     Similarity threshold for the semantic mode (default: 0)
//   --label <name>      Label printed in the report header (e.g. "baseline-nomic")
//
// Semantic and hybrid modes require a running Ollama with the configured embedding model
// (KIOKU_OLLAMA_URL / KIOKU_EMBEDDING_MODEL); keyword mode works without it.

var options = EvalOptions.Parse(args);
if (options is null)
{
    return 1;
}

if (!Directory.Exists(options.VaultPath))
{
    Console.Error.WriteLine($"[error] Vault path not found: {options.VaultPath}");
    return 1;
}

if (!File.Exists(options.GoldenPath))
{
    Console.Error.WriteLine($"[error] Golden set file not found: {options.GoldenPath}");
    return 1;
}

var golden = GoldenSet.Load(options.GoldenPath);
var config = new KiokuConfiguration
{
    VaultPath = Path.GetFullPath(options.VaultPath),
    EmbeddingModel = Environment.GetEnvironmentVariable("KIOKU_EMBEDDING_MODEL") ?? "nomic-embed-text",
    OllamaUrl = Environment.GetEnvironmentVariable("KIOKU_OLLAMA_URL") ?? "http://localhost:11434",
    MaxSearchResults = Math.Max(options.Ks.Max(), 20),
};

using var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance, new SimpleHttpClientFactory());
using var vault = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config, embedding);
var hybrid = new HybridSearchService(vault, embedding);

Console.Error.WriteLine($"[loading] Indexing vault: {config.VaultPath}");
await vault.InitializeAsync();
Console.Error.WriteLine($"[ok] Indexed {vault.IndexedCount} notes.");

var semanticRequested = options.Modes.Any(m => m is "semantic" or "hybrid");
if (semanticRequested && !embedding.IsAvailable)
{
    Console.Error.WriteLine(
        $"[error] Ollama is not reachable at {config.OllamaUrl} — semantic/hybrid modes cannot run. " +
        "Start Ollama or use --modes keyword.");
    return 1;
}

if (embedding.IsAvailable)
{
    Console.Error.WriteLine($"[loading] Waiting for embeddings ({config.EmbeddingModel})...");
    while (embedding.CachedEmbeddingCount + embedding.FailedEmbeddingCount < vault.IndexedCount
        || embedding.EmbeddingBacklog > 0)
    {
        var eta = embedding.EstimatedTimeRemaining;
        Console.Error.WriteLine(
            $"[loading] {embedding.CachedEmbeddingCount}/{vault.IndexedCount} embedded" +
            (embedding.FailedEmbeddingCount > 0 ? $" ({embedding.FailedEmbeddingCount} failed)" : "") +
            (eta.HasValue && eta.Value > TimeSpan.Zero ? $" (ETA {eta.Value:mm\\:ss})" : ""));
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    Console.Error.WriteLine($"[ok] {embedding.CachedEmbeddingCount} embeddings ready.");
    if (embedding.FailedEmbeddingCount > 0)
    {
        Console.Error.WriteLine(
            $"[warn] {embedding.FailedEmbeddingCount} note(s) failed to embed (e.g. request timeout or " +
            "content exceeding the model's context window) and will be absent from semantic/hybrid results:");
        foreach (var path in embedding.FailedPaths)
        {
            Console.Error.WriteLine($"  - {path}");
        }
    }
}

var kMax = options.Ks.Max();
var scored = golden.Queries.Where(q => q.HasRelevantNotes).ToList();
var probes = golden.Queries.Where(q => !q.HasRelevantNotes).ToList();

Console.WriteLine($"# Kioku retrieval eval{(options.Label is null ? "" : $" — {options.Label}")}");
Console.WriteLine();
Console.WriteLine($"- Vault: `{config.VaultPath}` ({vault.IndexedCount} notes)");
Console.WriteLine($"- Golden set: `{options.GoldenPath}` ({scored.Count} scored queries, {probes.Count} no-answer probes)");
Console.WriteLine($"- Embedding model: `{config.EmbeddingModel}` (available: {embedding.IsAvailable})");
Console.WriteLine($"- min_score (semantic): {options.MinScore}");
Console.WriteLine($"- Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

foreach (var mode in options.Modes)
{
    Console.WriteLine();
    Console.WriteLine($"## {mode}");
    Console.WriteLine();
    Console.WriteLine("| k | Precision@k | Recall@k | MRR | NDCG@k |");
    Console.WriteLine("|---|-------------|----------|-----|--------|");

    var rankings = new Dictionary<string, IReadOnlyList<string>>();
    foreach (var query in golden.Queries)
    {
        rankings[query.Id] = await RankAsync(mode, query.Query, kMax);
    }

    foreach (var k in options.Ks)
    {
        double p = 0, r = 0, mrr = 0, ndcg = 0;
        foreach (var query in scored)
        {
            var ranked = rankings[query.Id];
            var judgments = query.RelevanceByPath();
            p += RetrievalMetrics.PrecisionAtK(ranked, judgments, k);
            r += RetrievalMetrics.RecallAtK(ranked, judgments, k);
            mrr += RetrievalMetrics.ReciprocalRank(ranked, judgments);
            ndcg += RetrievalMetrics.NdcgAtK(ranked, judgments, k);
        }

        Console.WriteLine(
            $"| {k} | {p / scored.Count:F3} | {r / scored.Count:F3} | {mrr / scored.Count:F3} | {ndcg / scored.Count:F3} |");
    }

    if (probes.Count > 0)
    {
        var avgProbeResults = probes.Average(q => rankings[q.Id].Count);
        Console.WriteLine();
        Console.WriteLine($"No-answer probes: avg {avgProbeResults:F1} results returned (lower is better).");
    }
}

return 0;

async Task<IReadOnlyList<string>> RankAsync(string mode, string query, int k)
{
    switch (mode)
    {
        case "keyword":
            return vault.Search(query, k).Select(res => res.Note.VaultRelativePath).ToList();

        case "semantic":
            {
                var vector = await embedding.EmbedAsync(query);
                if (vector is null)
                {
                    return [];
                }

                var notesByPath = vault.GetAllNotes().ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);
                return embedding
                    .SearchByVector(vector, k, string.Empty, notesByPath, options.MinScore)
                    .Select(res => res.Note.VaultRelativePath)
                    .ToList();
            }

        case "hybrid":
            {
                var vector = await embedding.EmbedAsync(query);
                return hybrid.Search(query, k, queryVector: vector)
                    .Select(res => res.Note.VaultRelativePath)
                    .ToList();
            }

        default:
            throw new ArgumentException($"Unknown mode: {mode}");
    }
}

internal sealed record EvalOptions(
    string VaultPath,
    string GoldenPath,
    IReadOnlyList<string> Modes,
    IReadOnlyList<int> Ks,
    float MinScore,
    string? Label)
{
    private static readonly string[] ValidModes = ["keyword", "semantic", "hybrid"];

    public static EvalOptions? Parse(string[] args)
    {
        var vault = "src/Kioku.Mcp.Server.Tests/Fixtures/EvalVault";
        var goldenPath = "src/Kioku.Mcp.Server.Tests/Fixtures/golden-set.json";
        var modes = ValidModes.ToList();
        var ks = new List<int> { 5, 10 };
        var minScore = 0f;
        string? label = null;

        for (int i = 0; i < args.Length; i++)
        {
            string Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {args[i]}");
                }

                return args[++i];
            }

            try
            {
                switch (args[i])
                {
                    case "--vault": vault = Next(); break;
                    case "--golden": goldenPath = Next(); break;
                    case "--modes":
                        modes = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(m => m.ToLowerInvariant()).ToList();
                        break;
                    case "--k":
                        ks = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(int.Parse).ToList();
                        break;
                    case "--min-score": minScore = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--label": label = Next(); break;
                    case "--help" or "-h":
                        Console.WriteLine("Usage: dotnet run --project scripts/Kioku.Eval -- [--vault <path>] [--golden <file>] " +
                                          "[--modes keyword,semantic,hybrid] [--k 5,10] [--min-score 0.4] [--label name]");
                        return null;
                    default:
                        Console.Error.WriteLine($"[error] Unknown argument: {args[i]} (use --help)");
                        return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return null;
            }
        }

        var invalid = modes.Where(m => !ValidModes.Contains(m)).ToList();
        if (invalid.Count > 0 || modes.Count == 0 || ks.Count == 0 || ks.Any(k => k <= 0))
        {
            Console.Error.WriteLine($"[error] Invalid --modes or --k. Valid modes: {string.Join(", ", ValidModes)}; k must be positive.");
            return null;
        }

        return new EvalOptions(vault, goldenPath, modes, ks, minScore, label);
    }
}
