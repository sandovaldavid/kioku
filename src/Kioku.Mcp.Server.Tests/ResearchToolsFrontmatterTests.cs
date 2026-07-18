using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class ResearchToolsFrontmatterTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private VaultConfigService _vaultConfig = null!;

    private const string InitialEntry = """
        @article{smith2020,
          author = {Smith, John},
          title = {A Study of Things},
          year = {2020},
          journal = {Journal of Studies},
        }
        """;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        _vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ImportBibtex_UpdateExisting_PreservesBodyAndNonBibtexMetadata()
    {
        var tools = CreateTools();
        await tools.import_bibtex(InitialEntry);
        await _fixture.Index.RebuildIndexAsync();

        var note = _fixture.Index.GetAllNotes()
            .Single(candidate => candidate.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
        var original = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath));
        original.SetStringList("cssclasses", ["wide-page"]);
        original.SetValue("custom", new Dictionary<string, object?>
        {
            ["owner"] = "human",
            ["flags"] = new List<object?> { "keep", "me" },
        });
        var originalBody = original.Body;
        await File.WriteAllTextAsync(note.FilePath, original.Serialize(), NoteHelpers.Utf8NoBom);
        await _fixture.Index.RebuildIndexAsync();

        var updatedEntry = InitialEntry.Replace(
            "Journal of Studies",
            "Journal of Updated Studies",
            StringComparison.Ordinal);
        var result = await tools.import_bibtex(updatedEntry, update_existing: true);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("updated", result);
        var refreshed = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath));
        var metadata = refreshed.ToFrontmatter();

        Assert.Equal(originalBody, refreshed.Body);
        Assert.Equal(["wide-page"], metadata.CssClasses);
        Assert.Equal("Journal of Updated Studies", metadata.ExtraFields["journal"]);

        var custom = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(metadata.ExtraFields["custom"]);
        Assert.Equal("human", custom["owner"]);
        Assert.Equal(["keep", "me"], Assert.IsAssignableFrom<IEnumerable<object?>>(custom["flags"]));
    }

    private ResearchTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new ResearchTools(_fixture.Index, config, _vaultConfig);
    }
}
