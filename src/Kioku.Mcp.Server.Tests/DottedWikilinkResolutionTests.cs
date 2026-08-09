using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class DottedWikilinkResolutionTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private KiokuConfiguration _config = null!;
    private VaultConfigService _vaultConfig = null!;
    private EmbeddingService _embedding = null!;
    private HybridSearchService _hybrid = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();

        await _fixture.CreateNoteAsync(
            "20-execution/atena/web.admin/web.admin",
            "# Heading\nDotted Cortex reproduction.");
        await _fixture.CreateNoteAsync("some.note.with.multiple.dots", "Multiple dots.");
        await _fixture.CreateNoteAsync("web", "Similar prefix that must not win.");
        await _fixture.CreateNoteAsync("A/release.1", "Duplicate dotted basename A.");
        await _fixture.CreateNoteAsync("B/release.1", "Duplicate dotted basename B.");
        await _fixture.CreateNoteAsync(
            "Dotted Source",
            """
            [[web.admin]]
            [[web.admin.md]]
            [[web.admin#Heading]]
            [[some.note.with.multiple.dots]]
            [[some.note.with.multiple.dots.md]]
            [[release.1]]
            """);

        await _fixture.Index.RebuildIndexAsync();

        _config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        _vaultConfig = new VaultConfigService(_config, NullLogger<VaultConfigService>.Instance);
        _embedding = new EmbeddingService(
            _config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        _hybrid = new HybridSearchService(_fixture.Index, _embedding);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [InlineData("web.admin", "20-execution/atena/web.admin/web.admin.md")]
    [InlineData("web.admin.md", "20-execution/atena/web.admin/web.admin.md")]
    [InlineData("some.note.with.multiple.dots", "some.note.with.multiple.dots.md")]
    [InlineData("some.note.with.multiple.dots.md", "some.note.with.multiple.dots.md")]
    public void ResolveLink_DottedBasename_PreservesAllNonMarkdownDots(
        string target,
        string expectedPath)
    {
        var result = _fixture.Index.ResolveLinkResult(RequiredNote("Dotted Source"), target);

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedPath, result.Note?.VaultRelativePath);
    }

    [Fact]
    public void ResolveLink_DottedBasename_DoesNotCollapseToSimilarPrefix()
    {
        var result = _fixture.Index.ResolveLinkResult(RequiredNote("Dotted Source"), "web.admin");

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal("web.admin", result.Note?.Name);
        Assert.NotEqual("web", result.Note?.Name);
    }

    [Fact]
    public void ResolveLink_DuplicateDottedBasename_RemainsAmbiguous()
    {
        var result = _fixture.Index.ResolveLinkResult(RequiredNote("Dotted Source"), "release.1");

        Assert.Equal(VaultLinkResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Note);
        Assert.Null(result.CanonicalTargetPath);
    }

    [Fact]
    public void ResolveLink_DottedBasenameWithFragment_RetainsFragment()
    {
        var result = _fixture.Index.ResolveLinkResult(
            RequiredNote("Dotted Source"),
            "web.admin#Heading");

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Equal("20-execution/atena/web.admin/web.admin.md", result.Note?.VaultRelativePath);
        Assert.Equal("#Heading", result.Fragment);
    }

    [Fact]
    public async Task ResolveLink_UnindexedDottedMarkdownBasename_UsesSameSemantics()
    {
        var path = _fixture.GetNotePath("Unindexed/excluded.note");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "Exists on disk but is not in the current index.");

        var result = _fixture.Index.ResolveLinkResult(RequiredNote("Dotted Source"), "excluded.note");

        Assert.Equal(VaultLinkResolutionStatus.Resolved, result.Status);
        Assert.Null(result.Note);
        Assert.Equal("Unindexed/excluded.note", result.CanonicalTargetPath);
    }

    [Fact]
    public async Task CrossSurface_DottedBasename_UsesOneCanonicalIdentity()
    {
        var source = RequiredNote("Dotted Source");
        var dotted = RequiredNote("web.admin");

        Assert.Equal(
            "20-execution/atena/web.admin/web.admin.md",
            _fixture.Index.ResolveNote("web.admin")?.VaultRelativePath);
        Assert.Equal(dotted.FilePath, NoteHelpers.ResolveNote("web.admin", _fixture.Index)?.FilePath);
        Assert.Contains(_fixture.Index.GetBacklinks(dotted), note => note.FilePath == source.FilePath);

        var noteTools = new NoteQueryTools(
            new NoteQueryService(_fixture.Index, _config, _embedding, _hybrid));

        var search = await noteTools.search_notes("web.admin", mode: "keyword");
        Assert.Contains("web.admin", search, StringComparison.OrdinalIgnoreCase);

        var read = await noteTools.read_note("web.admin");
        Assert.Contains("Dotted Cortex reproduction", read);

        var inbound = noteTools.get_links("web.admin", direction: "in");
        Assert.Contains("Dotted Source", inbound, StringComparison.OrdinalIgnoreCase);

        var outgoingJson = noteTools.get_links("Dotted Source", direction: "out", format: "json");
        using (var linksDocument = JsonDocument.Parse(outgoingJson))
        {
            var resolutions = linksDocument.RootElement
                .GetProperty("outgoing_link_resolutions")
                .EnumerateArray()
                .ToList();

            var implicitResolution = resolutions.Single(item =>
                item.GetProperty("raw_target").GetString() == "web.admin");
            Assert.Equal("resolved", implicitResolution.GetProperty("status").GetString());
            Assert.Equal(
                "20-execution/atena/web.admin/web.admin",
                implicitResolution.GetProperty("canonical_target_path").GetString());

            var explicitResolution = resolutions.Single(item =>
                item.GetProperty("raw_target").GetString() == "web.admin.md");
            Assert.Equal("resolved", explicitResolution.GetProperty("status").GetString());
            Assert.Equal(
                "20-execution/atena/web.admin/web.admin",
                explicitResolution.GetProperty("canonical_target_path").GetString());

            var fragmentResolution = resolutions.Single(item =>
                item.GetProperty("raw_target").GetString() == "web.admin#Heading");
            Assert.Equal("resolved", fragmentResolution.GetProperty("status").GetString());
            Assert.Equal("#Heading", fragmentResolution.GetProperty("fragment").GetString());
        }

        var auditTools = new VaultOrganizationTools(
            _fixture.Index,
            _config,
            _hybrid,
            _embedding,
            _vaultConfig);
        var audit = await auditTools.audit_vault();
        var auditText = audit.Content.OfType<TextContentBlock>().Single().Text;
        Assert.DoesNotContain("[[web.admin]]", auditText);
        Assert.DoesNotContain("[[web.admin.md]]", auditText);
        Assert.DoesNotContain("[[web.admin#Heading]]", auditText);

        var graph = new KnowledgeGraphTools(_fixture.Index).get_concept_map(
            "Dotted Source",
            depth: 1,
            max_nodes: 50);
        var graphJson = graph[graph.IndexOf('{')..];
        using var graphDocument = JsonDocument.Parse(graphJson);
        var edges = graphDocument.RootElement.GetProperty("edges").EnumerateArray().ToList();

        Assert.Contains(edges, edge =>
            edge.GetProperty("target").GetString() == "20-execution/atena/web.admin/web.admin.md" &&
            edge.GetProperty("type").GetString() == "link");

        var component = _fixture.Index.FindConnectedComponent(
            source,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(component, note => note.FilePath == dotted.FilePath);
    }

    private Note RequiredNote(string nameOrPath) =>
        _fixture.Index.ResolveNote(nameOrPath) ??
        throw new InvalidOperationException($"Missing test note: {nameOrPath}");
}
