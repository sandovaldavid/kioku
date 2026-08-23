using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Behavioral coverage for FolderRanker.RankFolders's keyword-overlap scoring path (no Ollama,
/// so embedding.IsAvailable stays false and only the per-note-tokenized/unioned folder-token
/// path — the one refactored away from string.Join + whole-folder tokenize — is exercised).
/// </summary>
public sealed class FolderRankerTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task RankFolders_RanksHigherKeywordOverlapFolderFirst()
    {
        await _fixture.CreateNoteAsync("Source", "python asyncio concurrency patterns");
        await _fixture.CreateNoteAsync("Backend/Existing", "python asyncio web server implementation");
        await _fixture.CreateNoteAsync("Cooking/Existing", "pasta recipe with tomato sauce");
        await _fixture.Index.RebuildIndexAsync();

        var source = _fixture.Index.ResolveNote("Source")!;
        var (embedding, hybrid) = CreateUnavailableEmbeddingAndHybrid();

        var ranked = FolderRanker.RankFolders(source, topN: 5, _fixture.Index, hybrid, embedding);

        Assert.NotEmpty(ranked);
        Assert.Equal("Backend", ranked[0].Folder);
    }

    [Fact]
    public async Task RankFolders_ExcludesTheSourceNotesOwnFolder()
    {
        await _fixture.CreateNoteAsync("Backend/Source", "python asyncio concurrency patterns");
        await _fixture.CreateNoteAsync("Backend/Sibling", "python asyncio web server implementation");
        await _fixture.CreateNoteAsync("Cooking/Existing", "pasta recipe with tomato sauce");
        await _fixture.Index.RebuildIndexAsync();

        var source = _fixture.Index.ResolveNote("Backend/Source")!;
        var (embedding, hybrid) = CreateUnavailableEmbeddingAndHybrid();

        var ranked = FolderRanker.RankFolders(source, topN: 5, _fixture.Index, hybrid, embedding);

        Assert.DoesNotContain(ranked, r => r.Folder.Equals("Backend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RankFolders_NoOverlapWithAnyFolder_ReturnsEmpty()
    {
        await _fixture.CreateNoteAsync("Source", "zzzznonexistentword only appears here");
        await _fixture.CreateNoteAsync("Backend/Existing", "python asyncio web server implementation");
        await _fixture.Index.RebuildIndexAsync();

        var source = _fixture.Index.ResolveNote("Source")!;
        var (embedding, hybrid) = CreateUnavailableEmbeddingAndHybrid();

        var ranked = FolderRanker.RankFolders(source, topN: 5, _fixture.Index, hybrid, embedding);

        Assert.Empty(ranked);
    }

    private (EmbeddingService Embedding, HybridSearchService Hybrid) CreateUnavailableEmbeddingAndHybrid()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        // Never calling InitializeAsync leaves IsAvailable at its default (false), so
        // RankFolders skips the Ollama-backed similarity branch entirely — no fake HTTP
        // responder is needed since no HTTP call is ever attempted.
        var embedding = new EmbeddingService(
            config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP should not be called when IsAvailable is false."))));
        return (embedding, new HybridSearchService(_fixture.Index, embedding));
    }
}
