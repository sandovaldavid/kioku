using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;

namespace Kioku.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
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
