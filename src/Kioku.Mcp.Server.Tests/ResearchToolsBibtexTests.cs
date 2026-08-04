using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for ResearchTools.import_bibtex / export_citations. Each test gets its own
/// temporary vault (not shared via IClassFixture) since these tools create and update files.
/// </summary>
public class ResearchToolsBibtexTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private VaultConfigService _vaultConfig = null!;

    private const string OneEntry = """
        @article{smith2020,
          author = {Smith, John},
          title  = {A Study of Things},
          year   = {2020},
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

    private ResearchTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new ResearchTools(_fixture.Index, config, _vaultConfig, new VaultPathPolicy(config));
    }

    private async Task CreateCitationNoteAsync(string name, string citekeyField, string citekey)
    {
        var filePath = NoteHelpers.BuildFilePath(name, _fixture.VaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            ["literature"], "literature", "draft",
            extraFields: new Dictionary<string, string>
            {
                [citekeyField] = citekey,
                ["title"] = $"Title for {citekey}",
            });
        await File.WriteAllTextAsync(filePath, frontmatter + "\n# Citation\n");
    }

    [Fact]
    public async Task ImportBibtex_InlineContent_CreatesLiteratureNoteWithFrontmatterFields()
    {
        var tools = CreateTools();

        var result = await tools.import_bibtex(OneEntry);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("[ok] Imported 1 entries", result);
        var note = _fixture.Index.GetAllNotes().Single(n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
        Assert.Equal("smith2020", note.Metadata.ExtraFields["citekey"]);
        Assert.Equal("article", note.Metadata.ExtraFields["bibtex-type"]);
        Assert.Equal("Smith, John", note.Metadata.ExtraFields["author"]);
        Assert.Contains("literature", note.Metadata.Tags);
    }

    [Fact]
    public async Task ImportBibtex_DryRun_DoesNotWriteAnyFiles()
    {
        var tools = CreateTools();

        var result = await tools.import_bibtex(OneEntry, dry_run: true);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("[dry-run]", result);
        Assert.DoesNotContain(_fixture.Index.GetAllNotes(), n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
    }

    [Fact]
    public async Task ImportBibtex_ReimportSameContent_SkipsExistingByCitekeyWithoutDuplicating()
    {
        var tools = CreateTools();

        await tools.import_bibtex(OneEntry);
        await _fixture.Index.RebuildIndexAsync();

        var secondResult = await tools.import_bibtex(OneEntry);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("skipped", secondResult);
        var matches = _fixture.Index.GetAllNotes().Count(n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task ImportBibtex_UpdateExisting_RefreshesFrontmatterAndPreservesBody()
    {
        var tools = CreateTools();

        await tools.import_bibtex(OneEntry);
        await _fixture.Index.RebuildIndexAsync();

        var note = _fixture.Index.GetAllNotes().Single(n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
        var originalRawContent = await File.ReadAllTextAsync(note.FilePath);
        var bodyStart = FrontmatterParser.GetBodyStart(originalRawContent);
        var originalBody = originalRawContent[bodyStart..];

        var updatedEntry = OneEntry.Replace("Journal of Studies", "Journal of Updated Studies");
        var result = await tools.import_bibtex(updatedEntry, update_existing: true);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("updated", result);
        var updatedNote = _fixture.Index.GetAllNotes().Single(n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
        Assert.Equal("Journal of Updated Studies", updatedNote.Metadata.ExtraFields["journal"]);

        var newRawContent = await File.ReadAllTextAsync(updatedNote.FilePath);
        var newBodyStart = FrontmatterParser.GetBodyStart(newRawContent);
        Assert.Equal(originalBody, newRawContent[newBodyStart..]);
    }

    [Fact]
    public async Task ImportBibtex_FilenameCollision_ResolvedWithCitekeySuffix()
    {
        const string sameYearAndTitle = """
            @article{firstpaper,
              author = {A, One},
              title  = {Shared Title},
              year   = {2021},
            }

            @article{secondpaper,
              author = {B, Two},
              title  = {Shared Title},
              year   = {2021},
            }
            """;
        var tools = CreateTools();

        var result = await tools.import_bibtex(sameYearAndTitle);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("[ok] Imported 2 entries", result);
        var notes = _fixture.Index.GetAllNotes()
            .Where(n => n.Metadata.ExtraFields.ContainsKey("citekey") &&
                        (n.Metadata.ExtraFields["citekey"] == "firstpaper" || n.Metadata.ExtraFields["citekey"] == "secondpaper"))
            .ToList();

        Assert.Equal(2, notes.Count);
        Assert.Equal(2, notes.Select(n => n.VaultRelativePath).Distinct().Count());
        Assert.Contains(notes, n => n.Name.Contains("secondpaper"));
    }

    [Fact]
    public async Task ImportBibtex_MalformedEntry_ReportsErrorWithoutAbortingTheImport()
    {
        const string mixed = """
            @article{good,
              author = {Good, Author},
              title  = {A Fine Entry},
              year   = {2020},
            }

            @article{bad,
              author {Missing Equals},
            }
            """;
        var tools = CreateTools();

        var result = await tools.import_bibtex(mixed);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("[ok] Imported 1 entries", result);
        Assert.Contains("1 failed to parse", result);
        Assert.Contains(_fixture.Index.GetAllNotes(), n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "good");
    }

    [Fact]
    public async Task ImportBibtex_FromFilePath_ReadsContentFromDisk()
    {
        var bibPath = Path.Combine(_fixture.VaultPath, "Imports", "library.bib");
        Directory.CreateDirectory(Path.GetDirectoryName(bibPath)!);
        await File.WriteAllTextAsync(bibPath, OneEntry);
        try
        {
            var tools = CreateTools();

            var result = await tools.import_bibtex("Imports/library.bib");
            await _fixture.Index.RebuildIndexAsync();

            Assert.Contains("[ok] Imported 1 entries", result);
            Assert.Contains(_fixture.Index.GetAllNotes(), n => n.Metadata.ExtraFields.GetValueOrDefault("citekey") == "smith2020");
        }
        finally
        {
            File.Delete(bibPath);
        }
    }

    [Fact]
    public async Task ExportCitations_NoCitekeys_ReturnsInfoMessage()
    {
        var tools = CreateTools();

        var result = tools.export_citations(format: "bibtex");

        Assert.Contains("No notes with 'citekey' found", result);
    }

    [Fact]
    public async Task ImportThenExport_RoundTripPreservesEntryFields()
    {
        var tools = CreateTools();

        await tools.import_bibtex(OneEntry);
        await _fixture.Index.RebuildIndexAsync();

        var exported = tools.export_citations(format: "bibtex");

        Assert.Contains("@article{smith2020,", exported);
        Assert.Contains("author = {Smith, John}", exported);
        Assert.Contains("title = {A Study of Things}", exported);
        Assert.Contains("year = {2020}", exported);
        Assert.Contains("journal = {Journal of Studies}", exported);
    }

    [Fact]
    public async Task ImportExportImport_RoundTripYieldsIdenticalEntryOnReimport()
    {
        var tools = CreateTools();

        await tools.import_bibtex(OneEntry);
        await _fixture.Index.RebuildIndexAsync();
        var exported = tools.export_citations(format: "bibtex");

        var exportedStart = exported.IndexOf("@article", StringComparison.Ordinal);
        var bibOnly = exported[exportedStart..];

        var reparsed = BibtexParser.Parse(bibOnly);

        var original = BibtexParser.Parse(OneEntry).Entries.Single();
        var roundTripped = reparsed.Entries.Single();

        Assert.Equal(original.CiteKey, roundTripped.CiteKey);
        Assert.Equal(original.Fields["author"], roundTripped.Fields["author"]);
        Assert.Equal(original.Fields["title"], roundTripped.Fields["title"]);
        Assert.Equal(original.Fields["year"], roundTripped.Fields["year"]);
        Assert.Equal(original.Fields["journal"], roundTripped.Fields["journal"]);
    }

    [Fact]
    public void ExportCitations_InvalidFormat_ReturnsError()
    {
        var tools = CreateTools();

        var result = tools.export_citations(format: "bib");

        Assert.StartsWith("[error]", result);
        Assert.Contains("bibtex", result);
        Assert.Contains("markdown", result);
    }

    [Fact]
    public async Task ExportCitations_Markdown_UsesAllCitekeyVariants()
    {
        await CreateCitationNoteAsync("Literature/Canonical", "citekey", "canonical2024");
        await CreateCitationNoteAsync("Literature/CitationKey", "citation-key", "citation2024");
        await CreateCitationNoteAsync("Literature/Key", "key", "key2024");
        await _fixture.Index.RebuildIndexAsync();

        var result = CreateTools().export_citations(format: "markdown");

        Assert.Contains("`canonical2024`", result);
        Assert.Contains("`citation2024`", result);
        Assert.Contains("`key2024`", result);
    }

    [Fact]
    public async Task ExportCitations_FolderScope_ExcludesNotesOutsideFolder()
    {
        await CreateCitationNoteAsync("Literature/In Scope", "citekey", "inside2024");
        await CreateCitationNoteAsync("Projects/Out Of Scope", "citekey", "outside2024");
        await _fixture.Index.RebuildIndexAsync();

        var result = CreateTools().export_citations(format: "markdown", folder: "Literature");

        Assert.Contains("`inside2024`", result);
        Assert.DoesNotContain("`outside2024`", result);
    }
}
