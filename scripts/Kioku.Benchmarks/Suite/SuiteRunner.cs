using System.Text.Json;

namespace Kioku.Benchmarks.Suite;

/// <summary>Orchestrates every benchmark section in sequence and writes the combined JSON report.</summary>
public static class SuiteRunner
{
    public static async Task<int> RunAsync(string[] suiteArgs, CancellationToken cancellationToken)
    {
        SuiteOptions options;
        try
        {
            options = SuiteOptions.Parse(suiteArgs);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[error] {ex.Message}");
            return 1;
        }

        var repoRoot = Directory.GetCurrentDirectory();
        var evalVaultPath = Path.GetFullPath(options.EvalVaultPath, repoRoot);
        var goldenSetPath = Path.GetFullPath(options.GoldenSetPath, repoRoot);
        var serverProjectPath = Path.GetFullPath(options.ServerProjectPath, repoRoot);
        var evalProjectPath = Path.GetFullPath(options.EvalProjectPath, repoRoot);
        var outputPath = Path.GetFullPath(options.OutputPath, repoRoot);
        var tempRoot = Path.GetTempPath();

        var ollamaUrl = Environment.GetEnvironmentVariable("KIOKU_OLLAMA_URL") ?? "http://localhost:11434";
        var embeddingModel = Environment.GetEnvironmentVariable("KIOKU_EMBEDDING_MODEL") ?? "nomic-embed-text";

        Console.WriteLine("=== Kioku benchmark suite ===");
        Console.WriteLine($"[info] Indexing sizes: {string.Join(", ", options.IndexingSizes)}");
        Console.WriteLine($"[info] Search scenario vault size: {options.SearchScenarioVaultSize}");
        Console.WriteLine($"[info] Ollama: {ollamaUrl} (model {embeddingModel})");
        Console.WriteLine($"[info] Output: {outputPath}");

        await ServerProcessHelper.EnsureServerBuiltAsync(serverProjectPath, cancellationToken);
        var serverDllPath = ServerProcessHelper.ResolveServerDll(repoRoot);

        Console.WriteLine();
        Console.WriteLine("--- 1/6 Cold start ---");
        var coldStartVaultPath = Path.Combine(tempRoot, $"kioku-bench-coldstart-{Guid.NewGuid():N}");
        var coldStart = await ColdStartBenchmark.RunAsync(
            serverDllPath, coldStartVaultPath, options.ColdStartRuns, ollamaUrl, cancellationToken);
        TryDelete(coldStartVaultPath);

        Console.WriteLine();
        Console.WriteLine("--- 2/6 Indexing time + memory by vault size ---");
        var indexingResults = await IndexingMemoryBenchmark.RunAsync(options.IndexingSizes, tempRoot, cancellationToken);

        Console.WriteLine();
        Console.WriteLine("--- 3/6 Embedding cache, search latency, update-to-queryable ---");
        var scenario = await EmbeddingCacheAndSearchBenchmark.RunAsync(
            tempRoot, options.SearchScenarioVaultSize, options.SearchQueryCount,
            embeddingModel, ollamaUrl, cancellationToken);

        Console.WriteLine();
        Console.WriteLine("--- 4/6 Schema-token cost by capability profile ---");
        var allCapabilities = ReadAllCapabilities(repoRoot);
        var schemaCost = await SchemaCostBenchmark.RunAsync(serverDllPath, tempRoot, allCapabilities, cancellationToken);

        Console.WriteLine();
        Console.WriteLine("--- 5/6 Retrieval quality (Recall@K/MRR/NDCG@K via Kioku.Eval) ---");
        var retrieval = await RetrievalQualityBenchmark.RunAsync(
            repoRoot, evalProjectPath, evalVaultPath, goldenSetPath, options.Label, cancellationToken);

        Console.WriteLine();
        Console.WriteLine("--- 6/6 Writing report ---");
        var evalVaultNoteCount = Directory.Exists(evalVaultPath)
            ? Directory.EnumerateFiles(evalVaultPath, "*.md", SearchOption.AllDirectories).Count()
            : 0;

        var reproduceCommand =
            "dotnet run --project scripts/Kioku.Benchmarks -c Release -- suite " +
            $"--sizes {string.Join(',', options.IndexingSizes)} " +
            $"--search-vault-size {options.SearchScenarioVaultSize} " +
            $"--search-queries {options.SearchQueryCount} " +
            $"--cold-start-runs {options.ColdStartRuns} " +
            $"--label {options.Label}";

        var metadata = new ReportMetadata(
            DateTimeOffset.UtcNow,
            HardwareSnapshot.Capture(),
            embeddingModel,
            ollamaUrl,
            new DatasetDescription(
                options.IndexingSizes,
                "The tool accepts any --sizes list (e.g. 10000,50000); only the sizes above were " +
                "actually executed in this run — larger sizes are supported but not run here.",
                options.SearchScenarioVaultSize,
                options.SearchQueryCount,
                Path.GetRelativePath(repoRoot, evalVaultPath),
                evalVaultNoteCount,
                Path.GetRelativePath(repoRoot, goldenSetPath)),
            reproduceCommand,
            "Single-machine, single-session run in a development sandbox, not a controlled or " +
            "isolated benchmark environment. Other processes may share CPU/IO with this run; " +
            "absolute numbers should be read as one data point, not a guaranteed SLA.");

        var report = new BenchmarkReport(
            metadata, coldStart, indexingResults, scenario.EmbeddingCache,
            scenario.SearchLatencyByMode, scenario.UpdateToQueryable, schemaCost, retrieval);

        await BenchmarkReportWriter.WriteAsync(report, outputPath, cancellationToken);
        Console.WriteLine($"[ok] Report written to {Path.GetRelativePath(repoRoot, outputPath)}");
        return 0;
    }

    private static List<string> ReadAllCapabilities(string repoRoot)
    {
        var metadataPath = Path.Combine(repoRoot, "docs", "public-metadata.json");
        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var capabilities = document.RootElement.GetProperty("capabilities");
        var result = new List<string>();
        foreach (var name in new[] { "enabledByDefault", "disabledByDefault" })
        {
            foreach (var element in capabilities.GetProperty(name).EnumerateArray())
            {
                var value = element.GetString();
                if (value is not null)
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
