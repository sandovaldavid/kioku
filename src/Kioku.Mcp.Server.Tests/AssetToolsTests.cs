using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class AssetToolsTests : IAsyncLifetime
{
    private string _vaultPath = null!;
    private VaultIndexService _index = null!;

    public async Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);
        _index = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _vaultPath });
        await _index.RebuildIndexAsync();
    }

    public Task DisposeAsync()
    {
        _index.Dispose();
        if (Directory.Exists(_vaultPath))
        {
            Directory.Delete(_vaultPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task TidyAttachments_MovesAndPreservesEmbeddedLinkDetails()
    {
        await WriteFileAsync("Source/diagram.png", "asset");
        await WriteFileAsync(
            "Note.md",
            "![[Source/diagram.png|400]] and [[Source/diagram.png#^block|alias]] " +
            "and [diagram](Source/diagram.png \"title\")");
        await _index.RebuildIndexAsync();

        var result = await CreateTools().tidy_attachments(target_folder: "Attachments");

        Assert.StartsWith("[ok]", result);
        Assert.False(File.Exists(PathFor("Source/diagram.png")));
        Assert.True(File.Exists(PathFor("Attachments/diagram.png")));
        var note = await File.ReadAllTextAsync(PathFor("Note.md"));
        Assert.Contains("![[Attachments/diagram.png|400]]", note);
        Assert.Contains("[[Attachments/diagram.png#^block|alias]]", note);
        Assert.Contains("[diagram](Attachments/diagram.png \"title\")", note);
    }

    [Fact]
    public async Task TidyAttachments_NormalizationStagesCollidingRenames()
    {
        await WriteFileAsync("Attachments/attachment-002.png", "first");
        await WriteFileAsync("Attachments/z.png", "second");
        await WriteFileAsync("Note.md", "![[Attachments/attachment-002.png|old]] ![[Attachments/z.png]]");
        await _index.RebuildIndexAsync();

        await CreateTools().tidy_attachments(normalize_names: true, target_folder: "Attachments");

        Assert.Equal("first", await File.ReadAllTextAsync(PathFor("Attachments/attachment-001.png")));
        Assert.Equal("second", await File.ReadAllTextAsync(PathFor("Attachments/attachment-002.png")));
        var note = await File.ReadAllTextAsync(PathFor("Note.md"));
        Assert.Contains("![[Attachments/attachment-001.png|old]]", note);
        Assert.Contains("![[Attachments/attachment-002.png]]", note);
    }

    [Fact]
    public async Task TidyAttachments_MovingCollisionGetsUniqueNameWithoutOverwrite()
    {
        await WriteFileAsync("Attachments/diagram.png", "existing");
        await WriteFileAsync("Source/diagram.png", "incoming");
        await WriteFileAsync("Note.md", "![[Source/diagram.png]]");
        await _index.RebuildIndexAsync();

        await CreateTools().tidy_attachments(target_folder: "Attachments");

        Assert.Equal("existing", await File.ReadAllTextAsync(PathFor("Attachments/diagram.png")));
        Assert.Equal("incoming", await File.ReadAllTextAsync(PathFor("Attachments/diagram_1.png")));
        Assert.Contains("![[Attachments/diagram_1.png]]", await File.ReadAllTextAsync(PathFor("Note.md")));
    }

    [Fact]
    public async Task TidyAttachments_DryRunDoesNotCreateTargetOrMoveScopedFiles()
    {
        await WriteFileAsync("Attachments-old/diagram.png", "asset");
        await WriteFileAsync("Other/diagram.png", "asset");
        await _index.RebuildIndexAsync();

        var result = await CreateTools().tidy_attachments(target_folder: "Attachments", dry_run: true);

        Assert.StartsWith("[info] dry_run=true", result);
        Assert.False(Directory.Exists(PathFor("Attachments")));
        Assert.True(File.Exists(PathFor("Attachments-old/diagram.png")));
        Assert.True(File.Exists(PathFor("Other/diagram.png")));
        Assert.Contains("Attachments-old/diagram.png", result);
        Assert.Contains("Other/diagram.png", result);
    }

    [Fact]
    public async Task FindOrphanAssets_DryRun_DoesNotFlagAssetsReferencedWithAliasFragmentOrTitle()
    {
        await WriteFileAsync("diagram.png", "asset");
        await WriteFileAsync("doc.pdf", "asset");
        await WriteFileAsync("photo.jpg", "asset");
        await WriteFileAsync("truly-orphan.png", "asset");
        await WriteFileAsync(
            "Note.md",
            "![[diagram.png|300x200]] and ![[doc.pdf#page=2]] and [img](photo.jpg \"title\")");
        await _index.RebuildIndexAsync();

        var result = await CreateTools().find_orphan_assets(dry_run: true);

        Assert.Contains("truly-orphan.png", result);
        Assert.DoesNotContain("diagram.png", result);
        Assert.DoesNotContain("doc.pdf", result);
        Assert.DoesNotContain("photo.jpg", result);
    }

    [Fact]
    public async Task FindOrphanAssets_NotDryRun_MovesOnlyTrueOrphansAndKeepsReferencedAssets()
    {
        await WriteFileAsync("diagram.png", "asset");
        await WriteFileAsync("truly-orphan.png", "asset");
        await WriteFileAsync("Note.md", "![[diagram.png|300x200]]");
        await _index.RebuildIndexAsync();

        var result = await CreateTools().find_orphan_assets(dry_run: false);

        Assert.StartsWith("[ok] Moved 1 orphan", result);
        Assert.True(File.Exists(PathFor("diagram.png")));
        Assert.False(File.Exists(PathFor("truly-orphan.png")));
        Assert.True(File.Exists(PathFor(".trash/.kioku-orphans/truly-orphan.png")));
    }

    private AssetTools CreateTools() => new(
        _index,
        new KiokuConfiguration { VaultPath = _vaultPath });

    private string PathFor(string relativePath) => Path.Combine(
        _vaultPath,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private async Task WriteFileAsync(string relativePath, string content)
    {
        var path = PathFor(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }
}
