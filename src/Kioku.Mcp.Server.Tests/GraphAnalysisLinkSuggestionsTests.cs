using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for GraphAnalysisTools.suggest_links. Each test
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
    public async Task SuggestLinks_NoteNotFound_ReturnsNotFound()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Nonexistent Note");

        Assert.Contains("[error] [NOT_FOUND]", result);
    }

    [Fact]
    public async Task SuggestLinks_PerNote_WithoutEmbeddings_ReturnsDependencyUnavailable()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Note One");

        Assert.Contains("[error] [DEPENDENCY_UNAVAILABLE]", result);
    }

    [Fact]
    public async Task SuggestLinks_PerNote_ReturnsUnlinkedSemanticCandidate()
    {
        await _fixture.CreateNoteAsync("Python Note A", "A note about python programming.");
        await _fixture.CreateNoteAsync("Python Note B", "Another note about python programming.");
        await _fixture.Index.RebuildIndexAsync();
        var (tools, _) = await CreateToolsWithEmbeddingsAsync();

        var result = await tools.suggest_links("Python Note A", min_similarity: 0.5f);

        Assert.Contains("[[Python Note A]] → [[Python Note B]]", result);
        Assert.Contains("semantic-similarity", result);
    }

    [Fact]
    public async Task SuggestLinks_PerNote_ApplyAddsSemanticCandidate()
    {
        await _fixture.CreateNoteAsync("Python Note A", "A note about python programming.");
        await _fixture.CreateNoteAsync("Python Note B", "Another note about python programming.");
        await _fixture.Index.RebuildIndexAsync();
        var (tools, _) = await CreateToolsWithEmbeddingsAsync();

        var result = await tools.suggest_links("Python Note A", apply: true, min_similarity: 0.5f);
        var body = await _fixture.ReadNoteBodyAsync("Python Note A");

        Assert.Contains("Added 1 related link(s)", result);
        Assert.Contains("## Related", body);
        Assert.Contains("[[Python Note B]]", body);
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

        var result = await tools.suggest_links("Python Note C", min_similarity: 0.5f);

        Assert.DoesNotContain("Python Note D", result);
        Assert.Contains("Python Note E", result);
    }

    // suggest_links — vault-wide mode

    [Fact]
    public async Task SuggestLinksVault_WithoutEmbeddings_ReturnsStructuralFallback()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links();

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

        var result = await tools.suggest_links(min_similarity: 0.5f, max_suggestions: 20);

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

        var result = await tools.suggest_links(min_similarity: 0.5f, max_suggestions: 20);

        Assert.Contains("island-bridge", result);
        Assert.Contains("Music Hub", result);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_NoteNotFound_ReturnsNotFound()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Nonexistent Note", targets: "Note One", apply: true);

        Assert.Contains("[error] [NOT_FOUND]", result);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_DryRun_PreviewsWithoutWriting()
    {
        var tools = CreateToolsWithoutEmbeddings();
        var before = await _fixture.ReadNoteBodyAsync("Note One");

        var result = await tools.suggest_links("Note One", targets: "Note Two");
        var after = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("dry_run=true", result);
        Assert.Contains("Note Two", result);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_Apply_AppendsRelatedSectionWithTargets()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Note One", targets: "Note Two", apply: true);
        var body = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("[ok] Added 1 link(s)", result);
        Assert.Contains("## Related", body);
        Assert.Contains("[[Note Two]]", body);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_Idempotent_SecondRunAddsNothingNew()
    {
        var tools = CreateToolsWithoutEmbeddings();

        await tools.suggest_links("Note One", targets: "Note Two", apply: true);
        var afterFirst = await _fixture.ReadNoteBodyAsync("Note One");

        var secondResult = await tools.suggest_links("Note One", targets: "Note Two", apply: true);
        var afterSecond = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("already linked", secondResult);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_UnresolvedTarget_ReportsMissingButAppliesOthers()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Note One", targets: "Note Two, Nonexistent Target", apply: true);

        Assert.Contains("[ok] Added 1 link(s)", result);
        Assert.Contains("Could not resolve: Nonexistent Target", result);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_CustomSectionHeading_UsesProvidedHeading()
    {
        var tools = CreateToolsWithoutEmbeddings();

        await tools.suggest_links("Note One", targets: "Note Two", section: "See Also", apply: true);
        var body = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("## See Also", body);
        Assert.DoesNotContain("## Related", body);
    }

    [Fact]
    public void VaultSnapshot_ContainsConsolidatedGraphAnalysis()
    {
        var tools = new KnowledgeGraphTools(_fixture.Index);

        var result = tools.get_vault_snapshot();

        Assert.Contains("## Graph density", result);
        Assert.Contains("Average backlinks/note", result);
        Assert.Contains("## Unlinked notes", result);
        Assert.Contains("## Graph islands", result);
    }

    [Fact]
    public async Task VaultSnapshot_DuplicateBasenames_UsesPathIdentityWithoutCrashing()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root duplicate.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder duplicate.");
        await _fixture.Index.RebuildIndexAsync();

        var result = new KnowledgeGraphTools(_fixture.Index).get_vault_snapshot();

        Assert.StartsWith("[ok]", result);
        Assert.Contains("Duplicate.md", result);
        Assert.Contains("Folder/Duplicate.md", result);
    }

    [Fact]
    public async Task StructuralFallback_DuplicateBasenames_UsesPathIdentityWithoutCrashing()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root duplicate.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder duplicate.");
        await _fixture.CreateNoteAsync("Path Linker", "See [[Folder/Duplicate]].");
        await _fixture.Index.RebuildIndexAsync();

        var result = await CreateToolsWithoutEmbeddings().suggest_links();

        Assert.StartsWith("[info]", result);
        Assert.Contains("Folder/Duplicate.md", result);
        Assert.Contains("Path Linker", result);
    }

    [Fact]
    public async Task GetBacklinks_PathQualifiedLink_ResolvesDuplicateBasenameDeterministically()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root duplicate.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder duplicate.");
        await _fixture.CreateNoteAsync("Path Linker", "See [[Folder/Duplicate]].");
        await _fixture.Index.RebuildIndexAsync();

        var pathBacklinks = _fixture.Index.GetBacklinks("Folder/Duplicate");
        var ambiguousBacklinks = _fixture.Index.GetBacklinks("Duplicate");

        Assert.Contains(pathBacklinks, note => note.Name == "Path Linker");
        Assert.Empty(ambiguousBacklinks);
    }

    [Fact]
    public void GetBacklinks_UniqueBareName_ResolvesExistingLink()
    {
        var backlinks = _fixture.Index.GetBacklinks("Note One");

        Assert.Contains(backlinks, note => note.Name == "Note Three");
    }

    [Fact]
    public async Task ApplyLinkSuggestions_PathQualifiedTarget_WritesPathQualifiedLink()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root duplicate.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder duplicate.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Note One", targets: "Folder/Duplicate", apply: true);
        var body = await _fixture.ReadNoteBodyAsync("Note One");

        Assert.Contains("[ok] Added 1 link(s)", result);
        Assert.Contains("[[Folder/Duplicate]]", body);
    }

    [Fact]
    public async Task ApplyLinkSuggestions_AmbiguousBareTarget_IsNotResolved()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root duplicate.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder duplicate.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links("Note One", targets: "Duplicate", apply: true);

        Assert.Contains("[error] [NOT_FOUND]", result);
        Assert.Contains("Duplicate", result);
    }

    [Fact]
    public async Task SuggestLinks_InvalidThreshold_ReturnsInvalidArgument()
    {
        var tools = CreateToolsWithoutEmbeddings();

        var result = await tools.suggest_links(min_similarity: 1.1f);

        Assert.Equal("[error] [INVALID_ARGUMENT] 'min_similarity' must be between 0 and 1.", result);
    }

    [Fact]
    public void VaultSnapshot_InvalidThreshold_ReturnsInvalidArgument()
    {
        var tools = new KnowledgeGraphTools(_fixture.Index);

        var result = tools.get_vault_snapshot(island_threshold: 0);

        Assert.Equal("[error] [INVALID_ARGUMENT] Island threshold must be at least 1.", result);
    }
}
