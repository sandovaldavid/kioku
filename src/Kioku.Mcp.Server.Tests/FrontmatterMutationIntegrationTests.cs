using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class FrontmatterMutationIntegrationTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task UpdateFrontmatter_PreservesNestedMetadataCssClassesAndBody()
    {
        var name = $"FrontmatterMutation-{Guid.NewGuid():N}";
        var filePath = _fixture.GetNotePath(name);
        const string body = "# Body\n\nstatus: draft in Markdown must remain unchanged.\n";
        const string frontmatter = """
            ---
            tags: [alpha]
            aliases: [My Alias]
            cssclasses: [dashboard]
            type: note
            status: draft
            custom:
              owner: human
              flags: [keep, me]
            ---

            """;
        var source = FrontmatterDocument.Parse(frontmatter + body);
        await File.WriteAllTextAsync(filePath, source.Serialize(), NoteHelpers.Utf8NoBom);
        await _fixture.Index.RebuildIndexAsync();

        var result = await CreateTools().update_frontmatter(name, status: "done", add_tags: "beta");
        await _fixture.Index.RebuildIndexAsync();

        Assert.StartsWith("[ok]", result);
        var updated = FrontmatterDocument.Parse(await File.ReadAllTextAsync(filePath));
        var metadata = updated.ToFrontmatter();

        Assert.Equal(body, updated.Body);
        Assert.Contains("status: draft in Markdown", updated.Body);
        Assert.Equal("done", metadata.Status);
        Assert.Equal(["alpha", "beta"], metadata.Tags);
        Assert.Equal(["My Alias"], metadata.Aliases);
        Assert.Equal(["dashboard"], metadata.CssClasses);

        var custom = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(metadata.ExtraFields["custom"]);
        Assert.Equal("human", custom["owner"]);
        Assert.Equal(["keep", "me"], Assert.IsAssignableFrom<IEnumerable<object?>>(custom["flags"]));
    }

    private NoteCommandTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var embedding = new EmbeddingService(
            config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var zettelkasten = new ZettelkastenTools(
            _fixture.Index, embedding, hybrid, config, vaultConfig, bridge);
        return new NoteCommandTools(_fixture.Index, config, vaultConfig, zettelkasten);
    }
}
