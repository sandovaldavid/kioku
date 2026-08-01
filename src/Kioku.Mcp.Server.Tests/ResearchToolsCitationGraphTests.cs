using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for ResearchTools.audit_citations. Each test gets its own temporary
/// vault (not shared via IClassFixture) since these tests need custom literature-note fixtures
/// with specific citekeys, beyond what the shared VaultFixture seeds by default.
/// </summary>
public class ResearchToolsCitationGraphTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private VaultConfigService _vaultConfig = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        _vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private ResearchTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new ResearchTools(_fixture.Index, config, _vaultConfig);
    }

    private async Task CreateSourceNoteAsync(string name, string citekey)
    {
        var filePath = NoteHelpers.BuildFilePath(name, _fixture.VaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var frontmatter = NoteHelpers.BuildFrontmatter(
            ["literature"], "literature", "draft",
            extraFields: new Dictionary<string, string> { ["citekey"] = citekey });

        await File.WriteAllTextAsync(filePath, frontmatter + "\n# Source\n", Encoding.UTF8);
    }

    [Fact]
    public async Task AuditCitations_NoLiteratureNotes_ReturnsClearMessage()
    {
        var tools = CreateTools();

        var result = tools.audit_citations();

        Assert.Contains("No notes with 'citekey' found", result);
    }

    [Fact]
    public async Task AuditCitations_WikilinkCitation_CountsAsCited()
    {
        await CreateSourceNoteAsync("Literature/Source One", "source1");
        await _fixture.CreateNoteAsync("Citing Note", "See [[Source One]] for details.");
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();
        var result = tools.audit_citations();

        Assert.Contains("`source1`", result);
        Assert.Contains("Citing Note", result);
        Assert.Contains("1 cited, 0 orphan", result);
    }

    [Fact]
    public async Task AuditCitations_InlineCitekeyCitation_CountsAsCited()
    {
        await CreateSourceNoteAsync("Literature/Source Two", "source2");
        await _fixture.CreateNoteAsync("Inline Citer", "As shown in [@source2], this holds.");
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();
        var result = tools.audit_citations();

        Assert.Contains("`source2`", result);
        Assert.Contains("Inline Citer", result);
    }

    [Fact]
    public async Task AuditCitations_WikilinkAndInlineFromSameNote_CountedOnce()
    {
        await CreateSourceNoteAsync("Literature/Source Three", "source3");
        await _fixture.CreateNoteAsync(
            "Double Citer", "See [[Source Three]] and also [@source3] again.");
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();
        var result = tools.audit_citations();

        Assert.Contains("| `source3` | Source Three | 1 |", result);
    }

    [Fact]
    public async Task AuditCitations_UncitedSource_ReportedAsOrphan()
    {
        await CreateSourceNoteAsync("Literature/Orphan Source", "orphankey");
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();
        var result = tools.audit_citations();

        Assert.Contains("Orphan sources (never cited)", result);
        Assert.Contains("`orphankey`", result);
    }

    [Fact]
    public async Task AuditCitations_MultipleCiters_RankedByCitationCountDescending()
    {
        await CreateSourceNoteAsync("Literature/Popular Source", "popular");
        await CreateSourceNoteAsync("Literature/Rare Source", "rare");
        await _fixture.CreateNoteAsync("Citer A", "Cites [@popular].");
        await _fixture.CreateNoteAsync("Citer B", "Cites [@popular] too.");
        await _fixture.CreateNoteAsync("Citer C", "Cites [@rare].");
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();
        var result = tools.audit_citations();

        var popularIndex = result.IndexOf("`popular`", StringComparison.Ordinal);
        var rareIndex = result.IndexOf("`rare`", StringComparison.Ordinal);
        Assert.True(popularIndex >= 0 && rareIndex >= 0 && popularIndex < rareIndex,
            "The more-cited source should be ranked before the less-cited one.");
    }

    [Fact]
    public async Task AuditCitations_FolderFilter_OnlyConsidersSourcesInThatFolder()
    {
        await CreateSourceNoteAsync("Literature/In Folder", "infolder");
        await CreateSourceNoteAsync("Projects/Out Of Folder", "outfolder");
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();
        var result = tools.audit_citations(folder: "Literature");

        Assert.Contains("`infolder`", result);
        Assert.DoesNotContain("`outfolder`", result);
    }

    [Fact]
    public async Task AuditCitations_CombinesGraphGapsAndValidationSections()
    {
        await CreateSourceNoteAsync("Literature/Source", "source");
        await _fixture.CreateNoteAsync("Literature/References", "See [@missing].");
        await _fixture.Index.RebuildIndexAsync();

        var result = CreateTools().audit_citations(folder: "Literature");

        Assert.Contains("## Citation graph", result);
        Assert.Contains("## Literature gaps", result);
        Assert.Contains("`@missing`", result);
        Assert.Contains("## Metadata validation", result);
    }

    [Fact]
    public async Task AuditCitations_FolderGraphStillFindsCitersOutsideFolder()
    {
        await CreateSourceNoteAsync("Literature/Source", "source");
        await _fixture.CreateNoteAsync("Projects/Citing Note", "See [@source].");
        await _fixture.Index.RebuildIndexAsync();

        var result = CreateTools().audit_citations(folder: "Literature");

        Assert.Contains("Citing Note", result);
        Assert.Contains("1 cited, 0 orphan", result);
    }
}
