using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Kioku.Benchmarks.Suite;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;

namespace Kioku.Benchmarks;

public static class Program
{
    // `dotnet run --project scripts/Kioku.Benchmarks -- suite [options]` runs the full
    // performance/quality benchmark suite (issue #257). Any other invocation (no args, or
    // BenchmarkDotNet's own --filter/--job flags) falls through unchanged to BenchmarkSwitcher,
    // which runs the [MemoryDiagnoser] micro-benchmarks below exactly as before.
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "suite")
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            try
            {
                return await SuiteRunner.RunAsync(args[1..], cts.Token);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] Benchmark suite failed: {ex}");
                return 1;
            }
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}

[MemoryDiagnoser]
public class EmbeddingBenchmarks
{
    private readonly float[] _a = new float[768];
    private readonly float[] _b = new float[768];

    public EmbeddingBenchmarks()
    {
        var random = new Random(42);
        for (var i = 0; i < _a.Length; i++)
        {
            _a[i] = (float)random.NextDouble();
            _b[i] = (float)random.NextDouble();
        }
    }

    [Benchmark]
    public float CosineSimilarity_768D() => EmbeddingService.CosineSimilarity(_a, _b);
}

[MemoryDiagnoser]
public class FrontmatterBenchmarks
{
    private const string NoteWithFrontmatter = """
        ---
        title: My Note
        tags: [ai, project, draft]
        status: published
        date: 2026-06-27
        ---

        # My Note

        This is the body of the note.
        """;

    [Benchmark]
    public int GetBodyStart() => FrontmatterParser.GetBodyStart(NoteWithFrontmatter);
}
