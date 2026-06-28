using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
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
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        return new NoteQueryTools(
            _fixture.Index,
            config,
            null!,
            null!,
            vaultConfig);
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
    public void search_notes_json_returns_results()
    {
        var tools = CreateTools();

        var json = tools.search_notes("Body of note one", format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Body of note one", root.GetProperty("query").GetString());
        Assert.True(root.GetProperty("results").GetArrayLength() > 0);
    }

    [Fact]
    public void get_note_metadata_json_returns_fields()
    {
        var tools = CreateTools();

        var json = tools.get_note_metadata("Note One", format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Note One", root.GetProperty("name").GetString());
        Assert.Contains("alpha", root.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void get_vault_stats_json_returns_counts()
    {
        var tools = CreateTools();

        var json = tools.get_vault_stats(format: "json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("total_notes").GetInt32() >= 6);
        Assert.True(root.GetProperty("unique_tags").GetInt32() >= 4);
        Assert.True(root.GetProperty("folders").GetInt32() >= 2);
        Assert.True(root.GetProperty("index_ready").GetBoolean());
    }
}
