using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for write tools over a real temporary vault.
/// Uses VaultFixture to provision a throwaway vault directory.
/// </summary>
public class WriteToolIntegrationTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public WriteToolIntegrationTests(VaultFixture fixture)
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
    public async Task CreateNote_ThenRead_NoteExists()
    {
        var tools = CreateTools();
        var name = $"IntegrationTest-{Guid.NewGuid():N}";

        var result = await tools.create_note(name, "Integration test body", "test-tag");

        Assert.StartsWith("[ok]", result);
        Assert.True(_fixture.NoteExists(name));
    }

    [Fact]
    public async Task WriteToolRoundTrip_CreateUpdateMoveRenameDelete()
    {
        var tools = CreateTools();
        var baseName = $"RoundTrip-{Guid.NewGuid():N}";

        var createResult = await tools.create_note(baseName, "Initial body", "test");
        Assert.StartsWith("[ok]", createResult);
        await _fixture.Index.RebuildIndexAsync();

        var updateResult = await tools.update_note_content(baseName, "Updated body");
        Assert.StartsWith("[ok]", updateResult);
        var body = await _fixture.ReadNoteBodyAsync(baseName);
        Assert.Contains("Updated body", body);

        var moveResult = await tools.move_note(baseName, "SubFolder");
        Assert.StartsWith("[ok]", moveResult);
        await _fixture.Index.RebuildIndexAsync();

        var movedName = $"SubFolder/{baseName}";
        Assert.True(_fixture.NoteExists(movedName));

        var renamedName = $"SubFolder/{baseName}-renamed";
        var renameResult = await tools.rename_note(movedName, renamedName);
        Assert.StartsWith("[ok]", renameResult);
        Assert.True(_fixture.NoteExists(renamedName));

        await _fixture.Index.RebuildIndexAsync();
        var deleteResult = await tools.delete_note(renamedName);
        Assert.StartsWith("[ok]", deleteResult);
        Assert.False(_fixture.NoteExists(renamedName));
    }

    [Fact]
    public async Task CreateNote_PathTraversal_Throws()
    {
        var tools = CreateTools();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.create_note("../../evil-note", "malicious", ""));
    }

    [Fact]
    public async Task CreateNote_AbsolutePathOutside_Throws()
    {
        var tools = CreateTools();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.create_note("/etc/evil-note", "malicious", ""));
    }

    [Fact]
    public async Task MoveNote_PathTraversal_Throws()
    {
        var tools = CreateTools();
        var name = $"MoveTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", "");
        await _fixture.Index.RebuildIndexAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.move_note(name, "../../outside"));
    }

    [Fact]
    public async Task RenameNote_PathTraversal_Throws()
    {
        var tools = CreateTools();
        var name = $"RenameTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", "");
        await _fixture.Index.RebuildIndexAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.rename_note(name, "../../evil/renamed"));
    }

    [Fact]
    public async Task RenameNote_AbsolutePathOutside_Throws()
    {
        var tools = CreateTools();
        var name = $"RenameAbsTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", "");
        await _fixture.Index.RebuildIndexAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.rename_note(name, "/tmp/evil"));
    }

    [Fact]
    public async Task DeleteNote_SoftDelete_MovesToTrash()
    {
        var tools = CreateTools();
        var name = $"SoftDelete-{Guid.NewGuid():N}";
        await tools.create_note(name, "Soft delete body", "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.delete_note(name);

        Assert.StartsWith("[ok]", result);
        Assert.Contains("moved to trash", result);
        Assert.False(_fixture.NoteExists(name));

        var trashDir = Path.Combine(_fixture.VaultPath, ".trash");
        Assert.True(Directory.Exists(trashDir));
        var trashFiles = Directory.GetFiles(trashDir, "*.md");
        Assert.True(trashFiles.Length > 0);
    }

    [Fact]
    public async Task DeleteNote_PermanentDelete_RemovesFile()
    {
        var tools = CreateTools();
        var name = $"PermDelete-{Guid.NewGuid():N}";
        await tools.create_note(name, "Permanent delete body", "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.delete_note(name, permanent: true);

        Assert.StartsWith("[ok]", result);
        Assert.Contains("permanently deleted", result);
        Assert.False(_fixture.NoteExists(name));
    }

    [Fact]
    public async Task DeleteNote_DryRun_DoesNotDelete()
    {
        var tools = CreateTools();
        var name = $"DryRunDelete-{Guid.NewGuid():N}";
        await tools.create_note(name, "Dry run body", "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.delete_note(name, dry_run: true);

        Assert.StartsWith("[info]", result);
        Assert.Contains("Would", result);
        Assert.True(_fixture.NoteExists(name));
    }

    [Fact]
    public async Task UpdateFrontmatter_PreservesExistingAliasesAndUpdated()
    {
        var tools = CreateTools();
        var name = $"AliasesTest-{Guid.NewGuid():N}";
        var filePath = _fixture.GetNotePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var content = "---\naliases:\n  - My Alias\nupdated: 2020-01-01\ntype: note\nstatus: draft\ndate: 2020-01-01\n---\nBody text.";
        await File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, status: "done");

        Assert.StartsWith("[ok]", result);
        var raw = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);
        Assert.Contains("aliases:", raw);
        Assert.Contains("My Alias", raw);
        Assert.Contains("updated: 2020-01-01", raw);
        Assert.Contains("status: done", raw);
    }

    [Fact]
    public async Task UpdateFrontmatter_WritesFile_NoByteOrderMark()
    {
        var tools = CreateTools();
        var name = $"BomTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", "", type: "note", status: "draft");
        await _fixture.Index.RebuildIndexAsync();

        await tools.update_frontmatter(name, status: "done");

        var bytes = await File.ReadAllBytesAsync(_fixture.GetNotePath(name));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public async Task UpdateFrontmatter_ThenIndexLookup_ReflectsChangeImmediately()
    {
        var tools = CreateTools();
        var name = $"ReindexTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", "", type: "note", status: "draft");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, status: "done");

        Assert.StartsWith("[ok]", result);
        var found = _fixture.Index.GetNote(_fixture.GetNotePath(name));
        Assert.NotNull(found);
        Assert.Equal("done", found!.Metadata.Status);
    }
}
