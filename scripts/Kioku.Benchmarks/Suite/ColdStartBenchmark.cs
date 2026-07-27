using System.Diagnostics;
using ModelContextProtocol.Client;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Measures wall-clock cold-start time of the real Kioku MCP server process: from spawning
/// `dotnet Kioku.Mcp.Server.dll` over stdio to a completed MCP initialize handshake, and
/// separately, the first `tools/list` round-trip. Runs against a minimal single-note vault so
/// the number reflects server startup cost, not indexing a large vault (that is measured
/// separately by IndexingMemoryBenchmark).
/// </summary>
public static class ColdStartBenchmark
{
    public sealed record RunResult(double InitializeMs, double FirstToolsListMs, int ToolCount);

    public sealed record Result(
        int Runs,
        string? OllamaUrl,
        IReadOnlyList<RunResult> Samples,
        double InitializeMeanMs,
        double InitializeMedianMs,
        double InitializeMinMs,
        double InitializeMaxMs,
        double FirstToolsListMeanMs);

    public static async Task<Result> RunAsync(
        string serverDllPath,
        string minimalVaultPath,
        int runs,
        string? ollamaUrl,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(minimalVaultPath);
        var seedPath = Path.Combine(minimalVaultPath, "seed.md");
        if (!File.Exists(seedPath))
        {
            await File.WriteAllTextAsync(seedPath, "# Cold-start probe\n\nMinimal seed note.\n", cancellationToken);
        }

        var samples = new List<RunResult>(runs);
        for (var i = 0; i < runs; i++)
        {
            Console.WriteLine($"[loading] Cold-start run {i + 1}/{runs}...");
            var transport = ServerProcessHelper.CreateTransport(
                serverDllPath, minimalVaultPath, $"kioku-benchmarks-coldstart-{i}", ollamaUrl);

            var stopwatch = Stopwatch.StartNew();
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var initializeMs = stopwatch.Elapsed.TotalMilliseconds;

            var toolsStopwatch = Stopwatch.StartNew();
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var toolsListMs = toolsStopwatch.Elapsed.TotalMilliseconds;

            samples.Add(new RunResult(initializeMs, toolsListMs, tools.Count));
        }

        var initMs = samples.Select(s => s.InitializeMs).OrderBy(v => v).ToList();
        return new Result(
            runs,
            ollamaUrl,
            samples,
            initMs.Average(),
            Stats.Percentile(initMs, 0.5),
            initMs.Min(),
            initMs.Max(),
            samples.Select(s => s.FirstToolsListMs).Average());
    }
}
