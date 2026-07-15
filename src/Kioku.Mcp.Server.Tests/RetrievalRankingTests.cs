using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Shared EvalVault: copies the checked-in fixture vault to a temp directory and boots the
/// full retrieval stack (index + embeddings via the deterministic fake + hybrid RRF) once.
/// </summary>
public sealed class EvalVaultFixture : IAsyncLifetime
{
    public string VaultPath { get; private set; } = null!;
    public VaultIndexService Vault { get; private set; } = null!;
    public EmbeddingService Embedding { get; private set; } = null!;
    public HybridSearchService Hybrid { get; private set; } = null!;
    public GoldenSet Golden { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        VaultPath = Path.Combine(Path.GetTempPath(), $"kioku-evalvault-{Guid.NewGuid():N}");
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EvalVault");
        CopyDirectory(source, VaultPath);

        var config = new KiokuConfiguration { VaultPath = VaultPath, EmbeddingModel = "nomic-embed-text" };
        Embedding = new EmbeddingService(
            config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler(DeterministicEmbedding.Responder())));
        Vault = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config, Embedding);
        Hybrid = new HybridSearchService(Vault, Embedding);

        await Vault.InitializeAsync();
        await WaitForEmbeddingsAsync();

        Golden = GoldenSet.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-set.json"));
    }

    public Task DisposeAsync()
    {
        Vault.Dispose();
        Embedding.Dispose();
        try
        {
            Directory.Delete(VaultPath, recursive: true);
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private async Task WaitForEmbeddingsAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while ((Embedding.CachedEmbeddingCount < Vault.IndexedCount || Embedding.EmbeddingBacklog > 0)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        if (Embedding.CachedEmbeddingCount < Vault.IndexedCount)
        {
            throw new TimeoutException(
                $"Embedding backlog did not drain: {Embedding.CachedEmbeddingCount}/{Vault.IndexedCount} embedded.");
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}

/// <summary>
/// Retrieval-quality regression tests over the golden set, using the deterministic fake
/// embedder. Assertions are floors and invariants — never exact orderings — so they catch
/// real ranking regressions without overfitting to the fake's lexical bias.
/// </summary>
public class RetrievalRankingTests(EvalVaultFixture fixture) : IClassFixture<EvalVaultFixture>
{
    private const int K = 10;

    // Queries whose relevant notes share exact tokens with the query text — the subset both
    // the keyword leg and the lexical fake embedder are expected to handle well.
    private static readonly string[] LexicalQueryIds =
    [
        "q01-keyword-es", "q03-keyword-en", "q06-title", "q07-multi-note",
        "q13-finance", "q14-kubernetes", "q17-home-server", "q19-investing",
        "q20-forest-distractor", "q22-gtd",
    ];

    // Calibrated against the current index + fake embedder (observed means: keyword 0.633,
    // semantic 0.833, hybrid 0.783); floors sit well below so only real ranking regressions fail.
    private const double KeywordRecallFloor = 0.55;
    private const double SemanticRecallFloor = 0.70;
    private const double HybridRecallFloor = 0.65;

    private List<string> RankedKeyword(string query) =>
        fixture.Vault.Search(query, K).Select(r => r.Note.VaultRelativePath).ToList();

    private async Task<List<string>> RankedSemanticAsync(string query, float minScore = 0f)
    {
        var vector = await fixture.Embedding.EmbedQueryAsync(query);
        Assert.NotNull(vector);
        var notesByPath = fixture.Vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);
        return fixture.Embedding
            .SearchByVector(vector!, K, string.Empty, notesByPath, minScore)
            .Select(r => r.Note.VaultRelativePath)
            .ToList();
    }

    private async Task<List<string>> RankedHybridAsync(string query)
    {
        var vector = await fixture.Embedding.EmbedQueryAsync(query);
        return fixture.Hybrid.Search(query, K, queryVector: vector)
            .Select(r => r.Note.VaultRelativePath)
            .ToList();
    }

    private IEnumerable<GoldenQuery> LexicalQueries() =>
        fixture.Golden.Queries.Where(q => LexicalQueryIds.Contains(q.Id));

    [Fact]
    public void KeywordSearch_LexicalQueries_MeetRecallFloor()
    {
        var recalls = LexicalQueries()
            .Select(q => RetrievalMetrics.RecallAtK(RankedKeyword(q.Query), q.RelevanceByPath(), K))
            .ToList();

        var mean = recalls.Average();
        Assert.True(mean >= KeywordRecallFloor,
            $"Keyword mean Recall@{K} over lexical queries was {mean:F3}, expected >= {KeywordRecallFloor}.");
    }

    [Fact]
    public async Task SemanticSearch_LexicalQueries_MeetRecallFloor()
    {
        var recalls = new List<double>();
        foreach (var q in LexicalQueries())
        {
            recalls.Add(RetrievalMetrics.RecallAtK(await RankedSemanticAsync(q.Query), q.RelevanceByPath(), K));
        }

        var mean = recalls.Average();
        Assert.True(mean >= SemanticRecallFloor,
            $"Semantic (fake) mean Recall@{K} over lexical queries was {mean:F3}, expected >= {SemanticRecallFloor}.");
    }

    [Fact]
    public async Task HybridSearch_LexicalQueries_MeetRecallFloor()
    {
        var recalls = new List<double>();
        foreach (var q in LexicalQueries())
        {
            recalls.Add(RetrievalMetrics.RecallAtK(await RankedHybridAsync(q.Query), q.RelevanceByPath(), K));
        }

        var mean = recalls.Average();
        Assert.True(mean >= HybridRecallFloor,
            $"Hybrid mean Recall@{K} over lexical queries was {mean:F3}, expected >= {HybridRecallFloor}.");
    }

    [Fact]
    public async Task SemanticSearch_AliasOnlyQuery_FindsNoteKeywordSearchMisses()
    {
        // "slip-box" exists only in the frontmatter aliases of Metodo Zettelkasten.md.
        // Aliases are excluded from the keyword word-index (PlainText strips frontmatter) but are
        // part of the embedding text, so the semantic leg must find the note.
        var query = fixture.Golden.Queries.Single(q => q.Id == "q10-alias");
        var judgments = query.RelevanceByPath();

        var semanticRecall = RetrievalMetrics.RecallAtK(await RankedSemanticAsync(query.Query), judgments, K);
        Assert.Equal(1.0, semanticRecall, precision: 10);

        var hybridRecall = RetrievalMetrics.RecallAtK(await RankedHybridAsync(query.Query), judgments, K);
        Assert.Equal(1.0, hybridRecall, precision: 10);
    }

    [Fact]
    public async Task SemanticSearch_MinScore_FiltersLowSimilarityResults()
    {
        var unfiltered = await RankedSemanticAsync("quantum entanglement research papers");
        var filtered = await RankedSemanticAsync("quantum entanglement research papers", minScore: 0.9f);

        Assert.True(filtered.Count < unfiltered.Count,
            $"min_score=0.9 returned {filtered.Count} results vs {unfiltered.Count} unfiltered — expected it to filter.");
    }

    [Fact]
    public async Task HybridSearch_SameQueryTwice_ReturnsIdenticalRanking()
    {
        var first = await RankedHybridAsync("docker deployment production");
        var second = await RankedHybridAsync("docker deployment production");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task HybridSearch_StrongLexicalQuery_RanksExpectedNoteFirst()
    {
        var ranked = await RankedHybridAsync("burnout laboral");

        Assert.True(ranked.Count > 0, "Hybrid search returned no results.");
        Assert.Contains(ranked.Take(3), p => p.Replace('\\', '/').Equals("Salud/Burnout Laboral.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LongNote_TailFact_IsFindableByKeywordSearch()
    {
        // The word index covers the full note text, so the fact buried at the end of the long
        // note must be reachable via keywords regardless of chunking.
        var query = fixture.Golden.Queries.Single(q => q.Id == "q11-long-note-tail");
        var recall = RetrievalMetrics.RecallAtK(RankedKeyword(query.Query), query.RelevanceByPath(), K);

        Assert.Equal(1.0, recall, precision: 10);
    }

    [Fact]
    public async Task LongNote_TailFact_IsFindableBySemanticAndHybridSearch()
    {
        // Heading-aware chunking's whole point: the note is too long to embed as a single
        // vector without losing this buried fact. Split by heading, the section containing it
        // becomes its own chunk, and the semantic/hybrid legs must now find it too.
        var query = fixture.Golden.Queries.Single(q => q.Id == "q11-long-note-tail");
        var judgments = query.RelevanceByPath();

        var semanticRecall = RetrievalMetrics.RecallAtK(await RankedSemanticAsync(query.Query), judgments, K);
        Assert.Equal(1.0, semanticRecall, precision: 10);

        var hybridRecall = RetrievalMetrics.RecallAtK(await RankedHybridAsync(query.Query), judgments, K);
        Assert.Equal(1.0, hybridRecall, precision: 10);
    }
}
