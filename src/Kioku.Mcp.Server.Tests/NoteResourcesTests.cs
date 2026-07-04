using System.Text.Json;
using Kioku.Mcp.Server.Resources;
using ModelContextProtocol;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class NoteResourcesTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public NoteResourcesTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    private NoteResources CreateResources()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new NoteResources(_fixture.Index, config);
    }

    [Fact]
    public void GetNote_ExistingNote_ReturnsRawContent()
    {
        var resources = CreateResources();

        var content = resources.GetNote("Note One");

        Assert.Contains("Body of note one.", content);
    }

    [Fact]
    public void GetNote_ResolvesByVaultRelativePathWithMdExtension()
    {
        var resources = CreateResources();

        var content = resources.GetNote("Note One.md");

        Assert.Contains("Body of note one.", content);
    }

    [Fact]
    public void GetNote_NotFound_ThrowsMcpExceptionWithNotFoundMessage()
    {
        var resources = CreateResources();

        var ex = Assert.Throws<McpException>(() => resources.GetNote("Does Not Exist"));

        Assert.Contains("[NOT_FOUND]", ex.Message);
        Assert.Contains("Does Not Exist", ex.Message);
    }

    [Fact]
    public void GetVaultStats_ReturnsValidJsonWithExpectedFields()
    {
        var resources = CreateResources();

        var json = resources.GetVaultStats();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("total_notes").GetInt32() >= 6);
        Assert.True(root.GetProperty("index_ready").GetBoolean());
        Assert.True(root.TryGetProperty("unique_tags", out _));
        Assert.True(root.TryGetProperty("folders", out _));
        Assert.True(root.TryGetProperty("vault_path", out _));
    }
}
