using System.Globalization;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// CLI options for `dotnet run --project scripts/Kioku.Benchmarks -- suite [options]`.
///
/// --sizes accepts any positive integers (e.g. --sizes 1000,10000,50000) so a user with more
/// time or better hardware can run larger vaults later; this session only actually executes
/// whatever is passed (default: a few sizes chosen to complete in a few minutes total).
/// </summary>
public sealed record SuiteOptions(
    IReadOnlyList<int> IndexingSizes,
    int SearchScenarioVaultSize,
    int SearchQueryCount,
    int ColdStartRuns,
    string EvalVaultPath,
    string GoldenSetPath,
    string ServerProjectPath,
    string EvalProjectPath,
    string OutputPath,
    string Label)
{
    public static SuiteOptions Parse(string[] args)
    {
        var sizes = new List<int> { 100, 500, 1000, 2000 };
        var searchVaultSize = 500;
        var searchQueries = 60;
        var coldStartRuns = 5;
        var evalVault = "src/Kioku.Mcp.Server.Tests/Fixtures/EvalVault";
        var goldenSet = "src/Kioku.Mcp.Server.Tests/Fixtures/golden-set.json";
        var serverProject = "src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj";
        var evalProject = "scripts/Kioku.Eval/Kioku.Eval.csproj";
        var output = "scripts/Kioku.Benchmarks/output/benchmark-report.json";
        var label = "benchmark-suite";

        for (var i = 0; i < args.Length; i++)
        {
            string Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {args[i]}");
                }

                return args[++i];
            }

            switch (args[i])
            {
                case "--sizes":
                    sizes = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();
                    break;
                case "--search-vault-size":
                    searchVaultSize = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--search-queries":
                    searchQueries = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--cold-start-runs":
                    coldStartRuns = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--eval-vault":
                    evalVault = Next();
                    break;
                case "--golden":
                    goldenSet = Next();
                    break;
                case "--server-project":
                    serverProject = Next();
                    break;
                case "--eval-project":
                    evalProject = Next();
                    break;
                case "--output":
                    output = Next();
                    break;
                case "--label":
                    label = Next();
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]} (use --help)");
            }
        }

        if (sizes.Count == 0 || sizes.Any(s => s <= 0))
        {
            throw new ArgumentException("--sizes must be a comma-separated list of positive integers.");
        }

        if (searchVaultSize <= 0 || searchQueries <= 0 || coldStartRuns <= 0)
        {
            throw new ArgumentException("--search-vault-size, --search-queries and --cold-start-runs must be positive.");
        }

        return new SuiteOptions(
            sizes, searchVaultSize, searchQueries, coldStartRuns,
            evalVault, goldenSet, serverProject, evalProject, output, label);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: dotnet run --project scripts/Kioku.Benchmarks -c Release -- suite [options]

            Options:
              --sizes <csv>              Vault sizes for indexing/memory benchmark (default: 100,500,1000,2000)
              --search-vault-size <n>    Vault size for search-latency/embedding-cache/update benchmark (default: 500)
              --search-queries <n>       Number of queries sampled per search mode (default: 60)
              --cold-start-runs <n>      Number of cold-start process spawns to average (default: 5)
              --eval-vault <path>        Retrieval-quality vault (default: src/Kioku.Mcp.Server.Tests/Fixtures/EvalVault)
              --golden <path>            Golden set JSON (default: src/Kioku.Mcp.Server.Tests/Fixtures/golden-set.json)
              --server-project <path>    Server project (default: src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj)
              --eval-project <path>      Kioku.Eval project (default: scripts/Kioku.Eval/Kioku.Eval.csproj)
              --output <path>            Report JSON path (default: scripts/Kioku.Benchmarks/output/benchmark-report.json)
              --label <name>             Label recorded in the report and passed to Kioku.Eval

            --sizes accepts arbitrarily large values (e.g. 10000,50000) for future runs on better
            hardware or with more time; this run only executes the sizes actually passed.

            Semantic/hybrid measurements require a reachable Ollama with KIOKU_EMBEDDING_MODEL
            (default nomic-embed-text) pulled; keyword-only measurements work without it.
            """);
    }
}
