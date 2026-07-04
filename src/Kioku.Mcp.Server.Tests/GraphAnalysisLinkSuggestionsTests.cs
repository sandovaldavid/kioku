using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for GraphAnalysisTools.suggest_links / apply_link_suggestions. Each test
/// gets its own temporary vault (not shared via IClassFixture) since these tools write files
/// and rely on a controlled, deterministic fake embedding backend.
/// </summary>
public class GraphAnalysisLinkSuggestionsTests : IAsyncLifetime
{
    // Deterministic "embeddings": one dimension per topic keyword. Notes about the same topic
    // get identical (cosine similarity 1.0) vectors; unrelated topics are orthogonal (0.0).
    private static readonly string[] Topics = ["python", "cooking", "music"];

    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static float[] FakeVectorFor(string text)
    {
        var lower = text.ToLowerInvariant();
        return [.. Topics.Select(t => lower.Contains(t) ? 1f : 0f)];
    }

    private static HttpMessageHandler CreateFakeOllamaHandler() => new FakeHttpMessageHandler(async (request, ct) =>
    {
        if (request.Method == HttpMethod.Get)
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        var body = await request.Content!.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var prompt = doc.RootElement.GetProperty("prompt").GetString() ?? "";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { embedding = FakeVectorFor(prompt) }),
        };
    });

    private async Task<(GraphAnalysisTools tools, EmbeddingService embedding)> CreateToolsWithEmbeddingsAsync()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(CreateFakeOllamaHandler()));
        await embedding.InitializeAsync(_fixture.Index.GetAllNotes());

        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        return (new GraphAnalysisTools(_fixture.Index, hybrid, embedding, config), embedding);
    }

    private GraphAnalysisTools CreateToolsWithoutEmbeddings()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        return new GraphAnalysisTools(_fixture.Index, hybrid, embedding, config);
    }

    // suggest_links — per-note mode

    [Fact]
    public void SuggestLinks_NoteNotFound_ReturnsNotFound()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = tools.suggest_links("Nonexistent Note");

        Assert.Contains("[error] [NOT_FOUND]", result);
    }

    [Fact]
    public void SuggestLinks_PerNote_WithoutEmbeddings_ReturnsDependencyUnavailable()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = tools.suggest_links("Note One");

        Assert.Contains("[error] [DEPENDENCY_UNAVAILABLE]", result);
    }

    [Fact]
    public async Task SuggestLinks_PerNote_ReturnsUnlinkedSemanticCandidate()
    {
        await _fixture.CreateNoteAsync("Python Note A", "A note about python programming.");
        await _fixture.CreateNoteAsync("Python Note B", "Another note about python programming.");
        await _fixture.Index.RebuildIndexAsync();
        var (tools, _) = await CreateToolsWithEmbeddingsAsync();

        var result = tools.suggest_links("Python Note A", min_similarity: 0.5f);

        Assert.Contains("[[Python Note A]] → [[Python Note B]]", result);
        Assert.Contains("semantic-similarity", result);
    }

    [Fact]
    public async Task SuggestLinks_PerNote_ExcludesAlreadyLinkedPairs()
    {
        await _fixture.CreateNoteAsync("Python Note C", "See [[Python Note D]]. A note about python programming.");
        await _fixture.CreateNoteAsync("Python Note D", "Another note about python programming.");
        await _fixture.CreateNoteAsync("Python Note E", "Yet another note about python programming.");
        await _fixture.Index.RebuildIndexAsync();
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(CreateFakeOllamaHandler()));
        await embedding.InitializeAsync(_fixture.Index.GetAllNotes());
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        var tools = new GraphAnalysisTools(_fixture.Index, hybrid, embedding, config);

        var result = tools.suggest_links("Python Note C", min_similarity: 0.5f);

        Assert.DoesNotContain("Python Note D", result);
        Assert.Contains("Python Note E", result);
    }

    // suggest_links — vault-wide mode

    [Fact]
    public void SuggestLinksVault_WithoutEmbeddings_ReturnsStructuralFallback()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = tools.suggest_links();

        Assert.Contains("[info] Semantic link suggestions require Ollama", result);
        Assert.Contains("unlinked note", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestLinksVault_SuggestsOrphanRescue()
    {
        await _fixture.CreateNoteAsync("Python Hub", "A python programming hub.");
        await _fixture.CreateNoteAsync("Python Community", "See [[Python Hub]] for details.");
        await _fixture.CreateNoteAsync("Orphan Python Note", "An isolated note about python programming.");
        await _fixture.Index.RebuildIndexAsync();
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(CreateFakeOllamaHandler()));
        await embedding.InitializeAsync(_fixture.Index.GetAllNotes());
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        var tools = new GraphAnalysisTools(_fixture.Index, hybrid, embedding, config);

        var result = tools.suggest_links(min_similarity: 0.5f, max_suggestions: 20);

        Assert.Contains("[[Orphan Python Note]]", result);
        Assert.Contains("orphan-rescue", result);
    }

    [Fact]
    public async Task SuggestLinksVault_SuggestsIslandBridge()
    {
        await _fixture.CreateNoteAsync("Island Note One", "About music. See [[Island Note Two]].");
        await _fixture.CreateNoteAsync("Island Note Two", "About music. See [[Island Note One]].");
        await _fixture.CreateNoteAsync("Music Hub", "A music reference hub.");
        await _fixture.CreateNoteAsync("Music Community", "See [[Music Hub]] for details.");
        await _fixture.Index.RebuildIndexAsync();
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(CreateFakeOllamaHandler()));
        await embedding.InitializeAsync(_fixture.Index.GetAllNotes());
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        var tools = new GraphAnalysisTools(_fixture.Index, hybrid, embedding, config);

        var result = tools.suggest_links(min_similarity: 0.5f, max_suggestions: 20);

        Assert.Contains("island-bridge", result);
        Assert.Contains("Music Hub", result);
    }

    // apply_link_suggestions

    [Fact]
    public async Task ApplyLinkSuggestions_NoteNotFound_ReturnsNotFound()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.apply_link_suggestions("Nonexistent Note", "Note One");

        Assert.Contains("[error] [NOT_FOUND]", result);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_DryRun_PreviewsWithoutWriting()
    {
        var tools = CreateToolsWithoutEmbeddings();
        var before = await _fixture.ReadNoteBodyAsync("Note One");

        var result = await tools.apply_link_suggestions("Note One", "Note Two", dry_run: true);
        var after = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("dry_run=true", result);
        Assert.Contains("Note Two", result);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_Apply_AppendsRelatedSectionWithTargets()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.apply_link_suggestions("Note One", "Note Two");
        var body = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("[ok] Added 1 link(s)", result);
        Assert.Contains("## Related", body);
        Assert.Contains("[[Note Two]]", body);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_Idempotent_SecondRunAddsNothingNew()
    {
        var tools = CreateToolsWithoutEmbeddings();

        await tools.apply_link_suggestions("Note One", "Note Two");
        var afterFirst = await _fixture.ReadNoteBodyAsync("Note One");

        var secondResult = await tools.apply_link_suggestions("Note One", "Note Two");
        var afterSecond = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("already linked", secondResult);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_UnresolvedTarget_ReportsMissingButAppliesOthers()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.apply_link_suggestions("Note One", "Note Two, Nonexistent Target");

        Assert.Contains("[ok] Added 1 link(s)", result);
        Assert.Contains("Could not resolve: Nonexistent Target", result);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_CustomSectionHeading_UsesProvidedHeading()
    {
        var tools = CreateToolsWithoutEmbeddings();

        await tools.apply_link_suggestions("Note One", "Note Two", section: "See Also");
        var body = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("## See Also", body);
        Assert.DoesNotContain("## Related", body);
    }
}
