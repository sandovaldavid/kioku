using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class WikilinkResolutionConsistencyTests : IAsyncLifetime
{
    private string _vaultPath = null!;
    private KiokuConfiguration _config = null!;
    private VaultConfigService _vaultConfig = null!;
    private VaultIndexService _index = null!;
    private EmbeddingService _embedding = null!;
    private HybridSearchService _hybrid = null!;

    public async Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-wikilinks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);

        await WriteNoteAsync("Hub", "Hub body", aliases: ["linktree"]);
        await WriteNoteAsync("Identity", "Identity body", aliases: ["identidad-marca-personal"]);
        await WriteNoteAsync("target", "Root target");
        await WriteNoteAsync("Unique", "Unique basename target");
        await WriteNoteAsync("HeadingTarget", "# Heading\nBlock ^block");
        await WriteNoteAsync("filename-with-#-character", "# Heading\nLiteral hash filename");
        await WriteNoteAsync("projects/target", "Parent target");
        await WriteNoteAsync("projects/yukidoke-api", "API target");
        await WriteNoteAsync("projects/yukidoke-web", "Web target");
        await WriteNoteAsync("projects/current/local", "Local target");
        await WriteNoteAsync("A/Duplicate", "Duplicate A");
        await WriteNoteAsync("B/Duplicate", "Duplicate B");
        await WriteNoteAsync("AliasA", "Alias A", aliases: ["shared-alias"]);
        await WriteNoteAsync("AliasB", "Alias B", aliases: ["shared-alias"]);

        await WriteNoteAsync(
            "projects/current/source",
            """
            [[linktree]]
            [[identidad-marca-personal]]
            [[../yukidoke-api]]
            [[../yukidoke-web]]
            [[../target]]
            [[../../target]]
            [[./local]]
            [[/target]]
            [[projects/yukidoke-api]]
            [[Unique]]
            [[filename-with-#-character]]
            [[filename-with-#-character#Heading]]
            [[HeadingTarget#Heading]]
            [[HeadingTarget#^block]]
            [[Missing Target]]
            [[Duplicate]]
            [[shared-alias]]
            [[unclosed link
            """);

        _config = new KiokuConfiguration { VaultPath = _vaultPath };
        _vaultConfig = new VaultConfigService(_config, NullLogger<VaultConfigService>.Instance);
        _index = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            _config,
            vaultConfig: _vaultConfig);
        await _index.RebuildIndexAsync();

        _embedding = new EmbeddingService(
            _config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        _hybrid = new HybridSearchService(_index, _embedding);
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_vaultPath))
            {
                Directory.Delete(_vaultPath, recursive: true);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("../target", "projects/target.md")]
    [InlineData("../../target", "target.md")]
    [InlineData("./local", "projects/current/local.md")]
    [InlineData("/target", "target.md")]
    [InlineData("projects/yukidoke-api", "projects/yukidoke-api.md")]
    [InlineData("../yukidoke-api", "projects/yukidoke-api.md")]
    [InlineData("../yukidoke-web", "projects/yukidoke-web.md")]
    [InlineData("Unique", "Unique.md")]
    [InlineData("linktree", "Hub.md")]
    [InlineData("identidad-marca-personal", "Identity.md")]
    public void ResolveLink_ValidRelativePathBasenameAndAliasTargets_AreResolved(string target, string expectedPath)
    {
        var source = RequiredNote("projects/current/source");

        var result = _index.ResolveLinkResult(source, target);

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedPath, result.Note?.VaultRelativePath);
    }

    [Fact]
    public void ResolveLink_AmbiguousBasename_IsNotArbitrarilySelected()
    {
        var result = _index.ResolveLinkResult(RequiredNote("projects/current/source"), "Duplicate");

        Assert.Equal(VaultLinkResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Note);
    }

    [Fact]
    public void ResolveLink_AmbiguousAlias_IsNotArbitrarilySelected()
    {
        var result = _index.ResolveLinkResult(RequiredNote("projects/current/source"), "shared-alias");

        Assert.Equal(VaultLinkResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Note);
    }

    [Fact]
    public void ResolveLink_LiteralHashFilename_WinsBeforeFragmentParsing()
    {
        var result = _index.ResolveLinkResult(
            RequiredNote("projects/current/source"),
            "filename-with-#-character");

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal("filename-with-#-character.md", result.Note?.VaultRelativePath);
        Assert.Null(result.Fragment);
    }

    [Fact]
    public void ResolveLink_LiteralHashFilenameWithFragment_UsesLongestExistingFilename()
    {
        var result = _index.ResolveLinkResult(
            RequiredNote("projects/current/source"),
            "filename-with-#-character#Heading");

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal("filename-with-#-character.md", result.Note?.VaultRelativePath);
        Assert.Equal("#Heading", result.Fragment);
    }

    [Theory]
    [InlineData("HeadingTarget#Heading", "#Heading")]
    [InlineData("HeadingTarget#^block", "#^block")]
    public void ResolveLink_HeadingAndBlockFragments_RetainFragment(string target, string expectedFragment)
    {
        var result = _index.ResolveLinkResult(RequiredNote("projects/current/source"), target);

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal("HeadingTarget.md", result.Note?.VaultRelativePath);
        Assert.Equal(expectedFragment, result.Fragment);
    }

    [Fact]
    public void ResolveLink_MissingTarget_IsMissing()
    {
        var result = _index.ResolveLinkResult(RequiredNote("projects/current/source"), "Missing Target");

        Assert.Equal(VaultLinkResolutionStatus.Missing, result.Status);
    }

    [Fact]
    public void ResolveLink_EmptyTarget_IsMalformed()
    {
        var result = _index.ResolveLinkResult(RequiredNote("projects/current/source"), "   ");

        Assert.Equal(VaultLinkResolutionStatus.Malformed, result.Status);
    }

    [Fact]
    public void ResolveLink_PathTraversalOutsideVault_IsMalformed()
    {
        var result = _index.ResolveLinkResult(RequiredNote("projects/current/source"), "../../../outside");

        Assert.Equal(VaultLinkResolutionStatus.Malformed, result.Status);
    }

    [Fact]
    public async Task CrossSurfaceFixture_AgreesForAliasesAndRelativeLinks()
    {
        var source = RequiredNote("projects/current/source");
        var hub = RequiredNote("Hub");
        var api = RequiredNote("projects/yukidoke-api");

        Assert.Equal(hub.FilePath, _index.ResolveNote("linktree")?.FilePath);
        Assert.Equal(hub.FilePath, NoteHelpers.ResolveNote("linktree", _index)?.FilePath);

        Assert.Contains(_index.GetBacklinks(hub), note => note.FilePath == source.FilePath);
        Assert.Contains(_index.GetBacklinks(api), note => note.FilePath == source.FilePath);

        var noteTools = new NoteQueryTools(new NoteQueryService(_index, _config, null!, null!));
        var read = await noteTools.read_note("linktree");
        Assert.Contains("Hub body", read);

        var inbound = noteTools.get_links("linktree", direction: "in");
        Assert.Contains("source", inbound, StringComparison.OrdinalIgnoreCase);

        var auditTools = new VaultOrganizationTools(
            _index,
            _config,
            _hybrid,
            _embedding,
            _vaultConfig);
        var audit = await auditTools.audit_vault();
        Assert.Contains("Broken wikilinks (1)", audit);
        Assert.Contains("Ambiguous wikilinks (2)", audit);
        Assert.Contains("Malformed wikilinks (1)", audit);
        Assert.Contains("Missing Target", audit);
        Assert.DoesNotContain("[[linktree]]", audit);
        Assert.DoesNotContain("[[identidad-marca-personal]]", audit);
        Assert.DoesNotContain("[[../yukidoke-api]]", audit);
        Assert.DoesNotContain("[[../yukidoke-web]]", audit);
        Assert.DoesNotContain("[[filename-with-#-character#Heading]]", audit);

        var graph = new KnowledgeGraphTools(_index).get_concept_map(
            "projects/current/source",
            depth: 1,
            max_nodes: 50);
        var graphJson = graph[graph.IndexOf('{')..];
        using var document = JsonDocument.Parse(graphJson);
        var edges = document.RootElement.GetProperty("edges").EnumerateArray().ToList();

        Assert.Contains(edges, edge =>
            edge.GetProperty("target").GetString() == "Hub.md" &&
            edge.GetProperty("type").GetString() == "link");
        Assert.Contains(edges, edge =>
            edge.GetProperty("target").GetString() == "projects/yukidoke-api.md" &&
            edge.GetProperty("type").GetString() == "link");
        Assert.Contains(edges, edge =>
            edge.GetProperty("target").GetString() == "projects/yukidoke-web.md" &&
            edge.GetProperty("type").GetString() == "link");
        Assert.Contains(edges, edge =>
            edge.GetProperty("target").GetString() == "Duplicate" &&
            edge.GetProperty("type").GetString() == "ambiguous-link");

        var component = _index.FindConnectedComponent(
            source,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(component, note => note.FilePath == hub.FilePath);
        Assert.Contains(component, note => note.FilePath == api.FilePath);
    }

    private Note RequiredNote(string nameOrPath) =>
        _index.ResolveNote(nameOrPath) ?? throw new InvalidOperationException($"Missing test note: {nameOrPath}");

    private async Task WriteNoteAsync(string name, string body, IReadOnlyList<string>? aliases = null)
    {
        var path = Path.Combine(
            _vaultPath,
            (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name : name + ".md")
                .Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var frontmatter = aliases is null || aliases.Count == 0
            ? string.Empty
            : "---\naliases:\n" + string.Join(string.Empty, aliases.Select(alias => $"  - {alias}\n")) + "---\n";
        await File.WriteAllTextAsync(path, frontmatter + body, Encoding.UTF8);
    }
}
