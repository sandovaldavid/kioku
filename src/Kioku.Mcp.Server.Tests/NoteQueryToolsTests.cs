using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Tests for NoteQueryTools structured JSON output variants.
/// </summary>
public class NoteQueryToolsTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public NoteQueryToolsTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    private NoteQueryTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new NoteQueryTools(
            _fixture.Index,
            config,
            null!,
            null!);
    }

    private NoteQueryTools CreateToolsWithSearchServices()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        return new NoteQueryTools(_fixture.Index, config, embedding, hybrid);
    }

    [Fact]
    public async Task search_notes_hybrid_mode_degrades_without_ollama()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", mode: "hybrid");

        Assert.Contains("result(s)", result);
        Assert.Contains("keyword only", result);
    }

    [Fact]
    public async Task search_notes_semantic_mode_reports_unavailable_without_ollama()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", mode: "semantic");

        Assert.StartsWith("[info]", result);
        Assert.Contains("Ollama", result);
    }

    [Fact]
    public async Task search_notes_unknown_mode_is_rejected()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", mode: "fuzzy");

        Assert.StartsWith("[error]", result);
        Assert.Contains("hybrid", result);
    }

    [Fact]
    public void get_links_both_directions_reports_backlinks_and_outgoing()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "both");

        Assert.Contains("link", result, StringComparison.OrdinalIgnoreCase);
        var inOnly = tools.get_links("Note One", direction: "in");
        var outOnly = tools.get_links("Note One", direction: "out");
        Assert.DoesNotContain("outgoing link(s)", inOnly);
        Assert.DoesNotContain("note(s) link to", outOnly);
    }

    [Fact]
    public void get_links_unknown_direction_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "sideways");

        Assert.StartsWith("[error]", result);
        Assert.Contains("both", result);
    }

    [Fact]
    public async Task read_note_json_returns_structured_content()
    {
        var tools = CreateTools();

        var json = await tools.read_note("Note One", format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Note One", root.GetProperty("name").GetString());
        Assert.Equal("Note One.md", root.GetProperty("path").GetString());
        Assert.Contains("Body of note one", root.GetProperty("content").GetString());
    }

    [Fact]
    public void list_notes_json_returns_pagination_shape()
    {
        var tools = CreateTools();

        var json = tools.list_notes(limit: 2, offset: 0, format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("total").GetInt32() >= 6);
        Assert.Equal(0, root.GetProperty("offset").GetInt32());
        Assert.Equal(2, root.GetProperty("limit").GetInt32());
        Assert.True(root.GetProperty("notes").GetArrayLength() == 2);
    }

    [Fact]
    public async Task search_notes_json_returns_results()
    {
        var tools = CreateTools();

        var json = await tools.search_notes("Body of note one", mode: "keyword", format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Body of note one", root.GetProperty("query").GetString());
        Assert.True(root.GetProperty("results").GetArrayLength() > 0);
    }

    [Fact]
    public async Task read_note_metadata_json_returns_fields()
    {
        var tools = CreateTools();

        var json = await tools.read_note("Note One", metadata_only: true, format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Note One", root.GetProperty("name").GetString());
        Assert.Contains("alpha", root.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

}
