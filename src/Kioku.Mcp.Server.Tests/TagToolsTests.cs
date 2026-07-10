using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for add_tag / remove_tag / update_frontmatter over a real temporary vault.
/// Uses VaultFixture to provision a throwaway vault directory.
/// </summary>
public class TagToolsTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public TagToolsTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    private NoteCommandTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        return new NoteCommandTools(_fixture.Index, config, vaultConfig);
    }

    [Fact]
    public async Task RemoveTag_LastTag_ClearsTagsSection()
    {
        var tools = CreateTools();
        var name = $"RemoveLastTag-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(name, "Body", tags: ["test/addition"]);
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.remove_tag(name, "test/addition");
        Assert.StartsWith("[ok]", result);
        await _fixture.Index.RebuildIndexAsync();

        var raw = await File.ReadAllTextAsync(_fixture.GetNotePath(name));
        Assert.DoesNotContain("tags:", raw);
        Assert.Empty(_fixture.Index.GetNoteByName(name)!.Metadata.Tags);
    }

    [Fact]
    public async Task RemoveTag_OneOfMany_KeepsRemaining()
    {
        var tools = CreateTools();
        var name = $"RemovePartialTag-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(name, "Body", tags: ["keep", "drop"]);
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.remove_tag(name, "drop");
        Assert.StartsWith("[ok]", result);
        await _fixture.Index.RebuildIndexAsync();

        var tags = _fixture.Index.GetNoteByName(name)!.Metadata.Tags;
        Assert.Contains("keep", tags);
        Assert.DoesNotContain("drop", tags);
    }

    [Fact]
    public async Task UpdateFrontmatter_ClearTagsTrue_ClearsExistingTags()
    {
        var tools = CreateTools();
        var name = $"ClearTagsExplicit-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(name, "Body", tags: ["a", "b"]);
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, clear_tags: true);
        Assert.StartsWith("[ok]", result);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Empty(_fixture.Index.GetNoteByName(name)!.Metadata.Tags);
    }

    [Fact]
    public async Task UpdateFrontmatter_EmptyTagsNoClearFlag_LeavesTagsUnmodified()
    {
        var tools = CreateTools();
        var name = $"NoOpTags-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(name, "Body", tags: ["a", "b"]);
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, tags: "", status: "published");
        Assert.StartsWith("[ok]", result);
        await _fixture.Index.RebuildIndexAsync();

        var metadata = _fixture.Index.GetNoteByName(name)!.Metadata;
        Assert.Contains("a", metadata.Tags);
        Assert.Contains("b", metadata.Tags);
        Assert.Equal("published", metadata.Status);
    }
}
