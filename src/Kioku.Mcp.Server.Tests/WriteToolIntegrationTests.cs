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

    private NoteCommandTools CreateTools(bool allowPermanentDelete = false)
    {
        var config = new KiokuConfiguration
        {
            VaultPath = _fixture.VaultPath,
            AllowPermanentDelete = allowPermanentDelete,
        };
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

    [Fact]
    public async Task CreateNote_ThenRead_NoteExists()
    {
        var tools = CreateTools();
        var name = $"IntegrationTest-{Guid.NewGuid():N}";

        var result = await tools.create_note(name, "Integration test body", tags: "test-tag");

        Assert.StartsWith("[ok]", result);
        Assert.True(_fixture.NoteExists(name));
    }

    [Fact]
    public async Task CreateNote_ZettelKind_UsesStructuredCreation()
    {
        var tools = CreateTools();

        var result = await tools.create_note(
            "A structured idea", "The idea body", kind: "zettel", folder: "Zettelkasten");

        Assert.StartsWith("[ok] Zettel created:", result);
        Assert.Single(Directory.GetFiles(_fixture.GetFolderPath("Zettelkasten"), "*.md"));
    }

    [Fact]
    public async Task WriteToolRoundTrip_CreateUpdateMoveRenameDelete()
    {
        var tools = CreateTools();
        var baseName = $"RoundTrip-{Guid.NewGuid():N}";

        var createResult = await tools.create_note(baseName, "Initial body", tags: "test");
        Assert.StartsWith("[ok]", createResult);
        await _fixture.Index.RebuildIndexAsync();

        var updateResult = await tools.edit_note(baseName, "Updated body");
        Assert.StartsWith("[ok]", updateResult);
        var body = await _fixture.ReadNoteBodyAsync(baseName);
        Assert.Contains("Updated body", body);

        var moveResult = await tools.move_note(baseName, "SubFolder");
        Assert.StartsWith("[ok]", moveResult);
        await _fixture.Index.RebuildIndexAsync();

        var movedName = $"SubFolder/{baseName}";
        Assert.True(_fixture.NoteExists(movedName));

        var renamedName = $"SubFolder/{baseName}-renamed";
        var renameResult = await tools.move_note(movedName, new_name: renamedName);
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
            () => tools.create_note("../../evil-note", "malicious", tags: ""));
    }

    [Fact]
    public async Task CreateNote_AbsolutePathOutside_Throws()
    {
        var tools = CreateTools();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.create_note("/etc/evil-note", "malicious", tags: ""));
    }

    [Fact]
    public async Task MoveNote_PathTraversal_Throws()
    {
        var tools = CreateTools();
        var name = $"MoveTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.move_note(name, destination_folder: "../../outside"));
    }

    [Fact]
    public async Task RenameNote_PathTraversal_Throws()
    {
        var tools = CreateTools();
        var name = $"RenameTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.move_note(name, new_name: "../../evil/renamed"));
    }

    [Fact]
    public async Task RenameNote_AbsolutePathOutside_Throws()
    {
        var tools = CreateTools();
        var name = $"RenameAbsTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.move_note(name, new_name: "/tmp/evil"));
    }

    [Fact]
    public async Task DeleteNote_SoftDelete_MovesToTrash()
    {
        var tools = CreateTools();
        var name = $"SoftDelete-{Guid.NewGuid():N}";
        await tools.create_note(name, "Soft delete body", tags: "");
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
    public async Task DeleteNote_ConcurrentSameBasename_KeepsBothRecoverable()
    {
        var tools = CreateTools();
        var trashDir = Path.Combine(_fixture.VaultPath, ".trash");

        // The soft-delete race is timing-dependent, so repeat to make a regression deterministic.
        for (var iteration = 0; iteration < 15; iteration++)
        {
            var basename = $"concurrent-{Guid.NewGuid():N}";
            var pathA = $"ConcurrentA/{basename}";
            var pathB = $"ConcurrentB/{basename}";
            var bodyA = $"body-A-{basename}";
            var bodyB = $"body-B-{basename}";

            await tools.create_note(pathA, bodyA, tags: "");
            await tools.create_note(pathB, bodyB, tags: "");
            await _fixture.Index.RebuildIndexAsync();

            var results = await Task.WhenAll(
                tools.delete_note(pathA),
                tools.delete_note(pathB));

            Assert.All(results, r => Assert.StartsWith("[ok]", r));

            // Both notes must survive in the trash under distinct names — the second move must
            // never overwrite the first when the basenames collide.
            var trashed = Directory.GetFiles(trashDir, $"{basename}*.md");
            Assert.Equal(2, trashed.Length);

            var contents = trashed.Select(File.ReadAllText).ToList();
            Assert.Contains(contents, c => c.Contains(bodyA));
            Assert.Contains(contents, c => c.Contains(bodyB));
        }
    }

    [Fact]
    public async Task DeleteNote_PermanentDelete_RemovesFile()
    {
        var tools = CreateTools(allowPermanentDelete: true);
        var name = $"PermDelete-{Guid.NewGuid():N}";
        await tools.create_note(name, "Permanent delete body", tags: "");
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
        await tools.create_note(name, "Dry run body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.delete_note(name, dry_run: true);

        Assert.StartsWith("[info]", result);
        Assert.Contains("Would", result);
        Assert.True(_fixture.NoteExists(name));
    }

    [Fact]
    public async Task ManageTrash_RestoreRejectsTraversal()
    {
        var tools = CreateTools();

        var result = await tools.manage_trash(
            action: "restore", note: "../../outside.md");

        Assert.StartsWith("[error]", result);
        Assert.Contains("relative path", result);
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
        await tools.create_note(name, "Body", tags: "", type: "note", status: "draft");
        await _fixture.Index.RebuildIndexAsync();

        await tools.update_frontmatter(name, status: "done");

        var bytes = await File.ReadAllBytesAsync(_fixture.GetNotePath(name));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public async Task EditNote_AppendMode_AddsAtEnd()
    {
        var tools = CreateTools();
        var name = $"AppendTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "First line", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.edit_note(name, "Appended line", mode: "append");

        Assert.StartsWith("[ok]", result);
        var body = await _fixture.ReadNoteBodyAsync(name);
        Assert.Contains("First line", body);
        Assert.True(body.IndexOf("Appended line", StringComparison.Ordinal) >
                    body.IndexOf("First line", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EditNote_PrependMode_KeepsFrontmatterFirst()
    {
        var tools = CreateTools();
        var name = $"PrependTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Original body", tags: "keep-me");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.edit_note(name, "Prepended line", mode: "prepend");

        Assert.StartsWith("[ok]", result);
        var raw = await File.ReadAllTextAsync(_fixture.GetNotePath(name));
        Assert.StartsWith("---", raw);
        var body = await _fixture.ReadNoteBodyAsync(name);
        Assert.True(body.IndexOf("Prepended line", StringComparison.Ordinal) <
                    body.IndexOf("Original body", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EditNote_ExplicitReplaceMode_ReplacesBodyKeepsFrontmatter()
    {
        var tools = CreateTools();
        var name = $"ReplaceTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Old body", tags: "keep-me");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.edit_note(name, "New body", mode: "replace");

        Assert.StartsWith("[ok]", result);
        var raw = await File.ReadAllTextAsync(_fixture.GetNotePath(name));
        Assert.Contains("keep-me", raw);
        Assert.Contains("New body", raw);
        Assert.DoesNotContain("Old body", raw);
    }

    [Fact]
    public async Task EditNote_UnknownMode_ReturnsError()
    {
        var tools = CreateTools();
        var name = $"BadModeTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.edit_note(name, "x", mode: "insert");

        Assert.StartsWith("[error]", result);
        Assert.Contains("replace", result);
    }

    [Fact]
    public async Task UpdateFrontmatter_AddTags_MergesWithExisting()
    {
        var tools = CreateTools();
        var name = $"AddTagsTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", tags: "alpha");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, add_tags: "beta, alpha");

        Assert.StartsWith("[ok]", result);
        var found = _fixture.Index.GetNote(_fixture.GetNotePath(name));
        Assert.NotNull(found);
        Assert.Contains("alpha", found!.Metadata.Tags);
        Assert.Contains("beta", found.Metadata.Tags);
        Assert.Equal(2, found.Metadata.Tags.Count(t => t is "alpha" or "beta"));
    }

    [Fact]
    public async Task ManageTrash_ListThenRestore_RoundTrips()
    {
        var tools = CreateTools();
        var name = $"TrashRoundTrip-{Guid.NewGuid():N}";
        await tools.create_note(name, "Trash me", tags: "");
        await _fixture.Index.RebuildIndexAsync();
        await tools.delete_note(name);

        var listResult = await tools.manage_trash(action: "list");
        Assert.StartsWith("[ok]", listResult);
        Assert.Contains(name, listResult);

        var restoreResult = await tools.manage_trash(action: "restore", note: name);
        Assert.StartsWith("[ok]", restoreResult);
        Assert.True(File.Exists(Path.Combine(_fixture.VaultPath, $"{name}.md")));
    }

    [Fact]
    public async Task CreateNote_LiteratureKind_UsesStructuredCreation()
    {
        var tools = CreateTools();

        var result = await tools.create_note(
            "A Great Paper", kind: "literature", author: "Doe", year: "2024");

        Assert.StartsWith("[ok] Literature note created:", result);
        Assert.True(File.Exists(Path.Combine(_fixture.VaultPath, "Literature", "2024-A-Great-Paper.md")));
    }

    [Fact]
    public async Task CreateNote_MocKind_GeneratesIndexNote()
    {
        var tools = CreateTools();
        var folder = $"MocKind-{Guid.NewGuid():N}";
        await tools.create_note($"{folder}/Inner", "Inner body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.create_note(kind: "moc", folder: folder);

        Assert.StartsWith("[ok] MOC created:", result);
        Assert.True(File.Exists(Path.Combine(_fixture.VaultPath, folder, $"{folder}-MOC.md")));
    }

    [Fact]
    public async Task CreateNote_FolderReadmeKind_GeneratesFolderNote()
    {
        var tools = CreateTools();
        var folder = $"ReadmeKind-{Guid.NewGuid():N}";
        await tools.create_note($"{folder}/Inner", "Inner body", tags: "");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.create_note(kind: "folder-readme", folder: folder);

        Assert.StartsWith("[ok] Folder note created:", result);
        Assert.True(File.Exists(Path.Combine(_fixture.VaultPath, folder, $"{folder}.md")));
    }

    [Fact]
    public async Task UpdateFrontmatter_ThenIndexLookup_ReflectsChangeImmediately()
    {
        var tools = CreateTools();
        var name = $"ReindexTest-{Guid.NewGuid():N}";
        await tools.create_note(name, "Body", tags: "", type: "note", status: "draft");
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.update_frontmatter(name, status: "done");

        Assert.StartsWith("[ok]", result);
        var found = _fixture.Index.GetNote(_fixture.GetNotePath(name));
        Assert.NotNull(found);
        Assert.Equal("done", found!.Metadata.Status);
    }
}
