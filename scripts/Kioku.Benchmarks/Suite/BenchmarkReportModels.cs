using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kioku.Benchmarks.Suite;

/// <summary>Everything needed to independently reproduce and sanity-check a run: hardware, OS,
/// runtime, embedding model, dataset shape, and the exact command that produced the report.</summary>
public sealed record ReportMetadata(
    DateTimeOffset TimestampUtc,
    HardwareSnapshot Hardware,
    string EmbeddingModel,
    string OllamaUrl,
    DatasetDescription Dataset,
    string ReproduceCommand,
    string EnvironmentCaveat);

public sealed record DatasetDescription(
    IReadOnlyList<int> IndexingSizesRun,
    string IndexingSizesSupportedNote,
    int SearchScenarioVaultSize,
    int SearchQueryCount,
    string EvalVaultPath,
    int EvalVaultNoteCount,
    string GoldenSetPath);

public sealed record BenchmarkReport(
    ReportMetadata Metadata,
    ColdStartBenchmark.Result ColdStart,
    IReadOnlyList<IndexingMemoryBenchmark.SizeResult> IndexingBySize,
    EmbeddingCacheAndSearchBenchmark.EmbeddingCacheResult EmbeddingCache,
    IReadOnlyList<EmbeddingCacheAndSearchBenchmark.ModeLatency> SearchLatencyByMode,
    EmbeddingCacheAndSearchBenchmark.UpdateToQueryableResult UpdateToQueryable,
    SchemaCostBenchmark.Result SchemaCost,
    RetrievalQualityBenchmark.Result RetrievalQuality);

public static class BenchmarkReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static async Task WriteAsync(BenchmarkReport report, string outputPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(report, Options);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }
}
