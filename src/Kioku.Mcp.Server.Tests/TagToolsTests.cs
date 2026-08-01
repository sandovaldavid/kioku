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

    private VaultOrganizationTools CreateOrganizationTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(
            config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        return new VaultOrganizationTools(_fixture.Index, config, hybrid, embedding, vaultConfig);
    }

    [Fact]
    public async Task RemoveTag_LastTag_ClearsTagsSection()
    {
        var tools = CreateTools();
        var name = $"RemoveLastTag-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(name, "Body", tags: ["test/addition"]);
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, remove_tags: "test/addition");
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

        var result = await tools.update_frontmatter(name, remove_tags: "drop");
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

    [Fact]
    public async Task ManageTags_Rename_RewritesTagAcrossNotes()
    {
        var commandTools = CreateTools();
        var orgTools = CreateOrganizationTools();
        var name = $"RenameTag-{Guid.NewGuid():N}";
        await commandTools.create_note(name, "Body", tags: "old-label");
        await _fixture.Index.RebuildIndexAsync();

        var result = await orgTools.manage_tags("rename", old_tag: "old-label", new_tag: "new-label");

        Assert.StartsWith("[ok]", result);
        await _fixture.Index.RebuildIndexAsync();
        var metadata = _fixture.Index.GetNoteByName(name)!.Metadata;
        Assert.Contains("new-label", metadata.Tags);
        Assert.DoesNotContain("old-label", metadata.Tags);
    }

    [Fact]
    public async Task ManageTags_UnknownOperation_IsRejected()
    {
        var tools = CreateOrganizationTools();

        var result = await tools.manage_tags("replace");

        Assert.StartsWith("[error]", result);
        Assert.Contains("normalize", result);
        Assert.Contains("rename", result);
        Assert.Contains("merge", result);
    }

    [Fact]
    public async Task ManageTags_Normalize_RewritesListAndScalarAndReindexes()
    {
        var listName = $"NormalizeList-{Guid.NewGuid():N}";
        var scalarName = $"NormalizeScalar-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(listName, "Body", tags: ["Old_Tag"]);
        await File.WriteAllTextAsync(
            _fixture.GetNotePath(scalarName),
            "---\ntags: Old_Tag\n---\nBody\n- Old_Tag");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateOrganizationTools();

        var result = await tools.manage_tags("normalize");

        Assert.StartsWith("[ok]", result);
        Assert.Contains("old-tag", await File.ReadAllTextAsync(_fixture.GetNotePath(listName)));
        var scalarRaw = await File.ReadAllTextAsync(_fixture.GetNotePath(scalarName));
        Assert.Contains("tags: old-tag", scalarRaw);
        Assert.Contains("- Old_Tag", scalarRaw);
        Assert.Contains("old-tag", _fixture.Index.GetNoteByName(scalarName)!.Metadata.Tags);
        Assert.DoesNotContain("Old_Tag", _fixture.Index.GetNoteByName(scalarName)!.Metadata.Tags);
    }

    [Fact]
    public async Task ManageTags_Merge_RewritesScalarAndRemovesDuplicateTarget()
    {
        var scalarName = $"MergeScalar-{Guid.NewGuid():N}";
        var duplicateName = $"MergeDuplicate-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(
            _fixture.GetNotePath(scalarName),
            "---\ntags: source, keep\n---\nBody");
        await _fixture.CreateNoteAsync(duplicateName, "Body", tags: ["source", "target"]);
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateOrganizationTools();

        var result = await tools.manage_tags("merge", source_tag: "source", target_tag: "target");

        Assert.StartsWith("[ok]", result);
        Assert.Contains("tags: target, keep", await File.ReadAllTextAsync(_fixture.GetNotePath(scalarName)));
        var duplicateTags = _fixture.Index.GetNoteByName(duplicateName)!.Metadata.Tags;
        Assert.Single(duplicateTags, tag => tag.Equals("target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("source", duplicateTags);
        Assert.Contains("target", _fixture.Index.GetNoteByName(scalarName)!.Metadata.Tags);
    }

    [Fact]
    public async Task ManageTags_DryRun_DoesNotModifyFiles()
    {
        var name = $"NormalizeDryRun-{Guid.NewGuid():N}";
        await _fixture.CreateNoteAsync(name, "Body", tags: ["Old_Tag"]);
        await _fixture.Index.RebuildIndexAsync();
        var before = await File.ReadAllTextAsync(_fixture.GetNotePath(name));
        var tools = CreateOrganizationTools();

        var result = await tools.manage_tags("normalize", dry_run: true);

        Assert.Contains("dry_run=true", result);
        Assert.Equal(before, await File.ReadAllTextAsync(_fixture.GetNotePath(name)));
        Assert.Contains("Old_Tag", _fixture.Index.GetNoteByName(name)!.Metadata.Tags);
    }

    [Fact]
    public async Task SuggestTags_ReportsExistingInheritedExcludedAndSuggestions()
    {
        Directory.CreateDirectory(Path.Combine(_fixture.VaultPath, ".kioku"));
        await File.WriteAllTextAsync(
            Path.Combine(_fixture.VaultPath, ".kioku", "config.yml"),
            "auto_tags:\n  inherit:\n    Projects:\n      - inherited-topic\n  exclude_from_tags:\n    - status\n");

        var name = $"Projects/SuggestTags-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(
            _fixture.GetNotePath(name),
            "---\ntags:\n  - project\n---\nAlpha project note about alpha.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateOrganizationTools();

        var result = await tools.suggest_tags(name);

        Assert.Contains("Existing tags:", result);
        Assert.Contains("#project", result);
        Assert.Contains("Inherited tags:", result);
        Assert.Contains("#inherited-topic", result);
        Assert.Contains("Excluded from tags", result);
        Assert.Contains("status", result);
        Assert.Contains("Suggested tags", result);
        Assert.Contains("#alpha", result);
    }
}
