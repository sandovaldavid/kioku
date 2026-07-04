using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for VaultOrganizationTools.process_inbox. Each test gets its own temporary
/// vault (not shared via IClassFixture) since process_inbox moves and writes files.
/// </summary>
public class VaultOrganizationToolsInboxTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private HybridSearchService _hybrid = null!;
    private EmbeddingService _embedding = null!;
    private VaultConfigService _vaultConfig = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        _embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance, new FakeHttpClientFactory(
            new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        _hybrid = new HybridSearchService(_fixture.Index, _embedding);
        _vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private VaultOrganizationTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new VaultOrganizationTools(_fixture.Index, config, _hybrid, _embedding, _vaultConfig);
    }

    [Fact]
    public async Task ProcessInbox_NonexistentFolder_ReturnsInfoMessage()
    {
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "DoesNotExist");

        Assert.Contains("[info]", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public async Task ProcessInbox_EmptyInbox_ReturnsInfoMessage()
    {
        Directory.CreateDirectory(_fixture.GetFolderPath("Inbox"));
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "Inbox");

        Assert.Contains("[info]", result);
        Assert.Contains("empty", result);
    }

    [Fact]
    public async Task ProcessInbox_DryRun_ReturnsNumberedPlanWithoutModifyingFiles()
    {
        await _fixture.CreateNoteAsync("Inbox/Capture A", "About python programming and scripting.");
        await _fixture.CreateNoteAsync("Python/Existing Note", "Python programming language reference.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "Inbox");

        Assert.Contains("[info] Inbox plan for 'Inbox'", result);
        Assert.Contains("1. \"Capture A\"", result);
        Assert.Contains("apply=false", result);
        Assert.True(_fixture.NoteExists("Inbox/Capture A"));
        Assert.False(_fixture.NoteExists("Python/Capture A"));
    }

    [Fact]
    public async Task ProcessInbox_Apply_MovesNoteToSuggestedFolderAndAddsTags()
    {
        await _fixture.CreateNoteAsync("Inbox/Capture A", "About python programming and scripting.");
        await _fixture.CreateNoteAsync("Python/Existing Note", "Python programming language reference.", tags: ["python"]);
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "Inbox", apply: true);

        Assert.Contains("[ok] Processed 1 note(s)", result);
        Assert.False(_fixture.NoteExists("Inbox/Capture A"));
        Assert.True(_fixture.NoteExists("Python/Capture A"));
    }

    [Fact]
    public async Task ProcessInbox_Apply_UpdatesInboundFullPathWikilinksAfterMove()
    {
        await _fixture.CreateNoteAsync("Inbox/Capture A", "About python programming and scripting.");
        await _fixture.CreateNoteAsync("Python/Existing Note", "Python programming language reference.");
        await _fixture.CreateNoteAsync("Linker", "See [[Inbox/Capture A]] for details.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        await tools.process_inbox(inbox_folder: "Inbox", apply: true);

        var linkerBody = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[Python/Capture A]]", linkerBody);
        Assert.DoesNotContain("[[Inbox/Capture A]]", linkerBody);
    }

    [Fact]
    public async Task ProcessInbox_NoOverlappingFolderContent_KeepsNoteInInbox()
    {
        // No word overlap with any existing folder's notes, so FolderRanker finds no
        // destination (it filters out zero-score candidates) — the note should stay put.
        await _fixture.CreateNoteAsync(
            "Inbox/Lonely Capture", "Completely unrelated gibberish zzyxx qwerty wibble wobble flonk.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "Inbox", apply: true);

        Assert.True(_fixture.NoteExists("Inbox/Lonely Capture"));
        Assert.DoesNotContain("moved to", result);
    }

    [Fact]
    public async Task ProcessInbox_WithoutEmbeddings_OmitsLinkSuggestionsWithNotice()
    {
        await _fixture.CreateNoteAsync("Inbox/Capture A", "About python programming and scripting.");
        await _fixture.CreateNoteAsync("Python/Existing Note", "Python programming language reference.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "Inbox", apply: true);

        Assert.Contains("[info] Semantic embeddings are unavailable", result);
    }

    [Fact]
    public async Task ProcessInbox_MaxNotes_LimitsHowManyAreProcessed()
    {
        await _fixture.CreateNoteAsync("Inbox/Capture A", "Content about python.");
        await _fixture.CreateNoteAsync("Inbox/Capture B", "Content about python.");
        await _fixture.CreateNoteAsync("Python/Existing Note", "Python programming language reference.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.process_inbox(inbox_folder: "Inbox", max_notes: 1);

        Assert.Contains("1 note(s)", result);
    }
}
